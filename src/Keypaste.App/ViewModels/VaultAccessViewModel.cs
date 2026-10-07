using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Recent;

namespace Keypaste.App.ViewModels;

/// <summary>What an access change does to the keyfile or the hardware key, as the Settings form offers it.</summary>
internal enum KeyfileChoice
{
    Keep = 0,
    Attach = 1,
    Remove = 2,
}

/// <summary>
/// Changing what unlocks the open vault: its master password, its keyfile, its YubiKey, or several,
/// behind the current password and a confirmation that says what the change costs the backups.
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
    private KeyfileChoice _hardwareKey;
    private int _newSlot = 2;
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

        DependsOn(nameof(Factors), nameof(Now));
        DependsOn(nameof(HasKeyfile), nameof(Now));
        DependsOn(nameof(AttachLabel), nameof(HasKeyfile));
        DependsOn(nameof(IsEditing), nameof(IsConfirming), nameof(Busy));
        DependsOn(nameof(KeepKeyfile), nameof(Keyfile));
        DependsOn(nameof(AttachKeyfile), nameof(Keyfile));
        DependsOn(nameof(RemoveKeyfile), nameof(Keyfile));
        DependsOn(nameof(HasHardwareKey), nameof(Now));
        DependsOn(nameof(AttachHardwareKeyLabel), nameof(HasHardwareKey));
        DependsOn(nameof(KeepHardwareKey), nameof(HardwareKey));
        DependsOn(nameof(AttachHardwareKey), nameof(HardwareKey));
        DependsOn(nameof(RemoveHardwareKey), nameof(HardwareKey));
        DependsOn(nameof(NewSlotOne), nameof(NewSlot));
        DependsOn(nameof(NewSlotTwo), nameof(NewSlot));
        DependsOn(nameof(HardwareKeyWarning), nameof(HardwareKey));
        DependsOn(nameof(HasHardwareKeyWarning), nameof(HardwareKeyWarning));
        DependsOn(nameof(NewKeyfileName), nameof(NewKeyfilePath));
        DependsOn(nameof(Summary), nameof(Now), nameof(Keyfile), nameof(NewKeyfilePath), nameof(HardwareKey), nameof(NewSlot), nameof(SetPassword));
        DependsOn(nameof(KeyfileWarning), nameof(Keyfile), nameof(NewKeyfilePath), nameof(NewKeyfileName));
        DependsOn(nameof(HasKeyfileWarning), nameof(KeyfileWarning));
        DependsOn(nameof(HasMessage), nameof(Message));
        DependsOn(ReviewCommand, nameof(IsEditing), nameof(SetPassword), nameof(Keyfile), nameof(HardwareKey), nameof(NewKeyfilePath), nameof(CurrentMaskedLength), nameof(NewMaskedLength), nameof(Now));
        DependsOn(ConfirmCommand, nameof(IsConfirming), nameof(Busy));
        DependsOn(CancelCommand, nameof(IsConfirming), nameof(Busy));
        DependsOn(ChooseKeyfileCommand, nameof(IsEditing));
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
    internal string Factors => Now is { } now ? $"This vault {Opens(now)}" : string.Empty;

    /// <summary>Whether the open vault has a keyfile, so that removing one is offered.</summary>
    internal bool HasKeyfile => Now?.Keyfile is not null;

    internal string AttachLabel => HasKeyfile ? "Use a different keyfile" : "Add a keyfile";

    internal int CurrentMaskedLength => _current.Length;

    internal int NewMaskedLength => _new.Length;

    internal int ConfirmMaskedLength => _confirm.Length;

    /// <summary>Whether the form, rather than the confirmation, is showing.</summary>
    internal bool IsEditing => !_confirming && !_busy;

    internal bool IsConfirming
    {
        get => _confirming;
        private set => Set(ref _confirming, value);
    }

    internal bool Busy
    {
        get => _busy;
        private set => Set(ref _busy, value);
    }

    /// <summary>Whether the change sets a new master password.</summary>
    internal bool SetPassword
    {
        get => _setPassword;
        set
        {
            if (IsEditing && Set(ref _setPassword, value) && !value)
            {
                ResetNew();
            }
        }
    }

    internal bool KeepKeyfile
    {
        get => Keyfile == KeyfileChoice.Keep;
        set => Choose(value, KeyfileChoice.Keep);
    }

    internal bool AttachKeyfile
    {
        get => Keyfile == KeyfileChoice.Attach;
        set => Choose(value, KeyfileChoice.Attach);
    }

    internal bool RemoveKeyfile
    {
        get => Keyfile == KeyfileChoice.Remove;
        set => Choose(value, KeyfileChoice.Remove);
    }

    /// <summary>Whether this build reaches hardware keys, so a YubiKey can be added.</summary>
    internal bool OffersHardwareKey => _session.HardwareKeys is not null;

    /// <summary>Whether the open vault needs a YubiKey, so that removing one is offered.</summary>
    internal bool HasHardwareKey => Now?.Slot is not null;

    internal string AttachHardwareKeyLabel => HasHardwareKey ? "Use a different YubiKey slot" : "Add a YubiKey";

    internal bool KeepHardwareKey
    {
        get => HardwareKey == KeyfileChoice.Keep;
        set => ChooseHardwareKey(value, KeyfileChoice.Keep);
    }

    internal bool AttachHardwareKey
    {
        get => HardwareKey == KeyfileChoice.Attach;
        set => ChooseHardwareKey(value, KeyfileChoice.Attach);
    }

    internal bool RemoveHardwareKey
    {
        get => HardwareKey == KeyfileChoice.Remove;
        set => ChooseHardwareKey(value, KeyfileChoice.Remove);
    }

    /// <summary>Whether the YubiKey to add answers from slot 1.</summary>
    internal bool NewSlotOne
    {
        get => NewSlot == 1;
        set => ChooseSlot(value, 1);
    }

    /// <summary>Whether the YubiKey to add answers from slot 2.</summary>
    internal bool NewSlotTwo
    {
        get => NewSlot == 2;
        set => ChooseSlot(value, 2);
    }

    /// <summary>
    /// What adding a YubiKey costs, said before the change: a lost key locks the vault, only a spare
    /// programmed with the same secret opens it too, and every save asks the key.
    /// </summary>
    internal string HardwareKeyWarning => HardwareKey switch
    {
        KeyfileChoice.Attach =>
            "If this YubiKey is lost or broken, the vault cannot be opened: not by keypaste, not by KeePassXC, and not from any " +
            "backup made after this change. The only way back in is a second YubiKey programmed with the same secret in the same slot, " +
            "which you make with YubiKey Manager before you need it; keypaste cannot copy a secret out of a key. " +
            "Every save asks the key again, so a slot that needs a touch is touched on every save.",
        KeyfileChoice.Remove => "Afterwards the vault opens without the YubiKey. Backups already kept still need it.",
        _ => string.Empty,
    };

    internal bool HasHardwareKeyWarning => HardwareKeyWarning.Length > 0;

    /// <summary>The keyfile to attach, once one has been chosen.</summary>
    internal string? NewKeyfilePath
    {
        get => _newKeyfilePath;
        private set => Set(ref _newKeyfilePath, value);
    }

    internal string NewKeyfileName => _newKeyfilePath is null ? string.Empty : Path.GetFileName(_newKeyfilePath);

    /// <summary>What the confirmation says the change will do.</summary>
    internal string Summary
    {
        get
        {
            if (Now is not { } now)
            {
                return string.Empty;
            }

            var keyfileAfter = Keyfile switch
            {
                KeyfileChoice.Attach => _newKeyfilePath,
                KeyfileChoice.Remove => null,
                _ => now.Keyfile,
            };

            int? slotAfter = HardwareKey switch
            {
                KeyfileChoice.Attach => NewSlot,
                KeyfileChoice.Remove => null,
                _ => now.Slot,
            };

            return $"Afterwards the vault {Opens((_setPassword || now.Password, keyfileAfter, slotAfter))}";
        }
    }

    /// <summary>What the change costs the copies already taken (V.1a2).</summary>
    internal string BackupCost => _session.VaultPath is { } path
        ? $"The vault as it is now is kept in {VaultBackups.DirectoryFor(path)} before it changes. That copy, and every " +
          "copy already there, still opens with the current password, keyfile and YubiKey, not the new ones. If those were " +
          "exposed, delete the copies."
        : string.Empty;

    /// <summary>Shown when a keyfile is being attached.</summary>
    internal string KeyfileWarning => Keyfile == KeyfileChoice.Attach && _newKeyfilePath is not null
        ? $"Keep a copy of {NewKeyfileName} somewhere other than beside the vault. Losing it locks the vault, and nothing recovers it."
        : string.Empty;

    internal bool HasKeyfileWarning => KeyfileWarning.Length > 0;

    internal string Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    internal bool HasMessage => _message.Length > 0;

    private KeyfileChoice Keyfile
    {
        get => _keyfile;
        set => Set(ref _keyfile, value);
    }

    private KeyfileChoice HardwareKey
    {
        get => _hardwareKey;
        set => Set(ref _hardwareKey, value);
    }

    private int NewSlot
    {
        get => _newSlot;
        set => Set(ref _newSlot, value);
    }

    /// <summary>The open vault's factors, or null once it has locked.</summary>
    private (bool Password, string? Keyfile, int? Slot)? Now
    {
        get
        {
            try
            {
                return _session.Unlocked is { } vault ? (vault.HasPassword, vault.KeyfilePath, vault.HardwareKey?.Slot) : null;
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
        }
    }

    private bool CanReview =>
        IsEditing
        && (_setPassword || Keyfile != KeyfileChoice.Keep || HardwareKey != KeyfileChoice.Keep)
        && (Keyfile != KeyfileChoice.Attach || _newKeyfilePath is not null)
        && (!_setPassword || _new.Length > 0)
        && (_current.Length > 0 || Now is { Password: false });

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

        string message;
        try
        {
            // The session owns the key it attaches, and disposes it on every outcome but a change.
#pragma warning disable CA2000
            var change = new VaultAccessChange(
                _setPassword,
                _keyfile switch
                {
                    KeyfileChoice.Attach => AccessKeyfileChange.Attach,
                    KeyfileChoice.Remove => AccessKeyfileChange.Remove,
                    _ => AccessKeyfileChange.Keep,
                },
                _keyfile == KeyfileChoice.Attach ? _newKeyfilePath : null)
            {
                HardwareKeyChange = _hardwareKey switch
                {
                    KeyfileChoice.Attach => AccessHardwareKeyChange.Attach,
                    KeyfileChoice.Remove => AccessHardwareKeyChange.Remove,
                    _ => AccessHardwareKeyChange.Keep,
                },
                HardwareKey = _hardwareKey == KeyfileChoice.Attach ? _session.NewHardwareKey(_newSlot) : null,
            };
#pragma warning restore CA2000

            // Argon2 several times over: the check of the current password, the core's checks of
            // the new bytes, and the reopen. Off the UI thread, as unlocking is; a YubiKey being
            // added is asked by the save there, and touched once.
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
            message = "The vault file changed since it was unlocked, so nothing was changed. Reload to see what changed it, then try again.";
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

    private static string Opens((bool Password, string? Keyfile, int? Slot) factors)
    {
        var withKey = factors.Slot is { } used ? $" and the YubiKey in slot {used}" : string.Empty;

        return factors switch
        {
            (true, { } keyfile, null) => $"opens with its master password and the keyfile {Path.GetFileName(keyfile)}.",
            (true, { } keyfile, _) => $"opens with its master password, the keyfile {Path.GetFileName(keyfile)}{withKey}.",
            (true, null, _) => $"opens with its master password{withKey}.",
            (false, { } keyfile, _) => $"opens with the keyfile {Path.GetFileName(keyfile)}{withKey}, and has no master password.",
            (false, null, { } slot) => $"opens with the YubiKey in slot {slot} alone, and has no master password.",
            _ => "has no factor keypaste can name.",
        };
    }

    private string Explain(AccessChangeResult result) => result.Outcome switch
    {
        AccessChangeOutcome.Changed =>
            $"Changed. {Factors} The previous version is kept at {result.Result?.Kept?.Path}, and opens with the old password, keyfile and YubiKey.",
        AccessChangeOutcome.WrongCurrentSecret => "That isn't the vault's current password. Nothing was changed.",
        AccessChangeOutcome.Locked => "The vault locked before the change was made. Nothing was changed.",
        AccessChangeOutcome.ChangedAndLocked => string.Empty,
        _ => result.Result?.Outcome switch
        {
            VaultAccessOutcome.NothingToChange => "Nothing would change.",
            VaultAccessOutcome.EmptyPassword => "A new master password can't be empty. Nothing was changed.",
            VaultAccessOutcome.PasswordsDoNotMatch => "Those two new passwords aren't the same. Nothing was changed.",
            VaultAccessOutcome.WouldLeaveNoPassword when _keyfile == KeyfileChoice.Remove =>
                "This vault's keyfile is all that opens it, so it can go only if a master password is set in the same change. Nothing was changed.",
            VaultAccessOutcome.WouldLeaveNoPassword =>
                "This vault has no master password or keyfile, so its YubiKey can go only if a master password is set in the same change. Nothing was changed.",
            VaultAccessOutcome.NoKeyfileToRemove => "This vault has no keyfile to remove. Nothing was changed.",
            VaultAccessOutcome.NoHardwareKeyToRemove => "This vault has no YubiKey to remove. Nothing was changed.",
            VaultAccessOutcome.HardwareKeyNotFound =>
                $"No YubiKey with slot {_newSlot} programmed is plugged in. Plug it in, or program the slot for HMAC-SHA1 challenge-response in YubiKey Manager. Nothing was changed.",
            VaultAccessOutcome.KeyfileUnusable => $"{UnlockViewModel.ExplainKeyfile(result.Result.Keyfile.Outcome)} Nothing was changed.",
            VaultAccessOutcome.KeyfileIsFragile =>
                "keypaste won't attach that file: it would be keyed by its exact bytes, so one edit to it loses the vault. Choose a KeePass keyfile. Nothing was changed.",
            VaultAccessOutcome.KeyfileIsThisVault => "A vault can't be its own keyfile, or a copy of itself. Nothing was changed.",
            _ => "Nothing was changed.",
        },
    };

    /// <summary>Points the recent list at the keyfile and YubiKey slot the vault now needs, so the next unlock offers them.</summary>
    private void RememberKeyfile()
    {
        if (_session.Unlocked is not { } vault)
        {
            return;
        }

        var recent = KeypasteHome.RecentPath(_home);
        RecentVaults.Save(
            recent,
            RecentVaults.Remember(RecentVaults.Load(recent), vault.Path, DateTimeOffset.UtcNow, vault.KeyfilePath, vault.HardwareKey?.Slot));
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

        NewKeyfilePath = full;
        Keyfile = KeyfileChoice.Attach;
        Message = string.Empty;
    }

    private void Choose(bool selected, KeyfileChoice choice)
    {
        if (selected && IsEditing)
        {
            Keyfile = choice;
        }
    }

    private void ChooseHardwareKey(bool selected, KeyfileChoice choice)
    {
        if (selected && IsEditing)
        {
            HardwareKey = choice;
        }
    }

    private void ChooseSlot(bool selected, int slot)
    {
        if (selected && IsEditing)
        {
            NewSlot = slot;
        }
    }

    private void Review()
    {
        if (_disposed || !CanReview)
        {
            return;
        }

        IsConfirming = true;
        Message = string.Empty;
    }

    private void Edit(Action edit)
    {
        if (_disposed || !IsEditing)
        {
            return;
        }

        edit();
        Raise(nameof(CurrentMaskedLength));
        Raise(nameof(NewMaskedLength));
        Raise(nameof(ConfirmMaskedLength));

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
        Raise(nameof(CurrentMaskedLength));
        ResetNew();
        IsConfirming = false;
        Set(ref _setPassword, false, nameof(SetPassword));
        Keyfile = KeyfileChoice.Keep;
        HardwareKey = KeyfileChoice.Keep;
        NewSlot = 2;
        NewKeyfilePath = null;
        Message = message;
        Raise(nameof(Now));
    }

    private void ResetNew()
    {
        _new.Dispose();
        _confirm.Dispose();
        _new = new SecretBuffer();
        _confirm = new SecretBuffer();
        Raise(nameof(NewMaskedLength));
        Raise(nameof(ConfirmMaskedLength));
    }
}
