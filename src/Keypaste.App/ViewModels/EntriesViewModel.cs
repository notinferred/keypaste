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
    private string? _notice;
    private RecycledEntryId? _undo;
    private string _newEntryPath = string.Empty;
    private bool _generatePassword = true;

    internal EntriesViewModel(AppVaultSession session, ClipboardCountdown clipboard)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clipboard);

        _session = session;
        _clipboard = clipboard;
        NewPassword = new SecretField(clipboard);

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
        UndoDeleteCommand = new RelayCommand(UndoDelete, () => _undo is not null);

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
    /// <c>KpDanger</c> appears — <c>Tokens.axaml</c> reserves it for exactly this. In a vault with
    /// a recycle bin the entry can be recovered; in one whose owner turned the bin off it cannot,
    /// and <see cref="DeletePrompt"/> says which vault this is rather than guessing.
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

    /// <summary>What the confirmation asks, naming what goes and whether it can come back.</summary>
    /// <remarks>
    /// Read from the vault, because the answer is the vault's: KeePassXC writes the recycle-bin
    /// setting and a person can turn it off there. <see cref="Notice"/> says what the delete
    /// actually did, which is the answer that cannot go stale between the question and the act.
    /// </remarks>
    internal string DeletePrompt
    {
        get
        {
            if (Selected is not { } row)
            {
                return string.Empty;
            }

            var name = EntryNameSanitizer.SanitizePath(row.Path).Text;

            return _session.Unlocked?.RecyclesDeletedEntries == true
                ? $"Delete {name}? It goes to the vault's recycle bin."
                : $"Delete {name}? There is no undo.";
        }
    }

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

    /// <summary>What the last deletion did, or null.</summary>
    /// <remarks>
    /// Reported after the act, from the outcome core returned, so a vault whose recycle bin was
    /// switched off between the confirmation and the deletion cannot make this line wrong. The CLI
    /// says the same thing for the same reason.
    /// </remarks>
    internal string? Notice
    {
        get => _notice;
        private set
        {
            if (Set(ref _notice, value))
            {
                Raise(nameof(HasNotice));
            }
        }
    }

    internal bool HasNotice => _notice is not null;

    /// <summary>Whether the last deletion can still be taken back from here.</summary>
    internal bool CanUndoDelete => _undo is not null;

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
    /// Whether to generate the new entry's password, rather than take one already in hand.
    /// </summary>
    /// <remarks>
    /// On by default, because a generated password is the better answer whenever there is a choice.
    /// Turning it off shows <see cref="NewPassword"/>, which is how somebody stores the API key
    /// they were handed rather than one keypaste invented (docs/PRODUCT.md §1.1).
    /// </remarks>
    internal bool GeneratePassword
    {
        get => _generatePassword;
        set
        {
            if (Set(ref _generatePassword, value) && value)
            {
                // Turning generation back on hides the field. Anything typed into it would
                // otherwise sit in the buffer, invisible, until something else read it.
                NewPassword.Clear();
            }
        }
    }

    /// <summary>The password being entered for the new entry, when it is not being generated.</summary>
    internal SecretField NewPassword { get; }

    /// <summary>What to generate, while <see cref="GeneratePassword"/> is on.</summary>
    internal GeneratorViewModel Generator { get; } = new();

    internal RelayCommand BeginAddCommand { get; }

    internal RelayCommand CancelAddCommand { get; }

    internal RelayCommand ConfirmAddCommand { get; }

    /// <summary>Asks for confirmation. It does not delete.</summary>
    internal RelayCommand DeleteCommand { get; }

    /// <summary>Deletes, having been confirmed.</summary>
    internal RelayCommand ConfirmDeleteCommand { get; }

    internal RelayCommand CancelDeleteCommand { get; }

    /// <summary>Puts back the entry the last deletion recycled.</summary>
    /// <remarks>
    /// Recovery beside the action it reverses, for the one deletion a person is still looking at.
    /// The Trash screen is where every other recovery happens, including this one after a
    /// navigation or a lock.
    /// </remarks>
    internal RelayCommand UndoDeleteCommand { get; }

    /// <summary>Reads the vault again, keeping the selection if it survived.</summary>
    internal void Reload()
    {
        if (_session.Unlocked is not { } vault)
        {
            _all = [];
            Groups = [];
            Rows = [];
            Selected = null;

            // A half-entered password is as much a secret as a stored one, and the screen is about
            // to be disposed anyway. Clearing here means the lock holds on whichever path runs.
            NewPassword.Clear();
            IsAdding = false;
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
        Offer(null, null);
        NewPassword.Dispose();
    }

    private EntryDetailViewModel? Build(EntryRow row)
    {
        VaultEntry? found;
        try
        {
            // By name, not by path: two rows can share a path, and building the pane from the
            // wrong one is what made its copy button serve a secret nobody selected.
            found = _session.Unlocked?.Find(row.Name);
        }
        catch (VaultException e)
        {
            Error = e.Message;
            return null;
        }

        if (found is not { } entry)
        {
            Error = "That entry could not be read. The vault may have locked.";
            return null;
        }

        Error = null;
        return new EntryDetailViewModel(_session, _clipboard, entry, message => Error = message, Reselect);
    }

    /// <summary>Reads the list again and lands on the entry a restore left behind.</summary>
    /// <remarks>
    /// <see cref="Reload"/> alone is not enough: it keeps the selection by path, and a restored
    /// revision from before a rename gives the entry a different one. It is also not too much: an
    /// <see cref="EntryRow"/> is a record, so reselecting an equal row changes nothing.
    /// </remarks>
    private void Reselect(EntryName name)
    {
        Reload();
        Selected = Rows.FirstOrDefault(row => row.Name == name);
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
        Offer(null, null);
        NewEntryPath = SelectedGroup is { IsEverything: false } group ? group.Path + "/" : string.Empty;
        NewPassword.Clear();
        IsAdding = true;
        Error = null;
    }

    private void CancelAdd()
    {
        IsAdding = false;
        NewEntryPath = string.Empty;
        NewPassword.Clear();
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
            // The rejected name is not echoed: it was rejected precisely because it does not
            // render as what it is, so quoting it back would put the trickery on screen and read
            // as identical to the suggestion beside it.
            Error = $"That is not a name keypaste will create. Try '{sanitized.Text}'.";
            return;
        }

        var name = new EntryName(groupPath, title);
        var path = groupPath.Length == 0 ? title : groupPath + "/" + title;

        try
        {
            // The identity, so this cannot claim an entry exists that nothing has.
            if (vault.Find(name) is not null)
            {
                Error = $"'{EntryNameSanitizer.SanitizePath(path).Text}' already exists.";
                return;
            }

            // The joined form, reached for its refusal alone: it throws when the path already
            // names two, and a third would deepen a collision the detail pane then has to refuse.
            _ = vault.Find(path);
        }
        catch (VaultException e)
        {
            Error = e.Message;
            return;
        }

        var password = string.Empty;

        try
        {
            if (GeneratePassword)
            {
                if (Generator.Recipe is not { } recipe)
                {
                    Error = Generator.Error;
                    return;
                }

                using var buffer = new SecretBuffer();
                PasswordGenerator.Append(recipe, buffer);
                password = new string(buffer.Value);
            }
            else
            {
                // Empty is still allowed, and the field shows zero dots beside the unticked box:
                // KDBX permits an entry with no password, and 4.2 created them that way.
                password = NewPassword.Compose();
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
        NewPassword.Clear();
        Error = null;

        Reload();
        Selected = Rows.FirstOrDefault(row => row.Name == name);
    }

    private void Delete()
    {
        Offer(null, null);

        if (Selected is not { } row)
        {
            return;
        }

        if (_session.Unlocked is not { } vault)
        {
            Error = "The vault is locked.";
            return;
        }

        DeletionOutcome outcome;
        RecycledEntryId recycled;
        var name = EntryNameSanitizer.SanitizePath(row.Path).Text;

        try
        {
            // Reversible where the vault has a recycle bin: the entry keeps its identity, fields
            // and history, and Trash puts it back. The confirmation is the view's job, and it is
            // the one place KpDanger appears. The row is addressed by its name rather than its
            // path: two entries can share a path.
            outcome = vault.RemoveEntry(row.Name, out recycled);

            if (outcome == DeletionOutcome.NothingMatched)
            {
                Error = "That entry is not in this vault any more.";
                IsConfirmingDelete = false;
                Reload();
                return;
            }

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

        // Last, because Reload and the selection it clears both drop a pending offer.
        Offer(
            outcome == DeletionOutcome.Recycled ? recycled : null,
            outcome == DeletionOutcome.Recycled
                ? $"Moved {name} to the trash."
                : $"Deleted {name}. This vault has no recycle bin, so nothing can put it back.");
    }

    /// <summary>Restores the entry the last deletion recycled, by the identity core returned.</summary>
    private void UndoDelete()
    {
        if (_undo is not { } id)
        {
            return;
        }

        if (_session.Unlocked is not { } vault)
        {
            Error = "The vault is locked.";
            return;
        }

        RestoreOutcome outcome;

        try
        {
            outcome = vault.RestoreRecycled(id);

            if (outcome is RestoreOutcome.Restored or RestoreOutcome.RestoredToRoot)
            {
                vault.Save();
            }
        }
        catch (VaultChangedOnDiskException)
        {
            Error = "Something else changed this vault since you opened it. Lock and unlock to see it, then restore this from Trash.";
            return;
        }
        catch (VaultException e)
        {
            Error = e.Message;
            return;
        }

        Offer(null, null);

        if (outcome is RestoreOutcome.Restored or RestoreOutcome.RestoredToRoot)
        {
            Reload();
            Error = null;
            return;
        }

        // Every refusal reads the same way from here: the entry is still in the bin, and Trash is
        // the screen that says which refusal it was and what to do about it.
        Error = "That entry could not be put back. Open Trash to see why.";
    }

    /// <summary>Holds, or drops, the offer to undo the last deletion.</summary>
    private void Offer(RecycledEntryId? undo, string? notice)
    {
        _undo = undo;
        Notice = notice;
        Raise(nameof(CanUndoDelete));
        UndoDeleteCommand.RaiseCanExecuteChanged();
    }
}
