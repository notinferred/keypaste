using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Recent;

namespace Keypaste.App.ViewModels;

/// <summary>One remembered vault, as the unlock screen shows it.</summary>
/// <remarks>
/// The list shows <see cref="Name"/> and puts <see cref="Path"/> in a tooltip. the Ideas table in DECISIONS.md's screenshot
/// strategy puts this app beside classic KeePass in marketing shots, and a screenshot should not
/// publish somebody's directory layout.
/// </remarks>
internal sealed class RecentVaultItem(string path, bool exists)
{
    internal string Path { get; } = path;

    internal string Name { get; } = System.IO.Path.GetFileName(path);

    /// <summary>Whether the file is still where it was.</summary>
    /// <remarks>
    /// A missing vault is shown greyed rather than dropped. Silently removing it hides a moved file
    /// and reads as data loss.
    /// </remarks>
    internal bool Exists { get; } = exists;
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
internal sealed class UnlockViewModel : ObservableObject, IDisposable
{
    private readonly AppVaultSession _session;
    private readonly string? _home;
    private readonly Action _unlocked;
    private readonly IVaultFilePicker _picker;
    private readonly Action<Action> _post;

    private SecretBuffer _master = new();
    private SecretBuffer _new = new();
    private SecretBuffer _confirm = new();
    private IReadOnlyList<RecentVault> _remembered = [];
    private string? _selectedPath;
    private string? _keyfilePath;
    private string? _newVaultPath;
    private string _message = string.Empty;
    private string _owner = string.Empty;
    private bool _busy;
    private bool _creating;
    private bool _restoreOnly;
    private int _backups;
    private RestoreBackupViewModel? _restore;
    private bool _disposed;

    internal UnlockViewModel(
        AppVaultSession session,
        string? home,
        IVaultFilePicker picker,
        Action unlocked,
        Action<Action>? post = null,
        string? message = null,
        VaultLockReason? lockedBy = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(picker);
        ArgumentNullException.ThrowIfNull(unlocked);

        _session = session;
        _home = home;
        _picker = picker;
        _unlocked = unlocked;
        _post = post ?? (action => action());

        UnlockCommand = new AsyncRelayCommand(UnlockAsync, () => CanUnlock);
        BrowseCommand = new AsyncRelayCommand(BrowseAsync, () => !_busy && IsOpening);
        StartCreateCommand = new AsyncRelayCommand(StartCreateAsync, () => !_busy);
        CreateCommand = new AsyncRelayCommand(CreateAsync, () => CanCreate);
        CancelCreateCommand = new AsyncRelayCommand(CancelCreateAsync, () => !_busy);
        StartRestoreCommand = new RelayCommand(StartRestore, () => !_busy && HasBackups && IsOpening);
        CloseRestoreCommand = new RelayCommand(CloseRestore, () => _restore is { Busy: false });
        ChooseKeyfileCommand = new AsyncRelayCommand(ChooseKeyfileAsync, () => !_busy);
        ClearKeyfileCommand = new RelayCommand(() => KeyfilePath = null, () => !_busy && _keyfilePath is not null);
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

    /// <summary>The screen's heading: which vault is locked, or an invitation to open one.</summary>
    internal string Heading => _selectedPath is null
        ? "Open a vault"
        : _restoreOnly ? $"{SelectedName} can't be opened" : $"{SelectedName} is locked";

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

    internal AsyncRelayCommand UnlockCommand { get; }

    /// <summary>Opens the picker for an existing vault, from Browse or <c>Ctrl/Cmd+O</c>.</summary>
    /// <remarks>Off wherever Browse is hidden, so the chord cannot reach past a create or restore form.</remarks>
    internal AsyncRelayCommand BrowseCommand { get; }

    /// <summary>Opens the save picker and, if a path comes back, shows the create fields.</summary>
    internal AsyncRelayCommand StartCreateCommand { get; }

    /// <summary>Makes the vault.</summary>
    internal AsyncRelayCommand CreateCommand { get; }

    /// <summary>Abandons the create form and forgets both passwords.</summary>
    internal AsyncRelayCommand CancelCreateCommand { get; }

    /// <summary>Opens the restore panel for the selected vault's backups.</summary>
    internal RelayCommand StartRestoreCommand { get; }

    /// <summary>Leaves the restore panel, forgetting whatever was typed into it.</summary>
    internal RelayCommand CloseRestoreCommand { get; }

    /// <summary>The restore panel, while it is open.</summary>
    internal RestoreBackupViewModel? Restore
    {
        get => _restore;
        private set
        {
            if (Set(ref _restore, value))
            {
                Raise(nameof(IsRestoring));
                Raise(nameof(IsOpening));
                Raise(nameof(OffersRestore));
                StartRestoreCommand.RaiseCanExecuteChanged();
                CloseRestoreCommand.RaiseCanExecuteChanged();
                BrowseCommand.RaiseCanExecuteChanged();
            }
        }
    }

    internal bool IsRestoring => _restore is not null;

    /// <summary>Whether the selected vault has backups V.4a's saves left beside it.</summary>
    internal bool HasBackups => _backups > 0;

    /// <summary>Whether the quiet "Restore a backup" action shows under the unlock controls.</summary>
    internal bool OffersRestore => HasBackups && IsOpening;

    /// <summary>
    /// Whether the selection is a path with no readable vault at it, chosen only because it has
    /// backups. There is nothing to unlock, so the password field and the button stay off.
    /// </summary>
    internal bool IsRestoreOnly => _restoreOnly;

    internal bool CanTypePassword => _selectedPath is not null && !_restoreOnly;

    /// <summary>
    /// What the shell says once on opening: what a restore did, or that the vault's keyfile is one
    /// edit from lost (T-28).
    /// </summary>
    internal string? Notice { get; private set; }

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
    internal string? KeyfilePath
    {
        get => _keyfilePath;
        private set
        {
            if (Set(ref _keyfilePath, value))
            {
                Raise(nameof(KeyfileName));
                Raise(nameof(HasKeyfile));
                UnlockCommand.RaiseCanExecuteChanged();
                ClearKeyfileCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>The keyfile's name. The full path is a tooltip, as a vault's is.</summary>
    internal string KeyfileName =>
        _keyfilePath is null ? string.Empty : System.IO.Path.GetFileName(_keyfilePath);

    internal bool HasKeyfile => _keyfilePath is not null;

    /// <summary>Whether the create fields are showing instead of the unlock ones.</summary>
    internal bool IsCreating
    {
        get => _creating;
        private set
        {
            if (Set(ref _creating, value))
            {
                Raise(nameof(IsOpening));
                Raise(nameof(OffersRestore));
                StartRestoreCommand.RaiseCanExecuteChanged();
                BrowseCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>The other half of <see cref="IsCreating"/>, for the open controls' visibility.</summary>
    internal bool IsOpening => !_creating && _restore is null;

    /// <summary>The file name the new vault will have, for the heading.</summary>
    internal string NewVaultName =>
        _newVaultPath is null ? string.Empty : System.IO.Path.GetFileName(_newVaultPath);

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
            if (Set(ref _selectedPath, value))
            {
                Message = string.Empty;
                Owner = string.Empty;
                KeyfilePath = RememberedKeyfile(value);
                Look();
                Raise(nameof(SelectedName));
                Raise(nameof(HasSelection));
                Raise(nameof(Heading));
                Raise(nameof(ShowsRecent));
            }
        }
    }

    /// <summary>The selected vault's file name, for the heading.</summary>
    internal string SelectedName =>
        _selectedPath is null ? string.Empty : System.IO.Path.GetFileName(_selectedPath);

    internal bool HasSelection => _selectedPath is not null;

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
                Message = "That file isn't there any more.";
            }
        }
    }

    /// <summary>How many characters have been typed. The control renders this many dots.</summary>
    internal int MaskedLength => _master.Length;

    /// <summary>One calm sentence, or nothing.</summary>
    internal string Message
    {
        get => _message;
        set
        {
            if (Set(ref _message, value))
            {
                Raise(nameof(HasMessage));
            }
        }
    }

    internal bool HasMessage => _message.Length > 0;

    /// <summary>
    /// Which process holds the selected vault, from the claim that refused the last unlock, or
    /// nothing. Unlike <see cref="Message"/> it stays while the person types.
    /// </summary>
    internal string Owner
    {
        get => _owner;
        private set
        {
            if (Set(ref _owner, value))
            {
                Raise(nameof(HasOwner));
            }
        }
    }

    internal bool HasOwner => _owner.Length > 0;

    /// <summary>Whether Argon2 is running.</summary>
    internal bool Busy
    {
        get => _busy;
        private set
        {
            if (Set(ref _busy, value))
            {
                UnlockCommand.RaiseCanExecuteChanged();
                BrowseCommand.RaiseCanExecuteChanged();
                ChooseKeyfileCommand.RaiseCanExecuteChanged();
                ClearKeyfileCommand.RaiseCanExecuteChanged();
            }
        }
    }

    // A keyfile alone is a whole answer: KeePassXC makes vaults with no password (D-0282).
    private bool CanUnlock => !_busy && CanTypePassword && (_master.Length > 0 || _keyfilePath is not null);

    // The confirmation is not required to be non-empty here: an empty one that does not match is
    // VaultCreation's refusal to make, not a reason to grey out the button and explain nothing.
    private bool CanCreate => !_busy && _creating && _newVaultPath is not null && _new.Length > 0;

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

    private void EditCreation(Action edit)
    {
        if (_disposed)
        {
            return;
        }

        edit();
        Raise(nameof(NewMaskedLength));
        Raise(nameof(ConfirmMaskedLength));
        CreateCommand.RaiseCanExecuteChanged();

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
                Message = "That file isn't there any more.";
            }

            return false;
        }

        if (!KdbxHeader.IsVaultFile(path))
        {
            if (!OfferForRestore(path, "That isn't a readable KeePass vault"))
            {
                Message = "That isn't a KeePass vault.";
            }

            return false;
        }

        SelectedPath = System.IO.Path.GetFullPath(path);
        return true;
    }

    /// <summary>Offers a file with the keyfile it opens with, as keeping an imported file in place does.</summary>
    /// <returns><see langword="true"/> when it is a KDBX vault and is now selected.</returns>
    internal bool Offer(string path, string? keyfile)
    {
        if (!Offer(path))
        {
            return false;
        }

        if (keyfile is not null)
        {
            KeyfilePath = System.IO.Path.GetFullPath(keyfile);
        }

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
            : $"{why}, but {backups} backups of it are kept beside it. Restore one to get the vault back.";
        return true;
    }

    /// <summary>Reads what the selection is: whether anything can be unlocked, and how many backups it has.</summary>
    private void Look()
    {
        _backups = _selectedPath is { } path ? VaultBackups.List(path).Count : 0;
        _restoreOnly = _selectedPath is { } selected && !(File.Exists(selected) && KdbxHeader.IsVaultFile(selected));

        Raise(nameof(HasBackups));
        Raise(nameof(OffersRestore));
        Raise(nameof(IsRestoreOnly));
        Raise(nameof(Heading));
        Raise(nameof(CanTypePassword));
        UnlockCommand.RaiseCanExecuteChanged();
        StartRestoreCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Drops a restore that is waiting on a confirmation, as a minimize does to an open vault.</summary>
    internal void CancelPendingRestore() => _restore?.CancelPending();

    private void StartRestore()
    {
        if (_disposed || _selectedPath is not { } path)
        {
            return;
        }

        ResetPassword();
        Message = string.Empty;
        Restore = new RestoreBackupViewModel(
            path, _session.Clock, () => _session.IdleTimeout, _post, OnRestoredAsync, _keyfilePath, _picker.PickKeyfileAsync);
    }

    private void CloseRestore()
    {
        _restore?.Dispose();
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

        var outcome = await Task.Run(() => _session.TryUnlock(path, password.Value, keyfile)).ConfigureAwait(true);

        CloseRestore();
        KeyfilePath = keyfile;

        if (outcome != UnlockOutcome.Opened)
        {
            Message = "The backup was restored. Unlock it with the password and keyfile that backup was made under.";
            return;
        }

        Notice = Describe(report);
        Remember(path, keyfile);
        _unlocked();
    }

    private static string Describe(VaultRestoreReport report)
    {
        static string When(VaultBackup backup) =>
            backup.TakenAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.CurrentCulture);

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

    /// <summary>Forgets one vault and rewrites the list.</summary>
    internal void Forget(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        _remembered = RecentVaults.Forget(_remembered, path);
        RecentVaults.Save(KeypasteHome.RecentPath(_home), _remembered);

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
            var keyfile = _keyfilePath;
            var outcome = await Task.Run(() => _session.TryUnlock(path, _master.Value, keyfile))
                .ConfigureAwait(true);

            if (outcome == UnlockOutcome.Opened)
            {
                Remember(path, keyfile);
                ResetPassword();
                Notice = FragileNotice(keyfile);
                _unlocked();
                return;
            }

            if (outcome == UnlockOutcome.HeldElsewhere)
            {
                Message = HeldElsewhere();
                ShowOwner();
                ResetPassword();
                return;
            }

            var explained = outcome == UnlockOutcome.KeyfileUnusable && keyfile is not null
                ? ExplainKeyfile(VaultKeyfile.Inspect(keyfile).Outcome)
                : Explain(outcome, keyfile is not null);

            Message = HasBackups && outcome != UnlockOutcome.KeyfileUnusable
                ? $"{explained} If the file is damaged, or its password or keyfile was changed and lost, restore a backup."
                : explained;
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

        if (await _picker.PickExistingAsync().ConfigureAwait(true) is { } path)
        {
            Offer(path);
        }
    }

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
            Message = "There's already a file there. Choose a name that isn't taken.";
            return;
        }

        _newVaultPath = full;
        Message = string.Empty;
        KeyfilePath = null;
        Raise(nameof(NewVaultName));
        Raise(nameof(CreateHeading));
        IsCreating = true;
        CreateCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Makes the vault and opens the session on it.
    /// </summary>
    /// <remarks>
    /// <c>internal</c> for the same reason <see cref="UnlockAsync"/> is: a test driving the command
    /// cannot know when the work finished, and would be asserting against a race.
    /// </remarks>
    internal async Task CreateAsync()
    {
        if (_disposed || _newVaultPath is not { } path)
        {
            return;
        }

        Busy = true;
        Message = string.Empty;

        try
        {
            // Argon2 again, and off the UI thread again. Creating derives a key exactly as opening
            // does, so the window would stop painting for the same good fraction of a second.
            var keyfile = _keyfilePath;
            var outcome = await Task.Run(() => _session.TryCreate(path, _new.Value, _confirm.Value, keyfile))
                .ConfigureAwait(true);

            if (outcome == VaultCreationOutcome.Created)
            {
                Remember(path, keyfile);
                ResetCreation();
                IsCreating = false;
                _unlocked();
                return;
            }

            Message = _session.HeldElsewhere is not null ? HeldElsewhere() : ExplainCreation(outcome);
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
            _newVaultPath = null;
            Message = string.Empty;
            KeyfilePath = RememberedKeyfile(_selectedPath);
            Raise(nameof(NewVaultName));
            IsCreating = false;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Asks for a keyfile and takes it only if it is one, saying why not at once rather than after a
    /// password has been typed (D-0284).
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
            Message = ExplainKeyfile(inspection.Outcome);
            return;
        }

        KeyfilePath = full;
        Message = inspection.IsFragile
            ? _creating
                ? "keypaste won't make a vault that needs this file: it is keyed by the file's exact bytes, so one edit to it loses the vault. Choose a KeePass keyfile."
                : FragileNotice(full)!
            : string.Empty;
    }

    /// <summary>The T-28 warning for a keyfile keyed by its hash, or null for any other.</summary>
    private static string? FragileNotice(string? keyfile) =>
        keyfile is not null && VaultKeyfile.Inspect(keyfile).IsFragile
            ? $"This vault's keyfile, {System.IO.Path.GetFileName(keyfile)}, is an ordinary file keyed by its exact bytes. " +
              "Editing it, re-saving it or letting something sync over it loses the vault for good. Change to a KeePass keyfile in Settings."
            : null;

    /// <summary>Why a file cannot be used as a keyfile, in the register of <see cref="Explain"/>.</summary>
    internal static string ExplainKeyfile(KeyfileOutcome outcome) => outcome switch
    {
        KeyfileOutcome.Missing => "That keyfile isn't there any more.",
        KeyfileOutcome.Unreadable => "That keyfile couldn't be read.",
        KeyfileOutcome.Empty => "That file is empty, so it can't be a keyfile.",
        KeyfileOutcome.IsAVault => "That's a KeePass vault, not a keyfile.",
        KeyfileOutcome.XmlUnreadable =>
            "That's a KeePass XML keyfile, and this build of keypaste can't read one: it would use the wrong key. Nothing was opened.",
        _ => "That file can't be used as a keyfile.",
    };

    private string? RememberedKeyfile(string? vaultPath) =>
        vaultPath is null
            ? null
            : _remembered.FirstOrDefault(vault => string.Equals(vault.Path, vaultPath, PathIdentity.Comparison))?.KeyfilePath;

    /// <summary>
    /// What went wrong making a vault, in the same register as <see cref="Explain"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="VaultCreationOutcome.Created"/> has no sentence because it is not a problem, and
    /// the core's own failure text is not shown: a person at this screen can act on "choose another
    /// name" and cannot act on a writer's exception message.
    /// </remarks>
    private static string ExplainCreation(VaultCreationOutcome outcome) => outcome switch
    {
        VaultCreationOutcome.PathAlreadyExists => "There's already a file there. Choose a name that isn't taken.",
        VaultCreationOutcome.EmptyPassword => "A vault needs a master password.",
        VaultCreationOutcome.PasswordsDoNotMatch => "Those two passwords aren't the same.",
        VaultCreationOutcome.KeyfileUnusable => "That keyfile can't be used. Choose it again, or another.",
        VaultCreationOutcome.KeyfileIsFragile =>
            "keypaste won't make a vault that needs this file: it is keyed by the file's exact bytes, so one edit to it loses the vault. Choose a KeePass keyfile.",
        VaultCreationOutcome.KeyfileIsThisVault => "A vault can't be its own keyfile. Choose another file.",
        _ => "That vault couldn't be created.",
    };

    /// <summary>
    /// The four things that can go wrong, in words that do not shout.
    /// </summary>
    /// <remarks>
    /// None of these is an error state in the UI sense — no red, no icon, no dialog. Every one of
    /// them is something a person does routinely, and the Ideas table in DECISIONS.md names scary warnings for normal
    /// actions as an anti-pattern.
    /// </remarks>
    private string HeldElsewhere()
    {
        var reason = _session.HeldElsewhere ?? "another keypaste process holds this vault.";
        return char.ToUpperInvariant(reason[0]) + reason[1..];
    }

    private void ShowOwner() =>
        Owner = _session.HeldBy is { } holder ? new AuthorityStatus.HeldBy(holder).Sentence : string.Empty;

    /// <summary>The lock note for <paramref name="reason"/>, or empty where the screen already says why.</summary>
    internal static string DescribeLock(VaultLockReason reason, DateTimeOffset at, TimeSpan idleTimeout)
    {
        var time = at.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);

        return reason switch
        {
            VaultLockReason.Idle => $"Locked at {time} after {Duration(idleTimeout)} without use",
            VaultLockReason.Manual => $"You locked it at {time}",
            VaultLockReason.Minimized => $"Locked at {time} when the window was minimized",
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

    private static string Explain(UnlockOutcome outcome, bool withKeyfile = false) => outcome switch
    {
        // Naming both factors when both were given, or a good password and the wrong file sends the
        // person to retype the half that was right (D-0284).
        UnlockOutcome.WrongPassword when withKeyfile => "That password and keyfile didn't open this vault.",
        UnlockOutcome.WrongPassword => "That password didn't open this vault.",
        UnlockOutcome.KeyfileUnusable => "That keyfile can't be used.",
        UnlockOutcome.NotFound => "That file isn't there any more.",
        UnlockOutcome.NotAKdbx => "That isn't a KeePass vault.",
        _ => "That vault couldn't be opened.",
    };

    private void Remember(string path, string? keyfile)
    {
        _remembered = RecentVaults.Remember(_remembered, path, DateTimeOffset.UtcNow, keyfile);
        RecentVaults.Save(KeypasteHome.RecentPath(_home), _remembered);
        Project();
    }

    private void Reload()
    {
        _remembered = RecentVaults.Load(KeypasteHome.RecentPath(_home));
        Project();

        // The most recent vault that still exists is pre-selected, so the common case is launch,
        // type, Enter — with no mouse and no arrow keys.
        SelectedPath = Recent.FirstOrDefault(item => item.Exists)?.Path;
    }

    private void Project()
    {
        Recent = [.. _remembered.Select(vault => new RecentVaultItem(vault.Path, File.Exists(vault.Path)))];
        Raise(nameof(Recent));
        Raise(nameof(HasRecent));
        Raise(nameof(HasNoRecent));
        Raise(nameof(ShowsRecent));
    }

    private void AfterTyping()
    {
        Raise(nameof(MaskedLength));
        UnlockCommand.RaiseCanExecuteChanged();

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
        Raise(nameof(MaskedLength));
        UnlockCommand.RaiseCanExecuteChanged();
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
        Raise(nameof(NewMaskedLength));
        Raise(nameof(ConfirmMaskedLength));
        CreateCommand.RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _restore?.Dispose();
        _master.Dispose();
        _new.Dispose();
        _confirm.Dispose();
    }
}
