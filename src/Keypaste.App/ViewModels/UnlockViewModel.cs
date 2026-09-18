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

    private SecretBuffer _master = new();
    private SecretBuffer _new = new();
    private SecretBuffer _confirm = new();
    private IReadOnlyList<RecentVault> _remembered = [];
    private string? _selectedPath;
    private string? _newVaultPath;
    private string _message = string.Empty;
    private bool _busy;
    private bool _creating;
    private bool _disposed;

    internal UnlockViewModel(
        AppVaultSession session,
        string? home,
        IVaultFilePicker picker,
        Action unlocked)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(picker);
        ArgumentNullException.ThrowIfNull(unlocked);

        _session = session;
        _home = home;
        _picker = picker;
        _unlocked = unlocked;

        UnlockCommand = new AsyncRelayCommand(UnlockAsync, () => CanUnlock);
        BrowseCommand = new AsyncRelayCommand(BrowseAsync, () => !_busy);
        StartCreateCommand = new AsyncRelayCommand(StartCreateAsync, () => !_busy);
        CreateCommand = new AsyncRelayCommand(CreateAsync, () => CanCreate);
        CancelCreateCommand = new AsyncRelayCommand(CancelCreateAsync, () => !_busy);
        Reload();
    }

    /// <summary>The vaults this machine has opened, most recent first.</summary>
    internal IReadOnlyList<RecentVaultItem> Recent { get; private set; } = [];

    /// <summary>Whether there is anything to show in the recent list.</summary>
    internal bool HasRecent => Recent.Count > 0;

    /// <summary>Shown when there is not.</summary>
    internal bool HasNoRecent => Recent.Count == 0;

    internal AsyncRelayCommand UnlockCommand { get; }

    /// <summary>Opens the picker for an existing vault.</summary>
    internal AsyncRelayCommand BrowseCommand { get; }

    /// <summary>Opens the save picker and, if a path comes back, shows the create fields.</summary>
    internal AsyncRelayCommand StartCreateCommand { get; }

    /// <summary>Makes the vault.</summary>
    internal AsyncRelayCommand CreateCommand { get; }

    /// <summary>Abandons the create form and forgets both passwords.</summary>
    internal AsyncRelayCommand CancelCreateCommand { get; }

    /// <summary>Whether the create fields are showing instead of the unlock ones.</summary>
    internal bool IsCreating
    {
        get => _creating;
        private set
        {
            if (Set(ref _creating, value))
            {
                Raise(nameof(IsOpening));
            }
        }
    }

    /// <summary>The other half of <see cref="IsCreating"/>, for the open controls' visibility.</summary>
    internal bool IsOpening => !_creating;

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
                Raise(nameof(SelectedName));
                Raise(nameof(HasSelection));
                UnlockCommand.RaiseCanExecuteChanged();
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
            else if (value is not null)
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

    /// <summary>Whether Argon2 is running.</summary>
    internal bool Busy
    {
        get => _busy;
        private set
        {
            if (Set(ref _busy, value))
            {
                UnlockCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private bool CanUnlock => !_busy && _selectedPath is not null && _master.Length > 0;

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
            Message = "That file isn't there any more.";
            return false;
        }

        if (!KdbxHeader.IsVaultFile(path))
        {
            Message = "That isn't a KeePass vault.";
            return false;
        }

        SelectedPath = System.IO.Path.GetFullPath(path);
        return true;
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

        try
        {
            // Argon2 is a good fraction of a second by design. Off the UI thread, or the window
            // stops painting and the app looks broken at the exact moment it is working hardest.
            var outcome = await Task.Run(() => _session.TryUnlock(path, _master.Value))
                .ConfigureAwait(true);

            if (outcome == UnlockOutcome.Opened)
            {
                Remember(path);
                ResetPassword();
                _unlocked();
                return;
            }

            Message = Explain(outcome);
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
        Raise(nameof(NewVaultName));
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
            var outcome = await Task.Run(() => _session.TryCreate(path, _new.Value, _confirm.Value))
                .ConfigureAwait(true);

            if (outcome == VaultCreationOutcome.Created)
            {
                Remember(path);
                ResetCreation();
                IsCreating = false;
                _unlocked();
                return;
            }

            Message = ExplainCreation(outcome);

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
            Raise(nameof(NewVaultName));
            IsCreating = false;
        }

        return Task.CompletedTask;
    }

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
    private static string Explain(UnlockOutcome outcome) => outcome switch
    {
        UnlockOutcome.WrongPassword => "That password didn't open this vault.",
        UnlockOutcome.NotFound => "That file isn't there any more.",
        UnlockOutcome.NotAKdbx => "That isn't a KeePass vault.",
        _ => "That vault couldn't be opened.",
    };

    private void Remember(string path)
    {
        _remembered = RecentVaults.Remember(_remembered, path, DateTimeOffset.UtcNow);
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
        _master.Dispose();
        _new.Dispose();
        _confirm.Dispose();
    }
}
