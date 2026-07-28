using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Recent;

namespace Keypaste.App.ViewModels;

/// <summary>One remembered vault, as the unlock screen shows it.</summary>
/// <remarks>
/// The list shows <see cref="Name"/> and puts <see cref="Path"/> in a tooltip. docs/IDEAS.md's screenshot
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

    private SecretBuffer _master = new();
    private IReadOnlyList<RecentVault> _remembered = [];
    private string? _selectedPath;
    private string _message = string.Empty;
    private bool _busy;
    private bool _disposed;

    internal UnlockViewModel(AppVaultSession session, string? home, Action unlocked)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(unlocked);

        _session = session;
        _home = home;
        _unlocked = unlocked;

        UnlockCommand = new AsyncRelayCommand(UnlockAsync, () => CanUnlock);
        Reload();
    }

    /// <summary>The vaults this machine has opened, most recent first.</summary>
    internal IReadOnlyList<RecentVaultItem> Recent { get; private set; } = [];

    /// <summary>Whether there is anything to show in the recent list.</summary>
    internal bool HasRecent => Recent.Count > 0;

    /// <summary>Shown when there is not.</summary>
    internal bool HasNoRecent => Recent.Count == 0;

    internal AsyncRelayCommand UnlockCommand { get; }

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
            string.Equals(item.Path, _selectedPath, StringComparison.OrdinalIgnoreCase));

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

        try
        {
            _ = KdbxHeader.Read(path);
        }
        catch (VaultException)
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

        if (_selectedPath is not null && string.Equals(_selectedPath, System.IO.Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
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
    /// The four things that can go wrong, in words that do not shout.
    /// </summary>
    /// <remarks>
    /// None of these is an error state in the UI sense — no red, no icon, no dialog. Every one of
    /// them is something a person does routinely, and docs/IDEAS.md names scary warnings for normal
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _master.Dispose();
    }
}
