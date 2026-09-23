using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Recent;

namespace Keypaste.App.ViewModels;

/// <summary>What an access change does to the keyfile, as the Settings form offers it.</summary>
internal enum KeyfileChoice
{
    Keep = 0,
    Attach = 1,
    Remove = 2,
}

/// <summary>
/// Changing what unlocks the open vault: its master password, its keyfile, or both, behind the
/// current password and a confirmation that says what the change costs the backups.
/// </summary>
/// <remarks>
/// <para>
/// Every rule is <see cref="Vault.ChangeAccess(VaultAccessChange, ReadOnlySpan{char}, ReadOnlySpan{char})"/>'s
/// and every byte moves in the session. This asks, confirms and reports.
/// </para>
/// <para>
/// <b>This object owns three master-password buffers</b>, for <see cref="UnlockViewModel"/>'s
/// reason, and zeroes all three on every outcome, on cancel and on disposal. It needs no expiry of
/// its own: the form lives in the unlocked shell, so idleness locks the session and the lock
/// disposes this.
/// </para>
/// </remarks>
internal sealed class VaultAccessViewModel : ObservableObject, IDisposable
{
    private readonly AppVaultSession _session;
    private readonly string? _home;
    private readonly IVaultFilePicker? _picker;

    private SecretBuffer _current = new();
    private SecretBuffer _new = new();
    private SecretBuffer _confirm = new();
    private bool _setPassword;
    private KeyfileChoice _keyfile;
    private string? _newKeyfilePath;
    private bool _confirming;
    private bool _busy;
    private string _message = string.Empty;
    private bool _disposed;

    internal VaultAccessViewModel(AppVaultSession session, string? home, IVaultFilePicker? picker)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        _home = home;
        _picker = picker;

        ReviewCommand = new RelayCommand(Review, () => CanReview);
        ConfirmCommand = new AsyncRelayCommand(ChangeAsync, () => _confirming && !_busy);
        CancelCommand = new RelayCommand(() => Forget(string.Empty), () => _confirming && !_busy);
        ChooseKeyfileCommand = new AsyncRelayCommand(ChooseKeyfileAsync, () => IsEditing && _picker is not null);
    }

    /// <summary>Shows the confirmation for the change the form describes.</summary>
    internal RelayCommand ReviewCommand { get; }

    /// <summary>Makes the change.</summary>
    internal AsyncRelayCommand ConfirmCommand { get; }

    /// <summary>Leaves the confirmation, forgetting every password typed.</summary>
    internal RelayCommand CancelCommand { get; }

    /// <summary>Asks for the keyfile to attach.</summary>
    internal AsyncRelayCommand ChooseKeyfileCommand { get; }

    /// <summary>What unlocks the vault now, read from the vault each time.</summary>
    internal string Factors => Now() is { } now ? $"This vault {Opens(now)}" : string.Empty;

    /// <summary>Whether the open vault has a keyfile, so that removing one is offered.</summary>
    internal bool HasKeyfile => Now()?.Keyfile is not null;

    internal string AttachLabel => HasKeyfile ? "Use a different keyfile" : "Add a keyfile";

    internal int CurrentMaskedLength => _current.Length;

    internal int NewMaskedLength => _new.Length;

    internal int ConfirmMaskedLength => _confirm.Length;

    /// <summary>Whether the form, rather than the confirmation, is showing.</summary>
    internal bool IsEditing => !_confirming && !_busy;

    internal bool IsConfirming => _confirming;

    internal bool Busy
    {
        get => _busy;
        private set
        {
            if (Set(ref _busy, value))
            {
                RaiseState();
            }
        }
    }

    /// <summary>Whether the change sets a new master password.</summary>
    internal bool SetPassword
    {
        get => _setPassword;
        set
        {
            if (IsEditing && Set(ref _setPassword, value))
            {
                if (!value)
                {
                    ResetNew();
                }

                RaiseState();
            }
        }
    }

    internal bool KeepKeyfile
    {
        get => _keyfile == KeyfileChoice.Keep;
        set => Choose(value, KeyfileChoice.Keep);
    }

    internal bool AttachKeyfile
    {
        get => _keyfile == KeyfileChoice.Attach;
        set => Choose(value, KeyfileChoice.Attach);
    }

    internal bool RemoveKeyfile
    {
        get => _keyfile == KeyfileChoice.Remove;
        set => Choose(value, KeyfileChoice.Remove);
    }

    /// <summary>The keyfile to attach, once one has been chosen.</summary>
    internal string? NewKeyfilePath => _newKeyfilePath;

    internal string NewKeyfileName => _newKeyfilePath is null ? string.Empty : Path.GetFileName(_newKeyfilePath);

    /// <summary>What the confirmation says the change will do.</summary>
    internal string Summary
    {
        get
        {
            if (Now() is not { } now)
            {
                return string.Empty;
            }

            var keyfileAfter = _keyfile switch
            {
                KeyfileChoice.Attach => _newKeyfilePath,
                KeyfileChoice.Remove => null,
                _ => now.Keyfile,
            };

            return $"Afterwards the vault {Opens((_setPassword || now.Password, keyfileAfter))}";
        }
    }

    /// <summary>What the change costs the copies already taken (V.1a2).</summary>
    internal string BackupCost => _session.VaultPath is { } path
        ? $"The vault as it is now is kept in {VaultBackups.DirectoryFor(path)} before it changes. That copy, and every " +
          "copy already there, still opens with the current password and keyfile, not the new ones. If those were " +
          "exposed, delete the copies."
        : string.Empty;

    /// <summary>Shown when a keyfile is being attached.</summary>
    internal string KeyfileWarning => _keyfile == KeyfileChoice.Attach && _newKeyfilePath is not null
        ? $"Keep a copy of {NewKeyfileName} somewhere other than beside the vault. Losing it locks the vault, and nothing recovers it."
        : string.Empty;

    internal bool HasKeyfileWarning => KeyfileWarning.Length > 0;

    internal string Message
    {
        get => _message;
        private set
        {
            if (Set(ref _message, value))
            {
                Raise(nameof(HasMessage));
            }
        }
    }

    internal bool HasMessage => _message.Length > 0;

    private bool CanReview =>
        IsEditing
        && (_setPassword || _keyfile != KeyfileChoice.Keep)
        && (_keyfile != KeyfileChoice.Attach || _newKeyfilePath is not null)
        && (!_setPassword || _new.Length > 0)
        && (_current.Length > 0 || Now() is { Password: false });

    internal void TypeCurrent(char c) => Edit(() => _current.Append(c));

    internal void BackspaceCurrent() => Edit(_current.Backspace);

    internal void ClearCurrent() => Edit(_current.Clear);

    internal void TypeNew(char c) => Edit(() => _new.Append(c));

    internal void BackspaceNew() => Edit(_new.Backspace);

    internal void ClearNew() => Edit(_new.Clear);

    internal void TypeConfirm(char c) => Edit(() => _confirm.Append(c));

    internal void BackspaceConfirm() => Edit(_confirm.Backspace);

    internal void ClearConfirm() => Edit(_confirm.Clear);

    /// <summary>
    /// Makes the change. <c>internal</c> for <see cref="UnlockViewModel.UnlockAsync"/>'s reason.
    /// </summary>
    internal async Task ChangeAsync()
    {
        if (_disposed || !_confirming || _busy)
        {
            return;
        }

        Busy = true;

        var change = new VaultAccessChange(
            _setPassword,
            _keyfile switch
            {
                KeyfileChoice.Attach => AccessKeyfileChange.Attach,
                KeyfileChoice.Remove => AccessKeyfileChange.Remove,
                _ => AccessKeyfileChange.Keep,
            },
            _keyfile == KeyfileChoice.Attach ? _newKeyfilePath : null);

        string message;
        try
        {
            // Argon2 several times over: the check of the current password, the core's checks of
            // the new bytes, and the reopen. Off the UI thread, as unlocking is.
            var result = await Task.Run(() => _session.ChangeAccess(_current.Value, change, _new.Value, _confirm.Value))
                .ConfigureAwait(true);

            message = Explain(result);

            if (result.Outcome == AccessChangeOutcome.Changed)
            {
                RememberKeyfile();
            }
        }
        catch (VaultChangedOnDiskException)
        {
            message = "The vault file changed since it was unlocked, so nothing was changed. Lock and unlock to see what changed it, then try again.";
        }
        catch (VaultException e)
        {
            message = e.Message;
        }
        catch (ObjectDisposedException)
        {
            message = "The vault locked before the change was made. Nothing was changed.";
        }
        finally
        {
            Busy = false;
        }

        Forget(message);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _current.Dispose();
        _new.Dispose();
        _confirm.Dispose();
    }

    private static string Opens((bool Password, string? Keyfile) factors) => factors switch
    {
        (true, { } keyfile) => $"opens with its master password and the keyfile {Path.GetFileName(keyfile)}.",
        (true, null) => "opens with its master password.",
        (false, { } keyfile) => $"opens with the keyfile {Path.GetFileName(keyfile)} alone, and has no master password.",
        _ => "has no factor keypaste can name.",
    };

    /// <summary>The open vault's factors, or null once it has locked.</summary>
    private (bool Password, string? Keyfile)? Now()
    {
        try
        {
            return _session.Unlocked is { } vault ? (vault.HasPassword, vault.KeyfilePath) : null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    private string Explain(AccessChangeResult result) => result.Outcome switch
    {
        AccessChangeOutcome.Changed =>
            $"Changed. {Factors} The previous version is kept at {result.Result?.Kept?.Path}, and opens with the old password and keyfile.",
        AccessChangeOutcome.WrongCurrentSecret => "That isn't the vault's current password. Nothing was changed.",
        AccessChangeOutcome.Locked => "The vault locked before the change was made. Nothing was changed.",
        AccessChangeOutcome.ChangedAndLocked => string.Empty,
        _ => result.Result?.Outcome switch
        {
            VaultAccessOutcome.NothingToChange => "Nothing would change.",
            VaultAccessOutcome.EmptyPassword => "A new master password can't be empty. Nothing was changed.",
            VaultAccessOutcome.PasswordsDoNotMatch => "Those two new passwords aren't the same. Nothing was changed.",
            VaultAccessOutcome.WouldLeaveNoPassword =>
                "This vault's keyfile is all that opens it, so it can go only if a master password is set in the same change. Nothing was changed.",
            VaultAccessOutcome.NoKeyfileToRemove => "This vault has no keyfile to remove. Nothing was changed.",
            VaultAccessOutcome.KeyfileUnusable => $"{UnlockViewModel.ExplainKeyfile(result.Result.Keyfile.Outcome)} Nothing was changed.",
            VaultAccessOutcome.KeyfileIsFragile =>
                "keypaste won't attach that file: it would be keyed by its exact bytes, so one edit to it loses the vault. Choose a KeePass keyfile. Nothing was changed.",
            VaultAccessOutcome.KeyfileIsThisVault => "A vault can't be its own keyfile, or a copy of itself. Nothing was changed.",
            _ => "Nothing was changed.",
        },
    };

    /// <summary>Points the recent list at the keyfile the vault now needs, so the next unlock offers it.</summary>
    private void RememberKeyfile()
    {
        if (_session.Unlocked is not { } vault)
        {
            return;
        }

        var recent = KeypasteHome.RecentPath(_home);
        RecentVaults.Save(recent, RecentVaults.Remember(RecentVaults.Load(recent), vault.Path, DateTimeOffset.UtcNow, vault.KeyfilePath));
    }

    private async Task ChooseKeyfileAsync()
    {
        if (_disposed || _picker is null || await _picker.PickKeyfileAsync().ConfigureAwait(true) is not { } picked)
        {
            return;
        }

        var full = Path.GetFullPath(picked);
        var inspection = VaultKeyfile.Inspect(full);

        if (!inspection.Accepted)
        {
            Message = UnlockViewModel.ExplainKeyfile(inspection.Outcome);
            return;
        }

        if (inspection.IsFragile)
        {
            Message = "keypaste won't attach that file: it would be keyed by its exact bytes, so one edit to it loses the vault. Choose a KeePass keyfile.";
            return;
        }

        _newKeyfilePath = full;
        _keyfile = KeyfileChoice.Attach;
        Message = string.Empty;
        RaiseState();
    }

    private void Choose(bool selected, KeyfileChoice choice)
    {
        if (!selected || !IsEditing || _keyfile == choice)
        {
            return;
        }

        _keyfile = choice;
        RaiseState();
    }

    private void Review()
    {
        if (_disposed || !CanReview)
        {
            return;
        }

        _confirming = true;
        Message = string.Empty;
        RaiseState();
    }

    private void Edit(Action edit)
    {
        if (_disposed || !IsEditing)
        {
            return;
        }

        edit();
        RaiseState();

        if (HasMessage)
        {
            Message = string.Empty;
        }
    }

    /// <summary>Zeroes all three passwords and returns to the form, on every way out of a change.</summary>
    private void Forget(string message)
    {
        if (_disposed)
        {
            return;
        }

        _current.Dispose();
        _current = new SecretBuffer();
        ResetNew();
        _confirming = false;
        _setPassword = false;
        _keyfile = KeyfileChoice.Keep;
        _newKeyfilePath = null;
        Message = message;
        RaiseState();
    }

    private void ResetNew()
    {
        _new.Dispose();
        _confirm.Dispose();
        _new = new SecretBuffer();
        _confirm = new SecretBuffer();
    }

    private void RaiseState()
    {
        Raise(nameof(CurrentMaskedLength));
        Raise(nameof(NewMaskedLength));
        Raise(nameof(ConfirmMaskedLength));
        Raise(nameof(IsEditing));
        Raise(nameof(IsConfirming));
        Raise(nameof(SetPassword));
        Raise(nameof(KeepKeyfile));
        Raise(nameof(AttachKeyfile));
        Raise(nameof(RemoveKeyfile));
        Raise(nameof(NewKeyfilePath));
        Raise(nameof(NewKeyfileName));
        Raise(nameof(Factors));
        Raise(nameof(HasKeyfile));
        Raise(nameof(AttachLabel));
        Raise(nameof(Summary));
        Raise(nameof(BackupCost));
        Raise(nameof(KeyfileWarning));
        Raise(nameof(HasKeyfileWarning));
        ReviewCommand.RaiseCanExecuteChanged();
        ConfirmCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        ChooseKeyfileCommand.RaiseCanExecuteChanged();
    }
}
