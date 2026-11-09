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
internal sealed partial class VaultAccessViewModel : ObservableObject, IDisposable
{
    private readonly AppVaultSession _session;
    private readonly string? _home;
    private readonly IVaultFilePicker? _picker;
    private readonly Func<SecretBuffer, Task<PasteOutcome>>? _paste;

    private SecretBuffer _current = new();
    private SecretBuffer _new = new();
    private SecretBuffer _confirm = new();
    private bool _setPassword;
    private bool _createdKeyfile;
    private bool _disposed;

    internal VaultAccessViewModel(AppVaultSession session, string? home, IVaultFilePicker? picker, Func<SecretBuffer, Task<PasteOutcome>>? paste = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        _home = home;
        _picker = picker;
        _paste = paste;

        ConfirmCommand = new AsyncRelayCommand(ChangeAsync, () => IsConfirming && !Busy);
        CancelCommand = new RelayCommand(() => Forget(string.Empty), () => IsConfirming && !Busy);
        ChooseKeyfileCommand = new AsyncRelayCommand(ChooseKeyfileAsync, () => IsEditing && _picker is not null);
        CreateKeyfileCommand = new AsyncRelayCommand(CreateKeyfileAsync, () => IsEditing && _picker is not null);
    }

    /// <summary>Makes the change.</summary>
    internal AsyncRelayCommand ConfirmCommand { get; }

    /// <summary>Leaves the confirmation, forgetting every password typed.</summary>
    internal RelayCommand CancelCommand { get; }

    /// <summary>Asks for the keyfile to attach.</summary>
    internal AsyncRelayCommand ChooseKeyfileCommand { get; }

    /// <summary>Makes a new keyfile where the person chooses, to attach.</summary>
    internal AsyncRelayCommand CreateKeyfileCommand { get; }

    /// <summary>What unlocks the vault now, read from the vault each time.</summary>
    internal string Factors => Now is { } now ? $"This vault {Opens(now)}" : string.Empty;

    /// <summary>Whether the open vault has a keyfile, so that removing one is offered.</summary>
    internal bool HasKeyfile => Now?.Keyfile is not null;

    internal string AttachLabel => HasKeyfile ? "Use a different key file" : "Add a key file";

    /// <summary>What leaving would lose: a password typed or a keyfile chosen, or null.</summary>
    internal string? UnsavedDraft =>
        _current.Length > 0 || _new.Length > 0 || _confirm.Length > 0 || NewKeyfilePath is not null
            ? "this change to the vault's access"
            : null;

    internal int CurrentMaskedLength => _current.Length;

    internal int NewMaskedLength => _new.Length;

    internal int ConfirmMaskedLength => _confirm.Length;

    /// <summary>Whether the form, rather than the confirmation, is showing.</summary>
    internal bool IsEditing => !IsConfirming && !Busy;

    /// <summary>Whether the form that changes what unlocks the vault is open.</summary>
    [ObservableProperty]
    internal partial bool IsFormOpen { get; private set; }

    [RelayCommand]
    private void OpenForm() => IsFormOpen = true;

    [RelayCommand]
    private void CloseForm()
    {
        if (IsEditing)
        {
            Forget(string.Empty);
            IsFormOpen = false;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand), nameof(CancelCommand), nameof(ReviewCommand), nameof(ChooseKeyfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateKeyfileCommand))]
    internal partial bool IsConfirming { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand), nameof(CancelCommand), nameof(ReviewCommand), nameof(ChooseKeyfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateKeyfileCommand))]
    internal partial bool Busy { get; private set; }

    /// <summary>Whether the change sets a new master password.</summary>
    internal bool SetPassword
    {
        get => _setPassword;
        set
        {
            if (IsEditing && SetProperty(ref _setPassword, value))
            {
                OnPropertyChanged(nameof(Summary));
                ReviewCommand.NotifyCanExecuteChanged();

                if (!value)
                {
                    ResetNew();
                }
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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewKeyfileName), nameof(Summary), nameof(KeyfileWarning), nameof(HasKeyfileWarning))]
    [NotifyCanExecuteChangedFor(nameof(ReviewCommand))]
    internal partial string? NewKeyfilePath { get; private set; }

    internal string NewKeyfileName => NewKeyfilePath is null ? string.Empty : Path.GetFileName(NewKeyfilePath);

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
                KeyfileChoice.Attach => NewKeyfilePath,
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
          "copy already there, still opens with the current password, key file and YubiKey, not the new ones. If those " +
          "may have leaked, delete the copies."
        : string.Empty;

    /// <summary>Shown when a keyfile is being attached.</summary>
    internal string KeyfileWarning => Keyfile == KeyfileChoice.Attach && NewKeyfilePath is { } keyfile
        ? _createdKeyfile && _session.VaultPath is { } vault
            ? UnlockViewModel.CreatedKeyfileNote(keyfile, vault)
            : $"Keep a copy of {NewKeyfileName} somewhere other than beside the vault. Losing it locks the vault, and nothing recovers it."
        : string.Empty;

    internal bool HasKeyfileWarning => KeyfileWarning.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    internal partial string Message { get; private set; } = string.Empty;

    internal bool HasMessage => Message.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeepKeyfile), nameof(AttachKeyfile), nameof(RemoveKeyfile), nameof(Summary), nameof(KeyfileWarning))]
    [NotifyPropertyChangedFor(nameof(HasKeyfileWarning))]
    [NotifyCanExecuteChangedFor(nameof(ReviewCommand))]
    private partial KeyfileChoice Keyfile { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeepHardwareKey), nameof(AttachHardwareKey), nameof(RemoveHardwareKey), nameof(HardwareKeyWarning))]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(HasHardwareKeyWarning))]
    [NotifyCanExecuteChangedFor(nameof(ReviewCommand))]
    private partial KeyfileChoice HardwareKey { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewSlotOne), nameof(NewSlotTwo), nameof(Summary))]
    private partial int NewSlot { get; set; } = 2;

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
        && (Keyfile != KeyfileChoice.Attach || NewKeyfilePath is not null)
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

    /// <summary>Appends what is on the clipboard to the current master password.</summary>
    internal Task PasteCurrentAsync() => PasteIntoAsync(pasted => _current.Append(pasted.Value));

    /// <summary>Appends what is on the clipboard to the new master password.</summary>
    internal Task PasteNewAsync() => PasteIntoAsync(pasted => _new.Append(pasted.Value));

    /// <summary>Appends what is on the clipboard to the confirmation.</summary>
    internal Task PasteConfirmAsync() => PasteIntoAsync(pasted => _confirm.Append(pasted.Value));

    /// <summary>Reads the clipboard into a buffer of the paste's own, as the unlock screen does, and appends it only while the form is still being edited.</summary>
    private async Task PasteIntoAsync(Action<SecretBuffer> append)
    {
        if (_disposed || !IsEditing || _paste is null)
        {
            return;
        }

        using var pasted = new SecretBuffer();
        var outcome = await _paste(pasted).ConfigureAwait(true);

        if (outcome == PasteOutcome.Pasted)
        {
            Edit(() => append(pasted));
        }
        else if (!_disposed)
        {
            Message = SecretField.NoteFor(outcome);
        }
    }

    /// <summary>
    /// Makes the change. <c>internal</c> for <see cref="UnlockViewModel.UnlockAsync"/>'s reason.
    /// </summary>
    internal async Task ChangeAsync()
    {
        if (_disposed || !IsConfirming || Busy)
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
                Keyfile switch
                {
                    KeyfileChoice.Attach => AccessKeyfileChange.Attach,
                    KeyfileChoice.Remove => AccessKeyfileChange.Remove,
                    _ => AccessKeyfileChange.Keep,
                },
                Keyfile == KeyfileChoice.Attach ? NewKeyfilePath : null)
            {
                HardwareKeyChange = HardwareKey switch
                {
                    KeyfileChoice.Attach => AccessHardwareKeyChange.Attach,
                    KeyfileChoice.Remove => AccessHardwareKeyChange.Remove,
                    _ => AccessHardwareKeyChange.Keep,
                },
                HardwareKey = HardwareKey == KeyfileChoice.Attach ? _session.NewHardwareKey(NewSlot) : null,
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
            message = "The vault locked before the change was made. Nothing was changed. Unlock it, then try again.";
        }
        finally
        {
            Busy = false;
        }

        Forget(message);
        IsFormOpen = false;
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
        var withKey = factors.Slot is { } used ? Invariant($" and the YubiKey in slot {used}") : string.Empty;

        return factors switch
        {
            (true, { } keyfile, null) => $"opens with its master password and the key file {Path.GetFileName(keyfile)}.",
            (true, { } keyfile, _) => $"opens with its master password, the key file {Path.GetFileName(keyfile)}{withKey}.",
            (true, null, _) => $"opens with its master password{withKey}.",
            (false, { } keyfile, _) => $"opens with the key file {Path.GetFileName(keyfile)}{withKey}, and has no master password.",
            (false, null, { } slot) => Invariant($"opens with the YubiKey in slot {slot} alone, and has no master password."),
            _ => "has no factor keypaste can name.",
        };
    }

    private string Explain(AccessChangeResult result) => result.Outcome switch
    {
        AccessChangeOutcome.Changed =>
            $"Changed. {Factors} The previous version is kept at {result.Result?.Kept?.Path}, and opens with the old password, key file and YubiKey.",
        AccessChangeOutcome.WrongCurrentSecret => "That isn't the vault's current password. Nothing was changed. Type it again.",
        AccessChangeOutcome.Locked => "The vault locked before the change was made. Nothing was changed. Unlock it, then try again.",
        AccessChangeOutcome.ChangedAndLocked => string.Empty,
        _ => result.Result?.Outcome switch
        {
            VaultAccessOutcome.NothingToChange => "Nothing would change.",
            VaultAccessOutcome.EmptyPassword => "A new master password can't be empty. Nothing was changed. Type one.",
            VaultAccessOutcome.PasswordsDoNotMatch => "Those two new passwords aren't the same. Nothing was changed. Type them again.",
            VaultAccessOutcome.WouldLeaveNoPassword when Keyfile == KeyfileChoice.Remove =>
                "This vault's key file is all that opens it, so it can go only if a master password is set in the same change. Nothing was changed.",
            VaultAccessOutcome.WouldLeaveNoPassword =>
                "This vault has no master password or key file, so its YubiKey can go only if a master password is set in the same change. Nothing was changed.",
            VaultAccessOutcome.NoKeyfileToRemove => "This vault has no key file to remove. Nothing was changed.",
            VaultAccessOutcome.NoHardwareKeyToRemove => "This vault has no YubiKey to remove. Nothing was changed.",
            VaultAccessOutcome.HardwareKeyNotFound =>
                Invariant($"No YubiKey with slot {NewSlot} programmed is plugged in. Plug it in, or program the slot for HMAC-SHA1 challenge-response in YubiKey Manager. Nothing was changed."),
            VaultAccessOutcome.KeyfileUnusable => $"{UnlockViewModel.ExplainKeyfile(result.Result.Keyfile.Outcome)} Nothing was changed.",
            VaultAccessOutcome.KeyfileIsFragile =>
                "keypaste won't attach that file: it would be keyed by its exact bytes, so one edit to it loses the vault. Choose a KeePass key file. Nothing was changed.",
            VaultAccessOutcome.KeyfileIsThisVault => "A vault can't be its own key file, or a copy of itself. Nothing was changed. Choose another file.",
            _ => "Nothing was changed. Try again.",
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
            Message = "keypaste won't attach that file: it would be keyed by its exact bytes, so one edit to it loses the vault. Choose a KeePass key file.";
            return;
        }

        _createdKeyfile = false;
        NewKeyfilePath = full;
        Keyfile = KeyfileChoice.Attach;
        Message = string.Empty;
    }

    /// <summary>Makes a new keyfile where the person chooses, written at once and only where nothing is, and chooses it to attach.</summary>
    private async Task CreateKeyfileAsync()
    {
        if (_disposed || _picker is null || _session.VaultPath is not { } vault)
        {
            return;
        }

        if (await _picker.PickNewKeyfileAsync(Path.GetFileNameWithoutExtension(vault) + ".keyx").ConfigureAwait(true) is not { } picked
            || _disposed || !IsEditing)
        {
            return;
        }

        var full = Path.GetFullPath(picked);
        var outcome = VaultKeyfile.Create(full, out _);

        if (outcome != KeyfileCreationOutcome.Created)
        {
            Message = UnlockViewModel.ExplainKeyfileCreation(outcome);
            return;
        }

        _createdKeyfile = true;
        NewKeyfilePath = full;
        Keyfile = KeyfileChoice.Attach;
        Message = $"keypaste saved {Path.GetFileName(full)}. The vault needs it only once you change access.";
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

    /// <summary>Shows the confirmation for the change the form describes.</summary>
    [RelayCommand(CanExecute = nameof(CanReview))]
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
        OnPropertyChanged(nameof(CurrentMaskedLength));
        OnPropertyChanged(nameof(NewMaskedLength));
        OnPropertyChanged(nameof(ConfirmMaskedLength));
        ReviewCommand.NotifyCanExecuteChanged();

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
        OnPropertyChanged(nameof(CurrentMaskedLength));
        ReviewCommand.NotifyCanExecuteChanged();
        ResetNew();
        IsConfirming = false;
        if (SetProperty(ref _setPassword, false, nameof(SetPassword)))
        {
            OnPropertyChanged(nameof(Summary));
            ReviewCommand.NotifyCanExecuteChanged();
        }

        Keyfile = KeyfileChoice.Keep;
        HardwareKey = KeyfileChoice.Keep;
        NewSlot = 2;
        _createdKeyfile = false;
        NewKeyfilePath = null;
        Message = message;
        OnPropertyChanged(nameof(Now));
        OnPropertyChanged(nameof(Factors));
        OnPropertyChanged(nameof(HasKeyfile));
        OnPropertyChanged(nameof(HasHardwareKey));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(AttachLabel));
        OnPropertyChanged(nameof(AttachHardwareKeyLabel));
        ReviewCommand.NotifyCanExecuteChanged();
    }

    private void ResetNew()
    {
        _new.Dispose();
        _confirm.Dispose();
        _new = new SecretBuffer();
        _confirm = new SecretBuffer();
        OnPropertyChanged(nameof(NewMaskedLength));
        OnPropertyChanged(nameof(ConfirmMaskedLength));
        ReviewCommand.NotifyCanExecuteChanged();
    }
}
