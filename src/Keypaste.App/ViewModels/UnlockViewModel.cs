using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.HardwareKeys;
using Keypaste.Core.Ownership;
using Keypaste.Core.Recent;
using Keypaste.Core.Settings;

namespace Keypaste.App.ViewModels;

/// <summary>One remembered vault, as the unlock screen shows it.</summary>
/// <remarks>
/// The list shows <see cref="Name"/> and puts <see cref="Path"/> in a tooltip, because a screenshot
/// should not publish somebody's directory layout.
/// </remarks>
internal sealed class RecentVaultItem(string path, bool exists)
{
    internal string Path { get; } = path;

    internal string Name { get; } = DisplayTextSanitizer.Sanitize(System.IO.Path.GetFileName(path)).Text;

    internal string Location { get; } = DisplayTextSanitizer.Escaped(path);

    /// <summary>Whether the file is still where it was.</summary>
    /// <remarks>
    /// A missing vault is shown greyed rather than dropped. Silently removing it hides a moved file
    /// and reads as data loss.
    /// </remarks>
    internal bool Exists { get; } = exists;

    public override string ToString() => Name;
}

/// <summary>A database KeePassXC last opened, as the first run offers it: its name, with the path in a tooltip.</summary>
internal sealed class KeePassXcDatabaseItem(string path)
{
    internal string Path { get; } = path;

    internal string Name { get; } = DisplayTextSanitizer.Sanitize(System.IO.Path.GetFileName(path)).Text;

    internal string Location { get; } = DisplayTextSanitizer.Escaped(path);

    public override string ToString() => Name;
}

/// <summary>
/// The unlock screen: which vault, and the master password.
/// </summary>
/// <remarks>
/// <b>This object owns the <see cref="SecretBuffer"/>.</b> Not the control — Avalonia does not
/// dispose controls, so a buffer living on one would be leaked by design, and a buffer in the visual
/// tree is exactly what <see cref="Controls.MaskedInput"/> exists to avoid. This is
/// <see cref="IDisposable"/> and the shell disposes it on every route out.
/// </remarks>
internal sealed partial class UnlockViewModel : ObservableObject, IDisposable
{
    private readonly AppVaultSession _session;
    private readonly string? _home;
    private readonly Action _unlocked;
    private readonly IVaultFilePicker _picker;
    private readonly Action<Action> _post;
    private readonly Func<IReadOnlyList<string>>? _keePassXcConfigs;
    private readonly Func<SecretBuffer, Task<PasteOutcome>>? _paste;

    private SecretBuffer _master = new();
    private SecretBuffer _new = new();
    private SecretBuffer _confirm = new();
    private IReadOnlyList<RecentVault> _remembered = [];
    private string? _selectedPath;
    private string? _createdKeyfile;
    private string _message = string.Empty;
    private string? _heldMessage;
    private string? _handOffPath;
    private string? _notice;
    private string? _chosenNotice;
    private bool _disposed;

    internal UnlockViewModel(
        AppVaultSession session,
        string? home,
        IVaultFilePicker picker,
        Action unlocked,
        Action<Action>? post = null,
        string? message = null,
        VaultLockReason? lockedBy = null,
        Func<IReadOnlyList<string>>? keePassXcConfigs = null,
        Func<SecretBuffer, Task<PasteOutcome>>? paste = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(picker);
        ArgumentNullException.ThrowIfNull(unlocked);

        _session = session;
        _home = home;
        _picker = picker;
        _unlocked = unlocked;
        _post = post ?? (action => action());
        _keePassXcConfigs = keePassXcConfigs;
        _paste = paste;

        ResumeCommand = new AsyncRelayCommand(ResumeAsync, () => !Busy && OffersQuickUnlock);
        BrowseCommand = new AsyncRelayCommand(BrowseAsync, () => !Busy && IsOpening);
        StartCreateCommand = new AsyncRelayCommand(StartCreateAsync, () => !Busy);
        CancelCreateCommand = new AsyncRelayCommand(CancelCreateAsync, () => !Busy);
        StartRestoreCommand = new RelayCommand(StartRestore, () => !Busy && HasBackups && IsOpening);
        CloseRestoreCommand = new RelayCommand(CloseRestore, () => Restore is { Busy: false });
        ChooseKeyfileCommand = new AsyncRelayCommand(ChooseKeyfileAsync, () => !Busy);
        CreateKeyfileCommand = new AsyncRelayCommand(CreateKeyfileAsync, () => !Busy && IsCreating);
        ClearKeyfileCommand = new RelayCommand(() => KeyfilePath = null, () => !Busy && KeyfilePath is not null);
        ShowMoreOptionsCommand = new RelayCommand(() => MoreOptions = true, () => !MoreOptions);
        UseHardwareKeyCommand = new RelayCommand(() => HardwareKeySlot = 2, () => !Busy && OffersHardwareKey && HardwareKeySlot is null);
        SwitchSlotCommand = new RelayCommand(() => HardwareKeySlot = HardwareKeySlot == 1 ? 2 : 1, () => !Busy && HardwareKeySlot is not null);
        ClearHardwareKeyCommand = new RelayCommand(() => HardwareKeySlot = null, () => !Busy && HardwareKeySlot is not null);
        CancelTouchCommand = new RelayCommand(_session.CancelHardwareKeyWait, () => IsWaitingForTouch);

        _session.WaitingForTouch += OnWaitingForTouch;
        _session.HolderLeft += OnHolderLeft;
        Reload();
        LockNote = lockedBy is { } reason ? DescribeLock(reason, session.Clock.GetLocalNow(), session.IdleTimeout) : string.Empty;

        if (message is not null)
        {
            Message = message;
        }
    }

    /// <summary>Why the vault on this screen was locked, when this screen follows a lock; otherwise empty.</summary>
    internal string LockNote { get; }

    internal bool HasLockNote => LockNote.Length > 0;

    /// <summary>The screen's heading: which vault is locked, or an invitation to create or open one.</summary>
    internal string Heading => _selectedPath is null
        ? OffersCreateFirst ? "Start with a vault" : "Open a vault"
        : IsRestoreOnly ? $"{SelectedName} can't be opened"
        : IsHandOff ? $"Open {SelectedName}"
        : $"{SelectedName} is locked";

    /// <summary>
    /// Whether the selection is a file the import dialog handed over to keep editing in place. keypaste
    /// has not opened it before, so the screen asks for it rather than calling it locked.
    /// </summary>
    internal bool IsHandOff => _handOffPath is not null
        && string.Equals(_handOffPath, _selectedPath, PathIdentity.Comparison);

    /// <summary>The line under the heading while a vault is selected.</summary>
    internal string Subtitle => IsHandOff
        ? "Enter its master password to keep editing it in place."
        : OffersQuickUnlock ? $"Unlock it with {QuickUnlockName} or its master password."
        : "Enter its master password to open it.";

    /// <summary>What the person knows the quick unlock as, such as "Windows Hello", or empty where the app offers none.</summary>
    internal string QuickUnlockName => _session.QuickUnlock?.Name ?? string.Empty;

    /// <summary>What the screen says while Windows Hello asks, after a password unlock, to keep this unlock for a quick one.</summary>
    internal string KeepingNote => $"Confirm with {QuickUnlockName} so it can unlock this vault after a lock.";

    /// <summary>Whether the selected vault has a quick unlock copy, so the screen offers to unlock it without its password.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OffersQuickUnlock), nameof(Subtitle), nameof(QuickUnlockIsPrimary), nameof(UnlockIsPrimary))]
    [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
    internal partial bool Resumable { get; private set; }

    /// <summary>Whether the screen offers its quick unlock, beside the master password, which always stays.</summary>
    internal bool OffersQuickUnlock => Resumable && IsOpening && !IsRestoreOnly && _selectedPath is not null;

    /// <summary>Whether the quick unlock is the screen's amber action, which it is unless a YubiKey waits for a touch.</summary>
    internal bool QuickUnlockIsPrimary => OffersQuickUnlock && !IsWaitingForTouch;

    /// <summary>Whether Unlock is the screen's amber action: not while the quick unlock is offered or a YubiKey waits.</summary>
    internal bool UnlockIsPrimary => !OffersQuickUnlock && !IsWaitingForTouch;

    /// <summary>Opens the selected vault without its password, once the person answers Windows Hello.</summary>
    internal AsyncRelayCommand ResumeCommand { get; }

    /// <summary>Whether Windows Hello is asking, after a password unlock, to keep it for a quick unlock.</summary>
    [ObservableProperty]
    internal partial bool IsKeepingQuickUnlock { get; private set; }

    /// <summary>The line under the heading with no vault selected.</summary>
    internal string WelcomeSubtitle => OffersKeePassXc
        ? "Open a vault KeePassXC opened, another file, or a new vault."
        : "A vault is an encrypted file on this computer that keeps your passwords and keys, and its master password is the one password that opens it.";

    /// <summary>The crash-report choice this screen asks, below every way in, while nobody has answered it; null otherwise.</summary>
    internal CrashChoiceViewModel? CrashChoice { get; init; }

    internal bool HasCrashChoice => CrashChoice is not null;

    /// <summary>The databases KeePassXC last opened, offered while no vault is selected.</summary>
    internal IReadOnlyList<KeePassXcDatabaseItem> KeePassXc { get; private set; } = [];

    /// <summary>Whether the welcome lists KeePassXC's databases.</summary>
    internal bool OffersKeePassXc => _selectedPath is null && KeePassXc.Count > 0;

    /// <summary>Whether "Create a vault…" is the welcome's first action, which it is while KeePassXC offers nothing, with "Open a vault…" after it.</summary>
    internal bool OffersCreateFirst => _selectedPath is null && KeePassXc.Count == 0;

    /// <summary>Choosing a database KeePassXC opened goes to the ordinary unlock with its path.</summary>
    internal KeePassXcDatabaseItem? SelectedKeePassXc
    {
        get => null;
        set
        {
            if (value is not null)
            {
                Offer(value.Path);
                OnPropertyChanged(nameof(SelectedKeePassXc));
            }
        }
    }

    /// <summary>What the heading of the create form says.</summary>
    internal string CreateHeading => $"Create {NewVaultName}";

    /// <summary>Whether the recent list has a vault in it other than the one already selected.</summary>
    internal bool ShowsRecent => Recent.Any(item => !string.Equals(item.Path, _selectedPath, PathIdentity.Comparison));

    /// <summary>The vaults this machine has opened, most recent first.</summary>
    internal IReadOnlyList<RecentVaultItem> Recent { get; private set; } = [];

    /// <summary>Whether there is anything to show in the recent list.</summary>
    internal bool HasRecent => Recent.Count > 0;

    /// <summary>Shown when there is not.</summary>
    internal bool HasNoRecent => Recent.Count == 0;

    /// <summary>Opens the picker for an existing vault, from Browse or <c>Ctrl/Cmd+O</c>.</summary>
    /// <remarks>Off wherever Browse is hidden, so the chord cannot reach past a create or restore form.</remarks>
    internal AsyncRelayCommand BrowseCommand { get; }

    /// <summary>Opens the save picker and, if a path comes back, shows the create fields.</summary>
    internal AsyncRelayCommand StartCreateCommand { get; }

    /// <summary>Abandons the create form and forgets both passwords.</summary>
    internal AsyncRelayCommand CancelCreateCommand { get; }

    /// <summary>Opens the restore panel for the selected vault's backups.</summary>
    internal RelayCommand StartRestoreCommand { get; }

    /// <summary>Leaves the restore panel, forgetting whatever was typed into it.</summary>
    internal RelayCommand CloseRestoreCommand { get; }

    /// <summary>The restore panel, while it is open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRestoring), nameof(IsOpening), nameof(OffersQuickUnlock), nameof(OffersRestore), nameof(HasLooseError))]
    [NotifyPropertyChangedFor(nameof(Subtitle), nameof(QuickUnlockIsPrimary), nameof(UnlockIsPrimary))]
    [NotifyCanExecuteChangedFor(nameof(CloseRestoreCommand), nameof(BrowseCommand), nameof(StartRestoreCommand), nameof(ResumeCommand))]
    internal partial RestoreBackupViewModel? Restore { get; private set; }

    internal bool IsRestoring => Restore is not null;

    /// <summary>Whether the selected vault has backups V.4a's saves left beside it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OffersRestore))]
    [NotifyCanExecuteChangedFor(nameof(StartRestoreCommand))]
    internal partial bool HasBackups { get; private set; }

    /// <summary>Whether the quiet "Restore a backup" action shows under the unlock controls.</summary>
    internal bool OffersRestore => HasBackups && IsOpening;

    /// <summary>
    /// Whether the selection is a path with no readable vault at it, chosen only because it has
    /// backups. There is nothing to unlock, so the password field and the button stay off.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Heading), nameof(OffersQuickUnlock), nameof(CanTypePassword), nameof(Subtitle), nameof(QuickUnlockIsPrimary))]
    [NotifyPropertyChangedFor(nameof(UnlockIsPrimary))]
    [NotifyCanExecuteChangedFor(nameof(ResumeCommand), nameof(UnlockCommand))]
    internal partial bool IsRestoreOnly { get; private set; }

    internal bool CanTypePassword => _selectedPath is not null && !IsRestoreOnly;

    /// <summary>
    /// What the shell says once on opening: what a restore did, that the vault's keyfile is one
    /// edit from lost (T-28), and that agents and the CLI now use this vault.
    /// </summary>
    internal string? Notice
    {
        get => _chosenNotice is null ? _notice : _notice is null ? _chosenNotice : $"{_notice} {_chosenNotice}";
        private set => _notice = value;
    }

    /// <summary>What the shell says when the first create or unlock chose this vault for agents and the CLI.</summary>
    internal const string ChosenNotice =
        "Agents and the keypaste CLI now use this vault when no --vault is given. Settings changes it.";

    /// <summary>Asks for a keyfile, for opening, creating or restoring alike.</summary>
    internal AsyncRelayCommand ChooseKeyfileCommand { get; }

    /// <summary>Stops using a keyfile.</summary>
    internal RelayCommand ClearKeyfileCommand { get; }

    /// <summary>
    /// The keyfile the vault needs as well as, or instead of, a password, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Selecting a remembered vault fills this with the keyfile it last opened with, and a successful
    /// open or create remembers it again. The file's location is recorded, never its contents (T-27).
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyfileName), nameof(HasKeyfile), nameof(KeyfileNote), nameof(HasKeyfileNote))]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand), nameof(ClearKeyfileCommand))]
    internal partial string? KeyfilePath { get; private set; }

    /// <summary>The keyfile's name. The full path is a tooltip, as a vault's is.</summary>
    internal string KeyfileName =>
        KeyfilePath is null ? string.Empty : System.IO.Path.GetFileName(KeyfilePath);

    internal bool HasKeyfile => KeyfilePath is not null;

    /// <summary>Makes a new keyfile where the person chooses and has the new vault need it.</summary>
    internal AsyncRelayCommand CreateKeyfileCommand { get; }

    /// <summary>What the create form says about a keyfile keypaste just made for the new vault, while the form still uses it.</summary>
    internal string KeyfileNote =>
        IsCreating && _createdKeyfile is { } made && NewVaultPath is { } vault
            && string.Equals(made, KeyfilePath, PathIdentity.Comparison)
            ? CreatedKeyfileNote(made, vault)
            : string.Empty;

    internal bool HasKeyfileNote => KeyfileNote.Length > 0;

    /// <summary>Whether this build reaches hardware keys, so the screen offers one.</summary>
    internal bool OffersHardwareKey => _session.HardwareKeys is not null;

    /// <summary>Shows the controls a vault needs only sometimes, the YubiKey's.</summary>
    internal RelayCommand ShowMoreOptionsCommand { get; }

    /// <summary>Whether More options has been opened for the selected vault.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsHardwareKey), nameof(OffersMoreOptions))]
    [NotifyCanExecuteChangedFor(nameof(ShowMoreOptionsCommand))]
    internal partial bool MoreOptions { get; private set; }

    /// <summary>
    /// Whether the YubiKey controls show: straight away for a vault whose recent entry records a slot,
    /// otherwise under More options.
    /// </summary>
    internal bool ShowsHardwareKey => OffersHardwareKey && (MoreOptions || HardwareKeySlot is not null);

    /// <summary>Whether More options is offered, which it is while it hides something.</summary>
    internal bool OffersMoreOptions => OffersHardwareKey && !ShowsHardwareKey;

    /// <summary>Asks for the YubiKey as well, in slot 2 until another is chosen.</summary>
    internal RelayCommand UseHardwareKeyCommand { get; }

    /// <summary>Moves between the YubiKey's two slots.</summary>
    internal RelayCommand SwitchSlotCommand { get; }

    /// <summary>Stops asking for the YubiKey.</summary>
    internal RelayCommand ClearHardwareKeyCommand { get; }

    /// <summary>Stops waiting for a touch, so the unlock fails and opens nothing.</summary>
    internal RelayCommand CancelTouchCommand { get; }

    /// <summary>
    /// The YubiKey slot the vault needs as well, or <see langword="null"/>. Selecting a remembered
    /// vault fills it with the slot that vault last opened with, and a successful open remembers it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsHardwareKey), nameof(UsesHardwareKey), nameof(HardwareKeyLabel), nameof(OtherSlotLabel))]
    [NotifyPropertyChangedFor(nameof(OffersMoreOptions))]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand), nameof(UseHardwareKeyCommand), nameof(SwitchSlotCommand), nameof(ClearHardwareKeyCommand))]
    internal partial int? HardwareKeySlot { get; private set; }

    internal bool UsesHardwareKey => HardwareKeySlot is not null;

    internal string HardwareKeyLabel => HardwareKeySlot is { } slot ? Invariant($"YubiKey, slot {slot}") : string.Empty;

    internal string OtherSlotLabel => HardwareKeySlot == 1 ? "Use slot 2" : "Use slot 1";

    /// <summary>Whether the YubiKey is waiting to be touched.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QuickUnlockIsPrimary), nameof(UnlockIsPrimary))]
    [NotifyCanExecuteChangedFor(nameof(CancelTouchCommand))]
    internal partial bool IsWaitingForTouch { get; private set; }

    /// <summary>Whether the create fields are showing instead of the unlock ones.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyfileNote), nameof(IsOpening), nameof(HasKeyfileNote), nameof(OffersQuickUnlock), nameof(OffersRestore))]
    [NotifyPropertyChangedFor(nameof(HasLooseError), nameof(Subtitle), nameof(QuickUnlockIsPrimary), nameof(UnlockIsPrimary))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand), nameof(CreateKeyfileCommand), nameof(BrowseCommand), nameof(StartRestoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
    internal partial bool IsCreating { get; private set; }

    /// <summary>The other half of <see cref="IsCreating"/>, for the open controls' visibility.</summary>
    internal bool IsOpening => !IsCreating && Restore is null;

    /// <summary>The file name the new vault will have, for the heading.</summary>
    internal string NewVaultName =>
        NewVaultPath is null ? string.Empty : System.IO.Path.GetFileName(NewVaultPath);

    /// <summary>How many characters are in the new-password field.</summary>
    internal int NewMaskedLength => _new.Length;

    /// <summary>How many characters are in the confirmation field.</summary>
    internal int ConfirmMaskedLength => _confirm.Length;

    /// <summary>The vault about to be opened.</summary>
    internal string? SelectedPath
    {
        get => _selectedPath;
        set
        {
            if (SetProperty(ref _selectedPath, value))
            {
                OnPropertyChanged(nameof(Heading));
                OnPropertyChanged(nameof(SelectedRecent));
                OnPropertyChanged(nameof(IsHandOff));
                OnPropertyChanged(nameof(OffersQuickUnlock));
                OnPropertyChanged(nameof(OffersKeePassXc));
                OnPropertyChanged(nameof(OffersCreateFirst));
                OnPropertyChanged(nameof(ShowsRecent));
                OnPropertyChanged(nameof(CanTypePassword));
                OnPropertyChanged(nameof(SelectedName));
                OnPropertyChanged(nameof(SelectedLocation));
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(Subtitle));
                OnPropertyChanged(nameof(QuickUnlockIsPrimary));
                OnPropertyChanged(nameof(UnlockIsPrimary));
                OnPropertyChanged(nameof(WelcomeSubtitle));
                OnPropertyChanged(nameof(HasLooseError));
                ResumeCommand.NotifyCanExecuteChanged();
                UnlockCommand.NotifyCanExecuteChanged();
                Message = string.Empty;
                NewFileLocation = null;
                Owner = string.Empty;
                MoreOptions = false;
                KeyfilePath = RememberedKeyfile(value);
                HardwareKeySlot = OffersHardwareKey ? Remembered(value)?.HardwareKeySlot : null;
                Look();
            }
        }
    }

    /// <summary>The selected vault's file name, for the heading.</summary>
    internal string SelectedName =>
        _selectedPath is null ? string.Empty : Shown(System.IO.Path.GetFileName(_selectedPath));

    /// <summary>The selected vault's full path, every character of it visible, for the heading's tooltip.</summary>
    internal string? SelectedLocation => _selectedPath is null ? null : DisplayTextSanitizer.Escaped(_selectedPath);

    internal bool HasSelection => _selectedPath is not null;

    /// <summary>Where a file handed here is when keypaste has not opened it, every character of it visible, until another is selected: typing does not clear it, because it is what tells a look-alike from the person's vault.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewFileLocation), nameof(NewFileNotice))]
    internal partial string? NewFileLocation { get; private set; }

    internal bool HasNewFileLocation => NewFileLocation is not null;

    /// <summary>What a screen reader hears of it.</summary>
    internal string? NewFileNotice => NewFileLocation is { } location ? $"keypaste hasn't opened this file before: {location}" : null;

    /// <summary>The row the recent list has selected, which drives <see cref="SelectedPath"/>.</summary>
    internal RecentVaultItem? SelectedRecent
    {
        get => Recent.FirstOrDefault(item =>
            string.Equals(item.Path, _selectedPath, PathIdentity.Comparison));

        set
        {
            if (value is { Exists: true })
            {
                SelectedPath = value.Path;
            }
            else if (value is not null && !OfferForRestore(value.Path, "That file isn't there any more"))
            {
                Fail("That file isn't there any more. Choose it from where it is now.");
            }
        }
    }

    /// <summary>How many characters have been typed. The control renders this many dots.</summary>
    internal int MaskedLength => _master.Length;

    /// <summary>One calm sentence, or nothing: a note, or with <see cref="IsError"/> what was refused.</summary>
    internal string Message
    {
        get => _message;
        set => Say(value, failed: false);
    }

    internal bool HasMessage => _message.Length > 0;

    /// <summary>Whether <see cref="Message"/> says something was refused or failed, which the screen draws in red.</summary>
    internal bool IsError => Failed && HasMessage;

    /// <summary>A note, drawn in the quiet slot below the form.</summary>
    internal bool HasNote => HasMessage && !Failed;

    /// <summary>A refusal with no field to sit under: nothing is selected, so there is no password field.</summary>
    internal bool HasLooseError => IsError && IsOpening && !HasSelection;

    /// <summary>
    /// Which process holds the selected vault, from the claim that refused the last unlock, or
    /// nothing. Unlike <see cref="Message"/> it stays while the person types.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOwner))]
    internal partial string Owner { get; private set; } = string.Empty;

    internal bool HasOwner => Owner.Length > 0;

    /// <summary>Whether Argon2 is running.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResumeCommand), nameof(UnlockCommand), nameof(BrowseCommand), nameof(StartCreateCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand), nameof(CancelCreateCommand), nameof(StartRestoreCommand), nameof(ChooseKeyfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateKeyfileCommand), nameof(ClearKeyfileCommand), nameof(UseHardwareKeyCommand), nameof(SwitchSlotCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearHardwareKeyCommand))]
    internal partial bool Busy { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsError), nameof(HasNote), nameof(HasLooseError))]
    private partial bool Failed { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyfileNote), nameof(NewVaultName), nameof(HasKeyfileNote), nameof(CreateHeading))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private partial string? NewVaultPath { get; set; }

    // A keyfile or a hardware key alone is a whole answer: KeePassXC makes vaults with no password.
    private bool CanUnlock =>
        !Busy && CanTypePassword && (_master.Length > 0 || KeyfilePath is not null || HardwareKeySlot is not null);

    // The confirmation is not required to be non-empty here: an empty one that does not match is
    // VaultCreation's refusal to make, not a reason to grey out the button and explain nothing.
    private bool CanCreate => !Busy && IsCreating && NewVaultPath is not null && _new.Length > 0;

    /// <summary>Appends one typed character.</summary>
    internal void Type(char c)
    {
        if (_disposed)
        {
            return;
        }

        _master.Append(c);
        AfterTyping();
    }

    /// <summary>Removes the last character.</summary>
    internal void Backspace()
    {
        if (_disposed)
        {
            return;
        }

        _master.Backspace();
        AfterTyping();
    }

    /// <summary>Empties the field.</summary>
    internal void ClearPassword()
    {
        if (_disposed)
        {
            return;
        }

        _master.Clear();
        AfterTyping();
    }

    /// <summary>Appends what is on the clipboard to the master password, as a person could have typed it.</summary>
    internal Task PasteAsync() => PasteIntoAsync(pasted =>
    {
        _master.Append(pasted.Value);
        AfterTyping();
    });

    /// <summary>Appends what is on the clipboard to the new master password.</summary>
    internal Task PasteNewAsync() => PasteIntoAsync(pasted => EditCreation(() => _new.Append(pasted.Value)));

    /// <summary>Appends what is on the clipboard to the confirmation.</summary>
    internal Task PasteConfirmAsync() => PasteIntoAsync(pasted => EditCreation(() => _confirm.Append(pasted.Value)));

    /// <summary>Appends one character to the new master password.</summary>
    internal void TypeNew(char c) => EditCreation(() => _new.Append(c));

    /// <summary>Removes the last character of the new master password.</summary>
    internal void BackspaceNew() => EditCreation(_new.Backspace);

    /// <summary>Empties the new master password.</summary>
    internal void ClearNew() => EditCreation(_new.Clear);

    /// <summary>Appends one character to the confirmation.</summary>
    internal void TypeConfirm(char c) => EditCreation(() => _confirm.Append(c));

    /// <summary>Removes the last character of the confirmation.</summary>
    internal void BackspaceConfirm() => EditCreation(_confirm.Backspace);

    /// <summary>Empties the confirmation.</summary>
    internal void ClearConfirm() => EditCreation(_confirm.Clear);

    /// <summary>
    /// Reads the clipboard into a buffer of the paste's own, and appends it only once the
    /// clipboard has answered and nothing is reading the fields, so an unlock or create never sees half a paste.
    /// </summary>
    private async Task PasteIntoAsync(Action<SecretBuffer> append)
    {
        if (_disposed || Busy || _paste is null)
        {
            return;
        }

        using var pasted = new SecretBuffer();
        var outcome = await _paste(pasted).ConfigureAwait(true);

        if (_disposed || Busy)
        {
            return;
        }

        if (outcome == PasteOutcome.Pasted)
        {
            append(pasted);
        }
        else
        {
            Message = SecretField.NoteFor(outcome);
        }
    }

    private void EditCreation(Action edit)
    {
        if (_disposed)
        {
            return;
        }

        edit();
        OnPropertyChanged(nameof(NewMaskedLength));
        OnPropertyChanged(nameof(ConfirmMaskedLength));
        CreateCommand.NotifyCanExecuteChanged();

        if (HasMessage)
        {
            Message = string.Empty;
        }
    }

    /// <summary>
    /// Offers a file the user dropped or picked, answering "is that a vault?" before asking for a
    /// password.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns><see langword="true"/> when it is a KDBX vault and is now selected.</returns>
    internal bool Offer(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
        {
            if (!OfferForRestore(path, "That file isn't there any more"))
            {
                Fail("That file isn't there any more. Choose it from where it is now.");
            }

            return false;
        }

        if (!KdbxHeader.IsVaultFile(path))
        {
            if (!OfferForRestore(path, "That isn't a readable KeePass vault"))
            {
                Fail("That isn't a KeePass vault. Choose a .kdbx file.");
            }

            return false;
        }

        SelectedPath = System.IO.Path.GetFullPath(path);
        return true;
    }

    /// <summary>Whether this screen is doing nothing a file arriving could get in the way of.</summary>
    private bool IsFree => !_disposed && !Busy && IsOpening && !IsWaitingForTouch && !IsKeepingQuickUnlock;

    /// <summary>Whether dropping <paramref name="path"/> here would select it: a vault, or a damaged one with backups beside it.</summary>
    internal bool TakesDrop(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return IsFree && (KdbxHeader.IsVaultFile(path) || VaultBackups.List(System.IO.Path.GetFullPath(path)).Count > 0);
    }

    /// <summary>What the drop's caption says.</summary>
    internal static string DropCaption(string path) => $"Open {Shown(System.IO.Path.GetFileName(path))}";

    /// <summary>Selects a file the person dropped here; a password typed for another vault is not carried over to it.</summary>
    /// <returns><see langword="true"/> when it is a vault and is now selected.</returns>
    internal bool TakeDrop(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!IsFree)
        {
            return false;
        }

        var before = _selectedPath;
        var taken = Offer(path);

        if (!string.Equals(before, _selectedPath, PathIdentity.Comparison))
        {
            ResetPassword();
        }

        return taken;
    }

    /// <summary>
    /// Offers a file the system or a second start named, which only ever selects it: it never unlocks anything, and
    /// never takes the place of a form in use or of the vault a password is being typed for (THREATS.md T-29).
    /// </summary>
    internal void TakeHanded(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (_disposed)
        {
            return;
        }

        var name = Shown(System.IO.Path.GetFileName(path));

        if (!HandedFile.IsLocal(path))
        {
            Fail($"keypaste won't open {name} from a network location it was handed. Choose it with Open a vault instead.");
            return;
        }

        if (HandedWait() is { } why)
        {
            Message = $"keypaste left {name} unopened while {why}. Open it again when you're done.";
            return;
        }

        var taken = Offer(path);

        // Offered for restore counts too: that selects the file, with nothing else on the screen saying where it is.
        if (string.Equals(_selectedPath, System.IO.Path.GetFullPath(path), PathIdentity.Comparison) && Remembered(_selectedPath) is null)
        {
            NewFileLocation = DisplayTextSanitizer.Escaped(_selectedPath!);
        }

        if (taken)
        {
            FileChosen?.Invoke(this, EventArgs.Empty);
        }
    }

    private string? HandedWait() =>
        !IsOpening ? "this form is open"
        : !IsFree ? "a vault is opening"
        : _master.Length > 0 ? "a password is typed here"
        : null;

    private static string Shown(string text) => DisplayTextSanitizer.Sanitize(text).Text;

    /// <summary>Offers a file the import dialog handed over to keep editing in place, with the keyfile and YubiKey slot it opens with.</summary>
    /// <returns><see langword="true"/> when it is a KDBX vault and is now selected.</returns>
    internal bool Offer(string path, string? keyfile, int? hardwareKeySlot = null)
    {
        if (!Offer(path))
        {
            return false;
        }

        if (keyfile is not null)
        {
            KeyfilePath = System.IO.Path.GetFullPath(keyfile);
        }

        if (hardwareKeySlot is not null && OffersHardwareKey)
        {
            HardwareKeySlot = hardwareKeySlot;
        }

        _handOffPath = _selectedPath;
        OnPropertyChanged(nameof(IsHandOff));
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(Subtitle));
        return true;
    }

    /// <summary>
    /// Selects a path that holds no readable vault, for restoring only, when backups sit beside it.
    /// </summary>
    /// <remarks>
    /// A damaged or missing vault is the case backups exist for (docs/PRODUCT.md law 5.7), and
    /// refusing to select one would leave its backups unreachable from the only screen that can
    /// restore them. Nothing here can be unlocked, and <see cref="Offer(string)"/> still answers false.
    /// </remarks>
    private bool OfferForRestore(string path, string why)
    {
        var full = System.IO.Path.GetFullPath(path);
        var backups = VaultBackups.List(full).Count;

        if (backups == 0)
        {
            return false;
        }

        SelectedPath = full;
        Message = backups == 1
            ? $"{why}, but a backup of it is kept beside it. Restore it to get the vault back."
            : Invariant($"{why}, but {backups} backups of it are kept beside it. Restore one to get the vault back.");
        return true;
    }

    /// <summary>Reads what the selection is: whether anything can be unlocked, whether it has backups, and whether Windows Hello can open it.</summary>
    private void Look()
    {
        HasBackups = _selectedPath is { } path && VaultBackups.List(path).Count > 0;
        IsRestoreOnly = _selectedPath is { } selected && !KdbxHeader.IsVaultFile(selected);
        Resumable = _session.CanResume(_selectedPath);
    }

    /// <summary>Drops a restore that is waiting on a confirmation, as a minimize does to an open vault.</summary>
    internal void CancelPendingRestore() => Restore?.CancelPending();

    private void StartRestore()
    {
        if (_disposed || _selectedPath is not { } path)
        {
            return;
        }

        ResetPassword();
        Message = string.Empty;
        Restore = new RestoreBackupViewModel(
            path, _session.Clock, () => _session.IdleTimeout, _post, OnRestoredAsync, KeyfilePath, _picker.PickKeyfileAsync,
            HardwareKeySlot is { } slot ? () => _session.NewHardwareKey(slot) : null,
            _paste);
    }

    private void CloseRestore()
    {
        Restore?.Dispose();
        Restore = null;
        Look();
    }

    /// <summary>Opens the vault a restore has just put in place, with the password that was checked.</summary>
    private async Task OnRestoredAsync(VaultRestoreReport report, SecretBuffer password, string? keyfile)
    {
        if (_disposed || _selectedPath is not { } path)
        {
            return;
        }

        var slot = HardwareKeySlot;
        var outcome = await Task.Run(() => _session.TryUnlock(path, password.Value, keyfile, slot)).ConfigureAwait(true);

        CloseRestore();
        KeyfilePath = keyfile;

        if (outcome != UnlockOutcome.Opened)
        {
            Message = "The backup was restored. Unlock it with the password, key file and YubiKey that backup was made under.";
            return;
        }

        Notice = Describe(report);
        Remember(path, keyfile, slot);
        await LandAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// After a password unlock, has Windows Hello asked whether to keep this unlock for a quick one, as KeePassXC asks
    /// after each, then hands the shell the vault, which is open whatever the answer.
    /// </summary>
    /// <remarks>
    /// A screen replaced meanwhile still hands it over while the unlock is the session's own, so the app locks a vault
    /// nobody is looking at, but not once a lock and another unlock have followed it.
    /// </remarks>
    private async Task LandAsync()
    {
        var unlock = _session.SessionId;

        if (_session.QuickUnlock is not null && !_disposed)
        {
            IsKeepingQuickUnlock = true;

            try
            {
                await Task.Run(_session.KeepForQuickUnlock).ConfigureAwait(true);
            }
            finally
            {
                IsKeepingQuickUnlock = false;
            }
        }

        if (!_disposed || _session.SessionId == unlock)
        {
            _unlocked();
        }
    }

    /// <summary>Opens the selected vault from its quick unlock copy, once the person answers Windows Hello.</summary>
    /// <remarks><c>internal</c> for the reason <see cref="UnlockAsync"/> is.</remarks>
    internal async Task ResumeAsync()
    {
        if (_disposed || _selectedPath is not { } path || !OffersQuickUnlock)
        {
            return;
        }

        Busy = true;
        Message = string.Empty;
        Owner = string.Empty;

        try
        {
            var outcome = await Task.Run(() => _session.TryResume(path)).ConfigureAwait(true);

            if (outcome == UnlockOutcome.Opened)
            {
                var keyfile = _session.Unlocked?.KeyfilePath;
                Remember(path, keyfile, _session.HardwareKeySlot);
                ResetPassword();
                Notice = FragileNotice(keyfile);
                _unlocked();
                return;
            }

            Resumable = _session.CanResume(path);

            if (ExplainResume(outcome) is { } refusal)
            {
                Fail(refusal);
            }

            if (outcome == UnlockOutcome.HeldElsewhere)
            {
                ShowOwner();
            }
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Why a quick unlock opened nothing, or null when the person cancelled it and knows.</summary>
    private string? ExplainResume(UnlockOutcome outcome) => outcome switch
    {
        UnlockOutcome.HeldElsewhere => HeldElsewhere(),
        UnlockOutcome.HardwareKeyFailed => ExplainHardwareKey(_session.LastHardwareKeyFailure, HardwareKeySlot),
        UnlockOutcome.KeyChanged =>
            $"This vault's password or key file was changed after it locked, so {QuickUnlockName} can't unlock it. Unlock it with the new ones.",
        UnlockOutcome.QuickUnlockRefused => _session.LastQuickUnlock switch
        {
            QuickUnlockOutcome.Cancelled => null,
            QuickUnlockOutcome.Unavailable => $"{QuickUnlockName} isn't available right now. Enter the master password to unlock.",
            QuickUnlockOutcome.Unreadable or QuickUnlockOutcome.None =>
                $"{QuickUnlockName} can't unlock this vault any more. Enter the master password; {QuickUnlockName} is offered again after that.",
            _ => $"{QuickUnlockName} didn't unlock this vault. Try again, or enter the master password.",
        },
        _ => Explain(outcome),
    };

    private static string Describe(VaultRestoreReport report)
    {
        static string When(VaultBackup backup) =>
            Keypaste.App.Globalization.PersonTime.Moment(backup.TakenAt.ToLocalTime());

        var kept = report switch
        {
            { Preserved: { } preserved } =>
                $"The vault it replaced is kept beside it as the backup from {When(preserved)}.",
            { AlreadyKeptAs: { } already } =>
                $"The vault it replaced was already kept, as the backup from {When(already)}.",
            _ => "There was no file to keep.",
        };

        return $"Restored the backup from {When(report.Restored)}. {kept} " +
            "This vault opens with the master password that backup was made under.";
    }

    /// <summary>Forgets every vault, as File › Open recent › Clear menu asks.</summary>
    internal void ForgetAll()
    {
        RecentVaults.Save(KeypasteHome.RecentPath(_home), []);
        Reload();
    }

    /// <summary>Forgets one vault and rewrites the list.</summary>
    internal void Forget(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var recent = KeypasteHome.RecentPath(_home);
        _remembered = RecentVaults.Forget(RecentVaults.Load(recent), path);
        RecentVaults.Save(recent, _remembered);

        if (_selectedPath is not null && string.Equals(_selectedPath, System.IO.Path.GetFullPath(path), PathIdentity.Comparison))
        {
            SelectedPath = null;
        }

        Project();
    }

    /// <summary>
    /// Opens the selected vault.
    /// </summary>
    /// <remarks>
    /// <c>internal</c> rather than private because <see cref="UnlockCommand"/> is an
    /// <see cref="System.Windows.Input.ICommand"/>, whose <c>Execute</c> returns void — so a test
    /// driving the command cannot know when the unlock finished, and would be asserting against a
    /// race. The command still wraps this; the seam only exists so a test can await the same work
    /// the button does.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanUnlock))]
    internal async Task UnlockAsync()
    {
        if (_disposed || _selectedPath is not { } path)
        {
            return;
        }

        Busy = true;
        Message = string.Empty;
        Owner = string.Empty;

        try
        {
            // Argon2 is a good fraction of a second by design. Off the UI thread, or the window
            // stops painting and the app looks broken at the exact moment it is working hardest.
            // A hardware key waits for its touch there too, while this screen says to touch it.
            var keyfile = KeyfilePath;
            var slot = HardwareKeySlot;
            var outcome = await Task.Run(() => _session.TryUnlock(path, _master.Value, keyfile, slot))
                .ConfigureAwait(true);

            if (outcome == UnlockOutcome.Opened)
            {
                Remember(path, keyfile, slot);
                ResetPassword();
                Notice = FragileNotice(keyfile);
                await LandAsync().ConfigureAwait(true);
                return;
            }

            if (outcome == UnlockOutcome.HeldElsewhere)
            {
                Fail(HeldElsewhere());
                ShowOwner();
                ResetPassword();
                return;
            }

            if (outcome == UnlockOutcome.HardwareKeyFailed)
            {
                Fail(ExplainHardwareKey(_session.LastHardwareKeyFailure, slot));
                ResetPassword();
                return;
            }

            // A KDBX file does not record that it needs a second factor, so the password alone failing may mean that.
            var maybeSecondFactor = outcome == UnlockOutcome.WrongPassword && keyfile is null && slot is null;
            var explained = outcome == UnlockOutcome.KeyfileUnusable && keyfile is not null
                ? ExplainKeyfile(VaultKeyfile.Inspect(keyfile).Outcome)
                : maybeSecondFactor
                    ? ExplainPasswordAlone()
                    : Explain(outcome, keyfile is not null, slot is not null);

            MoreOptions |= maybeSecondFactor;

            Fail(HasBackups && outcome != UnlockOutcome.KeyfileUnusable
                ? $"{explained} If the file is damaged, or its password or key file was changed and lost, restore a backup."
                : explained);
            ResetPassword();
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>
    /// Asks where an existing vault is, and offers whatever comes back.
    /// </summary>
    /// <remarks>
    /// A cancelled picker answers null and this does nothing at all — no message, because choosing
    /// not to choose is not a failure.
    /// </remarks>
    internal async Task BrowseAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (await _picker.PickExistingAsync().ConfigureAwait(true) is { } path && Offer(path))
        {
            FileChosen?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>A vault was picked in the system's open panel, which took the keyboard while it was up.</summary>
    internal event EventHandler? FileChosen;

    /// <summary>
    /// Asks where a new vault should go, and opens the create fields if somewhere came back.
    /// </summary>
    /// <remarks>
    /// The occupied-path refusal is answered here as well as inside the create itself, so a person
    /// learns the file is taken while the picker is still fresh in mind rather than after typing a
    /// master password twice. It is <see cref="VaultCreation"/> that decides, not a second copy of
    /// the rule.
    /// </remarks>
    internal async Task StartCreateAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (await _picker.PickNewAsync().ConfigureAwait(true) is not { } path)
        {
            return;
        }

        var full = System.IO.Path.GetFullPath(path);

        if (VaultCreation.Inspect(full) == VaultDestination.Occupied)
        {
            Fail("There's already a file there. Choose a name that isn't taken.");
            return;
        }

        NewVaultPath = full;
        Message = string.Empty;
        _createdKeyfile = null;
        KeyfilePath = null;
        HardwareKeySlot = null;
        IsCreating = true;
    }

    /// <summary>
    /// Makes the vault and opens the session on it.
    /// </summary>
    /// <remarks>
    /// <c>internal</c> for the same reason <see cref="UnlockAsync"/> is: a test driving the command
    /// cannot know when the work finished, and would be asserting against a race.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanCreate))]
    internal async Task CreateAsync()
    {
        if (_disposed || NewVaultPath is not { } path)
        {
            return;
        }

        Busy = true;
        Message = string.Empty;

        try
        {
            // Argon2 again, and off the UI thread again. Creating derives a key exactly as opening
            // does, so the window would stop painting for the same good fraction of a second.
            var keyfile = KeyfilePath;
            var slot = HardwareKeySlot;
            var outcome = await Task.Run(() => _session.TryCreate(path, _new.Value, _confirm.Value, keyfile, slot))
                .ConfigureAwait(true);

            if (outcome == VaultCreationOutcome.Created)
            {
                Remember(path, keyfile, slot);
                ResetCreation();
                IsCreating = false;
                _unlocked();
                return;
            }

            Fail(_session.HeldElsewhere is not null ? HeldElsewhere()
                : _session.LastHardwareKeyFailure is { } keyFailure ? ExplainCreationKey(keyFailure, slot)
                : ExplainCreation(outcome, slot));
            ShowOwner();

            // The passwords go whatever the answer was. A refusal means starting the pair again,
            // which is what `keypaste init` makes a person do and is the only way the buffers are
            // not left holding a master password while somebody decides what to do next.
            ResetCreation();
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Leaves the create form, forgetting both passwords and the chosen path.</summary>
    internal Task CancelCreateAsync()
    {
        if (!_disposed)
        {
            ResetCreation();
            NewVaultPath = null;
            Message = string.Empty;
            _createdKeyfile = null;
            KeyfilePath = RememberedKeyfile(_selectedPath);
            HardwareKeySlot = OffersHardwareKey ? Remembered(_selectedPath)?.HardwareKeySlot : null;
            IsCreating = false;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Asks for a keyfile and takes it only if it is one, saying why not at once rather than after a
    /// password has been typed.
    /// </summary>
    internal async Task ChooseKeyfileAsync()
    {
        if (_disposed || await _picker.PickKeyfileAsync().ConfigureAwait(true) is not { } picked)
        {
            return;
        }

        var full = System.IO.Path.GetFullPath(picked);
        var inspection = VaultKeyfile.Inspect(full);

        if (!inspection.Accepted)
        {
            Fail(ExplainKeyfile(inspection.Outcome));
            return;
        }

        _createdKeyfile = null;
        KeyfilePath = full;

        if (inspection.IsFragile && IsCreating)
        {
            Fail("keypaste won't make a vault that needs this file: it is keyed by the file's exact bytes, so one edit to it loses the vault. Choose a KeePass key file.");
        }
        else
        {
            Message = inspection.IsFragile ? FragileNotice(full)! : string.Empty;
        }
    }

    /// <summary>
    /// Asks where a new keyfile should go, writes one there and has the new vault need it, saying at once to keep a
    /// copy elsewhere.
    /// </summary>
    /// <remarks>
    /// The file is written when the person chooses where, before the vault, so it is there to copy whatever happens
    /// next; it is written only where nothing is.
    /// </remarks>
    internal async Task CreateKeyfileAsync()
    {
        if (_disposed || !IsCreating || NewVaultPath is not { } vault)
        {
            return;
        }

        if (await _picker.PickNewKeyfileAsync(System.IO.Path.GetFileNameWithoutExtension(vault) + ".keyx").ConfigureAwait(true) is not { } picked
            || _disposed || !IsCreating)
        {
            return;
        }

        var full = System.IO.Path.GetFullPath(picked);
        var outcome = VaultKeyfile.Create(full, out _);

        if (outcome != KeyfileCreationOutcome.Created)
        {
            Fail(ExplainKeyfileCreation(outcome));
            return;
        }

        _createdKeyfile = full;
        KeyfilePath = full;
        Message = string.Empty;
    }

    /// <summary>What a person is told about a keyfile keypaste just made for <paramref name="vault"/>: to keep a copy elsewhere, and when it sits in the vault's folder.</summary>
    /// <param name="keyfile">The keyfile.</param>
    /// <param name="vault">The vault it protects.</param>
    internal static string CreatedKeyfileNote(string keyfile, string vault)
    {
        var name = System.IO.Path.GetFileName(keyfile);
        var note = $"keypaste saved {name}. Keep a copy somewhere other than this computer: without it the vault can't be opened, and nothing recovers it.";

        return string.Equals(System.IO.Path.GetDirectoryName(keyfile), System.IO.Path.GetDirectoryName(vault), PathIdentity.Comparison)
            ? $"{note} It's in the vault's folder, so whoever copies that folder gets the vault and its key file together."
            : note;
    }

    /// <summary>Why no keyfile was made, in the register of <see cref="Explain"/>.</summary>
    internal static string ExplainKeyfileCreation(KeyfileCreationOutcome outcome) => outcome switch
    {
        KeyfileCreationOutcome.PathAlreadyExists => "There's already a file there, and keypaste won't replace it. Choose a name that isn't taken.",
        KeyfileCreationOutcome.Unsupported => "This build of keypaste can't read a KeePass XML key file, so it won't make one. Choose a key file you already have.",
        _ => "The key file couldn't be saved there. Choose another folder.",
    };

    /// <summary>The T-28 warning for a keyfile keyed by its hash, or null for any other.</summary>
    private static string? FragileNotice(string? keyfile) =>
        keyfile is not null && VaultKeyfile.Inspect(keyfile).IsFragile
            ? $"This vault's key file, {System.IO.Path.GetFileName(keyfile)}, is an ordinary file keyed by its exact bytes. " +
              "Editing it, re-saving it or letting something sync over it loses the vault for good. Change to a KeePass key file in Settings."
            : null;

    /// <summary>Why a file cannot be used as a keyfile, in the register of <see cref="Explain"/>.</summary>
    internal static string ExplainKeyfile(KeyfileOutcome outcome) => outcome switch
    {
        KeyfileOutcome.Missing => "That key file isn't there any more. Choose it from where it is now.",
        KeyfileOutcome.Unreadable => "That key file couldn't be read. Check that you can open it, then choose it again.",
        KeyfileOutcome.Empty => "That file is empty, so it can't be a key file. Choose another.",
        KeyfileOutcome.IsAVault => "That's a KeePass vault, not a key file.",
        KeyfileOutcome.XmlUnreadable =>
            "That's a KeePass XML key file, and this build of keypaste can't read one: it would use the wrong key. Nothing was opened. Open this vault in KeePassXC instead.",
        _ => "That file can't be used as a key file. Choose another.",
    };

    private string? RememberedKeyfile(string? vaultPath) => Remembered(vaultPath)?.KeyfilePath;

    private RecentVault? Remembered(string? vaultPath) =>
        vaultPath is null
            ? null
            : _remembered.FirstOrDefault(vault => string.Equals(vault.Path, vaultPath, PathIdentity.Comparison));

    /// <summary>Why a hardware key gave no answer, in the register of <see cref="Explain"/>.</summary>
    internal static string ExplainHardwareKey(HardwareKeyException? failure, int? slot) => failure?.Failure switch
    {
        HardwareKeyFailure.NotFound => "No YubiKey found. Plug it in and unlock again.",
        HardwareKeyFailure.MoreThanOne => "More than one YubiKey is plugged in. Leave only the one this vault uses.",
        HardwareKeyFailure.SlotNotConfigured =>
            Invariant($"That YubiKey's slot {slot} isn't set up for challenge-response. If the vault uses the other slot, switch to it."),
        HardwareKeyFailure.TimedOut => "The YubiKey wasn't touched in time. Unlock again and touch it when it flashes.",
        HardwareKeyFailure.Cancelled => "Stopped waiting for the YubiKey. Nothing was opened.",
        _ => failure?.Message ?? "The YubiKey didn't answer. Plug it in again, then try again.",
    };

    /// <summary>
    /// What went wrong making a vault, in the same register as <see cref="Explain"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="VaultCreationOutcome.Created"/> has no sentence because it is not a problem, and
    /// the core's own failure text is not shown: a person at this screen can act on "choose another
    /// name" and cannot act on a writer's exception message.
    /// </remarks>
    private static string ExplainCreation(VaultCreationOutcome outcome, int? slot) => outcome switch
    {
        VaultCreationOutcome.PathAlreadyExists => "There's already a file there. Choose a name that isn't taken.",
        VaultCreationOutcome.EmptyPassword => "A vault needs a master password.",
        VaultCreationOutcome.PasswordsDoNotMatch => "Those two passwords aren't the same.",
        VaultCreationOutcome.KeyfileUnusable => "That key file can't be used. Choose it again, or another.",
        VaultCreationOutcome.KeyfileIsFragile =>
            "keypaste won't make a vault that needs this file: it is keyed by the file's exact bytes, so one edit to it loses the vault. Choose a KeePass key file.",
        VaultCreationOutcome.KeyfileIsThisVault => "A vault can't be its own key file. Choose another file.",
        VaultCreationOutcome.HardwareKeyNotFound =>
            Invariant($"No YubiKey with slot {slot} programmed is plugged in. Plug it in, or program the slot for HMAC-SHA1 challenge-response in YubiKey Manager."),
        _ => "That vault couldn't be created. Check that you can write to that folder, then try again.",
    };

    /// <summary>Why a YubiKey gave the new vault's first save no answer, so no vault was made.</summary>
    private static string ExplainCreationKey(HardwareKeyException failure, int? slot) => failure.Failure switch
    {
        HardwareKeyFailure.NotFound => "No YubiKey found. Plug it in and create the vault again.",
        HardwareKeyFailure.MoreThanOne => "More than one YubiKey is plugged in. Leave only the one the vault will use.",
        HardwareKeyFailure.SlotNotConfigured =>
            Invariant($"That YubiKey's slot {slot} isn't set up for challenge-response. Program it in YubiKey Manager, or switch to the other slot."),
        HardwareKeyFailure.TimedOut => "The YubiKey wasn't touched in time. Create the vault again and touch it when it flashes.",
        HardwareKeyFailure.Cancelled => "Stopped waiting for the YubiKey. No vault was created.",
        _ => failure.Message,
    };

    private string HeldElsewhere()
    {
        var reason = _session.HeldElsewhere ?? "another keypaste process holds this vault.";
        return _heldMessage = char.ToUpperInvariant(reason[0]) + reason[1..];
    }

    private void ShowOwner() =>
        Owner = _session.HeldBy is { } holder ? new AuthorityStatus.HeldBy(holder).Holder : string.Empty;

    /// <summary>The lock note for <paramref name="reason"/>, or empty where the screen already says why.</summary>
    internal static string DescribeLock(VaultLockReason reason, DateTimeOffset at, TimeSpan idleTimeout)
    {
        var time = Keypaste.App.Globalization.PersonTime.Clock(at);

        return reason switch
        {
            VaultLockReason.Idle => $"Locked at {time} after {Duration(idleTimeout)} without use",
            VaultLockReason.Manual => $"You locked it at {time}",
            VaultLockReason.Minimized => $"Locked at {time} when the window was minimized",
            VaultLockReason.Closed => $"Locked at {time} when the window was closed",
            VaultLockReason.Requested => $"Locked at {time} by keypaste lock",
            _ => string.Empty,
        };
    }

    private static string Duration(TimeSpan span)
    {
        static string Unit(int count, string unit) =>
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{count} {unit}{(count == 1 ? string.Empty : "s")}");

        var minutes = (int)Math.Round(span.TotalMinutes);

        return minutes switch
        {
            < 60 => Unit(minutes, "minute"),
            _ when minutes % 60 == 0 => Unit(minutes / 60, "hour"),
            _ => $"{Unit(minutes / 60, "hour")} {Unit(minutes % 60, "minute")}",
        };
    }

    /// <summary>
    /// What went wrong opening, in words that do not shout: red text under the field and never an
    /// icon or a dialog, because a mistyped password is routine.
    /// </summary>
    private static string Explain(UnlockOutcome outcome, bool withKeyfile = false, bool withHardwareKey = false) => outcome switch
    {
        // Naming both factors when both were given, or a good password and the wrong file sends the
        // person to retype the half that was right. A wrong key or slot is refused the same
        // way, because it answers, only differently.
        UnlockOutcome.WrongPassword when withKeyfile && withHardwareKey =>
            "That password, key file and YubiKey didn't open this vault. If the vault uses the other slot, switch to it.",
        UnlockOutcome.WrongPassword when withHardwareKey =>
            "That password and YubiKey didn't open this vault. If the vault uses the other slot, switch to it.",
        UnlockOutcome.WrongPassword when withKeyfile => "That password and key file didn't open this vault. Check both, then try again.",
        UnlockOutcome.WrongPassword => "That password didn't open this vault. Type it again.",
        UnlockOutcome.KeyfileUnusable => "That key file can't be used. Choose it again, or another.",
        UnlockOutcome.NotFound => "That file isn't there any more. Choose it from where it is now.",
        UnlockOutcome.NotAKdbx => "That isn't a KeePass vault. Choose a .kdbx file.",
        _ => "That vault couldn't be opened. Check that you can read the file, then try again.",
    };

    private string ExplainPasswordAlone() => OffersHardwareKey
        ? "That password didn't open this vault. It may also need a key file or a YubiKey: add one below, or type the password again."
        : "That password didn't open this vault. It may also need a key file: add one below, or type the password again.";

    private void Remember(string path, string? keyfile, int? hardwareKeySlot = null)
    {
        var recent = KeypasteHome.RecentPath(_home);
        _remembered = RecentVaults.Remember(RecentVaults.Load(recent), path, DateTimeOffset.UtcNow, keyfile, hardwareKeySlot);
        RecentVaults.Save(recent, _remembered);
        _chosenNotice = ChosenVault.Choose(_home, path, onlyIfNone: true) == ChooseOutcome.Chosen ? ChosenNotice : null;
        Project();
    }

    private void OnHolderLeft(object? sender, EventArgs e) => _post(() =>
    {
        if (!_disposed)
        {
            ShowOwner();

            if (Failed && _message == _heldMessage)
            {
                Message = string.Empty;
            }

            _heldMessage = null;
        }
    });

    private void OnWaitingForTouch(object? sender, bool waiting) => _post(() =>
    {
        if (!_disposed)
        {
            IsWaitingForTouch = waiting;
        }
    });

    private void Reload()
    {
        _remembered = RecentVaults.Load(KeypasteHome.RecentPath(_home));
        Project();

        // The most recent vault that still exists is pre-selected, so the common case is launch,
        // type, Enter — with no mouse and no arrow keys.
        SelectedPath = Recent.FirstOrDefault(item => item.Exists)?.Path;

        if (_selectedPath is null)
        {
            KeePassXc = _keePassXcConfigs is null
                ? []
                : [.. KeePassXcDatabases.ReadAll(_keePassXcConfigs()).Select(path => new KeePassXcDatabaseItem(path))];
            OnPropertyChanged(nameof(KeePassXc));
            OnPropertyChanged(nameof(OffersKeePassXc));
            OnPropertyChanged(nameof(OffersCreateFirst));
            OnPropertyChanged(nameof(WelcomeSubtitle));
        }
    }

    private void Project()
    {
        Recent = [.. _remembered.Select(vault => new RecentVaultItem(vault.Path, File.Exists(vault.Path)))];
        OnPropertyChanged(nameof(Recent));
        OnPropertyChanged(nameof(ShowsRecent));
        OnPropertyChanged(nameof(HasRecent));
        OnPropertyChanged(nameof(HasNoRecent));
    }

    private void Fail(string text) => Say(text, failed: true);

    private void Say(string text, bool failed)
    {
        Failed = failed;

        if (SetProperty(ref _message, text, nameof(Message)))
        {
            OnPropertyChanged(nameof(HasMessage));
            OnPropertyChanged(nameof(IsError));
            OnPropertyChanged(nameof(HasNote));
            OnPropertyChanged(nameof(HasLooseError));
        }
    }

    private void AfterTyping()
    {
        OnPropertyChanged(nameof(MaskedLength));
        UnlockCommand.NotifyCanExecuteChanged();

        if (HasMessage)
        {
            Message = string.Empty;
        }
    }

    /// <summary>
    /// Zeroes the buffer and starts a fresh one.
    /// </summary>
    /// <remarks>
    /// Called on success and on every failure. A wrong password is the path people take most and
    /// the one that would be easiest to leave holding a password until the next keystroke.
    /// </remarks>
    private void ResetPassword()
    {
        _master.Dispose();
        _master = new SecretBuffer();
        OnPropertyChanged(nameof(MaskedLength));
        UnlockCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Zeroes both creation buffers and starts fresh ones.
    /// </summary>
    /// <remarks>
    /// Called after a create whatever it decided, and on cancel. Every path out of the create form
    /// runs through here, which is the only way neither field is left holding a master password.
    /// </remarks>
    private void ResetCreation()
    {
        _new.Dispose();
        _confirm.Dispose();
        _new = new SecretBuffer();
        _confirm = new SecretBuffer();
        OnPropertyChanged(nameof(NewMaskedLength));
        OnPropertyChanged(nameof(ConfirmMaskedLength));
        CreateCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.WaitingForTouch -= OnWaitingForTouch;
        _session.HolderLeft -= OnHolderLeft;
        Restore?.Dispose();
        _master.Dispose();
        _new.Dispose();
        _confirm.Dispose();
    }
}
