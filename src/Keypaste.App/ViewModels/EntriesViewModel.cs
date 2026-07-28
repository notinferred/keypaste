using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>
/// The Entries screen: a group tree, a searchable list of names, and one entry's detail.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything here is rebuilt from the vault rather than cached across a lock.</b>
/// <see cref="ShellViewModel"/> disposes its content on every navigation and on every lock, so this
/// object's lifetime is bounded by an open vault — which is the rule 4.1 wrote down before there
/// was anything to break it.
/// </para>
/// <para>
/// <b>Searching and the group tree are built here, not in the core.</b> Core has no search API and
/// no tree API, and it should not grow either for one caller: <c>keypaste ls</c> assembles its own
/// indentation from the same flat <see cref="Vault.ReadGroupPaths"/>. What must not be duplicated
/// is a <em>rule</em> — where an entry lives, what a name may contain — and those stay in
/// <see cref="EntryNameSanitizer"/> and the core's own addressing.
/// </para>
/// </remarks>
internal sealed class EntriesViewModel : ObservableObject, IDisposable
{
    private readonly AppVaultSession _session;
    private readonly ClipboardCountdown _clipboard;

    private IReadOnlyList<EntryRow> _all = [];
    private IReadOnlyList<EntryRow> _rows = [];
    private IReadOnlyList<GroupNode> _groups = [];
    private GroupNode? _selectedGroup;
    private EntryRow? _selected;
    private EntryDetailViewModel? _detail;
    private string _search = string.Empty;
    private string? _error;
    private bool _isAdding;
    private bool _isConfirmingDelete;
    private string _newEntryPath = string.Empty;
    private bool _generatePassword = true;

    internal EntriesViewModel(AppVaultSession session, ClipboardCountdown clipboard)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clipboard);

        _session = session;
        _clipboard = clipboard;

        BeginAddCommand = new RelayCommand(BeginAdd, () => !IsAdding);
        CancelAddCommand = new RelayCommand(CancelAdd, () => IsAdding);
        ConfirmAddCommand = new RelayCommand(ConfirmAdd, () => IsAdding);
        DeleteCommand = new RelayCommand(
            () => IsConfirmingDelete = true,
            () => Selected is not null && !IsConfirmingDelete);
        ConfirmDeleteCommand = new RelayCommand(Delete, () => IsConfirmingDelete);
        CancelDeleteCommand = new RelayCommand(
            () => IsConfirmingDelete = false,
            () => IsConfirmingDelete);

        Reload();
    }

    /// <summary>The group tree, flattened, with "All entries" first.</summary>
    internal IReadOnlyList<GroupNode> Groups
    {
        get => _groups;
        private set => Set(ref _groups, value);
    }

    /// <summary>Which group is showing. Null means every entry.</summary>
    internal GroupNode? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (Set(ref _selectedGroup, value))
            {
                Filter();
            }
        }
    }

    /// <summary>What matches the search and the selected group.</summary>
    internal IReadOnlyList<EntryRow> Rows
    {
        get => _rows;
        private set => Set(ref _rows, value);
    }

    /// <summary>The search box.</summary>
    /// <remarks>
    /// Matches on title and group, case-insensitively, because those are the two things a row
    /// shows. Searching a field the list does not display would let somebody find an entry by a
    /// password they already knew, and tell them nothing they did not.
    /// </remarks>
    internal string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value))
            {
                Filter();
            }
        }
    }

    /// <summary>The selected row, or null.</summary>
    internal EntryRow? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value))
            {
                return;
            }

            Detail = value is null ? null : Build(value);
            IsConfirmingDelete = false;
            DeleteCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Whether the delete confirmation is showing.
    /// </summary>
    /// <remarks>
    /// A second click rather than a modal, and the one place in this screen where
    /// <c>KpDanger</c> appears — <c>Tokens.axaml</c> reserves it for exactly this. The confirmation
    /// exists because there is nothing to undo: core has no recycle bin, and
    /// <see cref="Vault.RemoveEntry"/> leaves a tombstone rather than a copy.
    /// </remarks>
    internal bool IsConfirmingDelete
    {
        get => _isConfirmingDelete;
        private set
        {
            if (Set(ref _isConfirmingDelete, value))
            {
                DeleteCommand.RaiseCanExecuteChanged();
                ConfirmDeleteCommand.RaiseCanExecuteChanged();
                CancelDeleteCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>What the confirmation asks, naming what goes.</summary>
    internal string DeletePrompt => Selected is { } row
        ? $"Delete {row.Path}? There is no undo."
        : string.Empty;

    /// <summary>The selected entry's fields, or null when nothing is selected.</summary>
    internal EntryDetailViewModel? Detail
    {
        get => _detail;
        private set
        {
            var previous = _detail;

            if (Set(ref _detail, value))
            {
                // Disposed on the way out, not left to a collection: it holds a username, a URL and
                // a notes field read from an open vault, and the lock has to mean something.
                previous?.Dispose();
            }
        }
    }

    /// <summary>A calm sentence when something did not work, or null.</summary>
    internal string? Error
    {
        get => _error;
        private set
        {
            if (Set(ref _error, value))
            {
                Raise(nameof(HasError));
            }
        }
    }

    internal bool HasError => _error is not null;

    /// <summary>How many entries the vault holds, for the empty state.</summary>
    internal int TotalCount => _all.Count;

    /// <summary>Whether the add form is showing.</summary>
    internal bool IsAdding
    {
        get => _isAdding;
        private set
        {
            if (Set(ref _isAdding, value))
            {
                BeginAddCommand.RaiseCanExecuteChanged();
                CancelAddCommand.RaiseCanExecuteChanged();
                ConfirmAddCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Where the new entry goes, as a path — <c>servers/production</c>.</summary>
    internal string NewEntryPath
    {
        get => _newEntryPath;
        set => Set(ref _newEntryPath, value);
    }

    /// <summary>
    /// Whether to generate the new entry's password.
    /// </summary>
    /// <remarks>
    /// The only way this screen creates a password, and on by default. Typing one into the GUI would
    /// need a field that holds a secret, and the control for that is
    /// <see cref="Controls.MaskedInput"/>, whose whole design is that it accumulates nothing —
    /// which is right for a password being checked and wrong for one being composed. Anyone who
    /// needs to type a specific password has <c>keypaste add</c>, which prompts for it without
    /// putting it in a window (docs/PRODUCT.md law 4.2).
    /// </remarks>
    internal bool GeneratePassword
    {
        get => _generatePassword;
        set => Set(ref _generatePassword, value);
    }

    internal RelayCommand BeginAddCommand { get; }

    internal RelayCommand CancelAddCommand { get; }

    internal RelayCommand ConfirmAddCommand { get; }

    /// <summary>Asks for confirmation. It does not delete.</summary>
    internal RelayCommand DeleteCommand { get; }

    /// <summary>Deletes, having been confirmed.</summary>
    internal RelayCommand ConfirmDeleteCommand { get; }

    internal RelayCommand CancelDeleteCommand { get; }

    /// <summary>Reads the vault again, keeping the selection if it survived.</summary>
    internal void Reload()
    {
        if (_session.Unlocked is not { } vault)
        {
            _all = [];
            Groups = [];
            Rows = [];
            Selected = null;
            return;
        }

        var wanted = Selected?.Path;

        _all = [.. vault.ReadEntries().Select(entry => new EntryRow(entry.Title, entry.GroupPath))];
        Groups = GroupNode.Flatten(vault.ReadGroupPaths());
        Raise(nameof(TotalCount));

        Filter();

        Selected = wanted is null
            ? null
            : Rows.FirstOrDefault(row => string.Equals(row.Path, wanted, StringComparison.Ordinal));
    }

    /// <summary>Nothing derived from the vault outlives this.</summary>
    public void Dispose()
    {
        _all = [];
        Rows = [];
        Groups = [];
        Selected = null;
        Detail = null;
    }

    private EntryDetailViewModel? Build(EntryRow row)
    {
        if (_session.Unlocked?.Find(row.Path) is not { } entry)
        {
            Error = "That entry could not be read. The vault may have locked.";
            return null;
        }

        Error = null;
        return new EntryDetailViewModel(_session, _clipboard, entry, message => Error = message);
    }

    private void Filter()
    {
        var group = SelectedGroup;
        var search = Search.Trim();

        Rows =
        [
            .. _all
                .Where(row => group is null || group.Contains(row.GroupPath))
                .Where(row => search.Length == 0
                    || row.Title.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || row.GroupPath.Contains(search, StringComparison.OrdinalIgnoreCase))
        ];
    }

    private void BeginAdd()
    {
        NewEntryPath = SelectedGroup is { IsEverything: false } group ? group.Path + "/" : string.Empty;
        IsAdding = true;
        Error = null;
    }

    private void CancelAdd()
    {
        IsAdding = false;
        NewEntryPath = string.Empty;
        Error = null;
    }

    private void ConfirmAdd()
    {
        if (_session.Unlocked is not { } vault)
        {
            Error = "The vault is locked.";
            return;
        }

        var target = NewEntryPath.Trim();
        var slash = target.LastIndexOf('/');
        var title = slash < 0 ? target : target[(slash + 1)..];
        var groupPath = slash < 0 ? string.Empty : target[..slash];

        if (title.Length == 0)
        {
            Error = "An entry needs a name.";
            return;
        }

        // The same function `keypaste add` reaches for, rather than a regular expression written
        // for this form. A validator that lives next to an error message is where a second
        // implementation of a naming rule always appears.
        var sanitized = EntryNameSanitizer.Sanitize(title);
        if (sanitized.WasAltered)
        {
            Error = $"'{title}' is not a name keypaste will create. Try '{sanitized.Text}'.";
            return;
        }

        var path = groupPath.Length == 0 ? title : groupPath + "/" + title;

        if (vault.Find(path) is not null)
        {
            Error = $"'{path}' already exists.";
            return;
        }

        var password = string.Empty;

        try
        {
            if (GeneratePassword)
            {
                using var buffer = new SecretBuffer();
                PasswordGenerator.Append(PasswordRecipe.Default, buffer);
                password = new string(buffer.Value);
            }

            vault.AddEntry(new VaultEntry
            {
                Title = sanitized.Text,
                Password = password,
                GroupPath = EntryNameSanitizer.SanitizePath(groupPath).Text,
            });

            vault.Save();
        }
        catch (VaultChangedOnDiskException)
        {
            Error = "Something else changed this vault since you opened it. Lock and unlock to see it, then add this again.";
            return;
        }
        catch (VaultException e)
        {
            Error = e.Message;
            return;
        }

        IsAdding = false;
        NewEntryPath = string.Empty;
        Error = null;

        Reload();
        Selected = Rows.FirstOrDefault(row => string.Equals(row.Path, path, StringComparison.Ordinal));
    }

    private void Delete()
    {
        if (Selected is not { } row)
        {
            return;
        }

        if (_session.Unlocked is not { } vault)
        {
            Error = "The vault is locked.";
            return;
        }

        try
        {
            // No recycle bin, because core has none: RemoveEntry writes a tombstone and the value
            // is gone. The confirmation is the view's job, and it is the one place KpDanger appears.
            vault.RemoveEntry(row.Path);
            vault.Save();
        }
        catch (VaultChangedOnDiskException)
        {
            Error = "Something else changed this vault since you opened it. Lock and unlock to see it, then delete this again.";
            return;
        }
        catch (VaultException e)
        {
            Error = e.Message;
            return;
        }

        IsConfirmingDelete = false;
        Selected = null;
        Reload();
    }
}
