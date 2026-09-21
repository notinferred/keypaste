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
    private EntryName? _selection;
    private EntryName? _pinned;
    private EntryDetailViewModel? _detail;
    private string _search = string.Empty;
    private string? _matchedQuery;
    private Dictionary<EntryName, MatchedFields> _matches = [];
    private string? _error;
    private bool _isAdding;
    private bool _isConfirmingDelete;
    private string? _notice;
    private RecycledEntryId? _undo;
    private string _newEntryPath = string.Empty;
    private bool _generatePassword = true;
    private bool _isOrganizing;
    private bool _isCreatingGroup;
    private bool _isRenamingGroup;
    private string _draftTitle = string.Empty;
    private string _draftGroupName = string.Empty;
    private GroupNode? _moveTarget;
    private IReadOnlyList<GroupNode> _moveTargets = [];

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

        OrganizeCommand = new RelayCommand(BeginOrganize, () => Selected is not null && !IsOrganizing);
        ConfirmOrganizeCommand = new RelayCommand(ConfirmOrganize, () => IsOrganizing);
        CancelOrganizeCommand = new RelayCommand(() => CloseOrganize(), () => IsOrganizing);

        BeginCreateGroupCommand = new RelayCommand(BeginCreateGroup, () => !IsCreatingGroup);
        ConfirmCreateGroupCommand = new RelayCommand(ConfirmCreateGroup, () => IsCreatingGroup);
        BeginRenameGroupCommand = new RelayCommand(
            BeginRenameGroup,
            () => !IsRenamingGroup && SelectedGroup is { IsEverything: false });
        ConfirmRenameGroupCommand = new RelayCommand(ConfirmRenameGroup, () => IsRenamingGroup);
        CancelGroupCommand = new RelayCommand(CloseGroupForms, () => IsCreatingGroup || IsRenamingGroup);

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
                CloseGroupForms();
                BeginRenameGroupCommand.RaiseCanExecuteChanged();
                Raise(nameof(CreateGroupPrompt));
                Raise(nameof(RenameGroupPrompt));
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
    /// <para>
    /// <b>The matching happens in <see cref="Vault.Search"/>, not here, and that is a security
    /// decision rather than a tidiness one.</b> A query covers titles, group paths, usernames and
    /// URLs; the last two are not on a row, so matching them in this class would mean holding every
    /// username in the vault in order to compare it — exactly what <see cref="EntryRow"/> refuses to
    /// carry and what the hygiene gate checks for. Core returns names and a
    /// <see cref="MatchedFields"/>, so no value reaches this layer at all.
    /// </para>
    /// <para>
    /// It follows that a password and a note cannot be searched: core never reads them. That is the
    /// same conclusion this remark used to reach from the opposite direction, and the reasoning it
    /// used — do not match what the row does not show — was what made a username unsearchable too.
    /// </para>
    /// </remarks>
    internal string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value))
            {
                // A new query is a new question, so an entry held in the list by the last one stops
                // being held by this one.
                _pinned = null;
                Filter();
            }
        }
    }

    /// <summary>The selected row, or null.</summary>
    /// <remarks>
    /// <para>
    /// <b>Held as the entry's name rather than as the row object, and that is what makes the
    /// selection survive a search.</b> The list writes null back into this property whenever the row
    /// it had selected leaves <see cref="Rows"/> — which is every keystroke that narrows a result —
    /// so a selection stored as a row was cleared by filtering, taking the detail pane with it. A
    /// null from the list is therefore ignored; <see cref="Select"/> is how the selection is
    /// deliberately dropped, and <see cref="Dispose"/>, the locked branch of <see cref="Reload"/>
    /// and a completed delete are the three callers that do it.
    /// </para>
    /// <para>
    /// The getter looks the row up in the current list, so an equal row selected again is the same
    /// value and the list highlights it the moment it comes back.
    /// </para>
    /// </remarks>
    internal EntryRow? Selected
    {
        get => _selection is { } name
            ? Rows.FirstOrDefault(row => row.Name == name)
            : null;
        set
        {
            if (value is { } row)
            {
                Select(row.Name);
            }
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

    /// <summary>Whether the organize form is showing for the selected entry.</summary>
    internal bool IsOrganizing
    {
        get => _isOrganizing;
        private set
        {
            if (Set(ref _isOrganizing, value))
            {
                OrganizeCommand.RaiseCanExecuteChanged();
                ConfirmOrganizeCommand.RaiseCanExecuteChanged();
                CancelOrganizeCommand.RaiseCanExecuteChanged();
                Raise(nameof(OrganizePrompt));
            }
        }
    }

    /// <summary>Whether the new-group form is showing.</summary>
    internal bool IsCreatingGroup
    {
        get => _isCreatingGroup;
        private set
        {
            if (Set(ref _isCreatingGroup, value))
            {
                BeginCreateGroupCommand.RaiseCanExecuteChanged();
                ConfirmCreateGroupCommand.RaiseCanExecuteChanged();
                CancelGroupCommand.RaiseCanExecuteChanged();
                Raise(nameof(CreateGroupPrompt));
            }
        }
    }

    /// <summary>Whether the rename-group form is showing.</summary>
    internal bool IsRenamingGroup
    {
        get => _isRenamingGroup;
        private set
        {
            if (Set(ref _isRenamingGroup, value))
            {
                BeginRenameGroupCommand.RaiseCanExecuteChanged();
                ConfirmRenameGroupCommand.RaiseCanExecuteChanged();
                CancelGroupCommand.RaiseCanExecuteChanged();
                Raise(nameof(RenameGroupPrompt));
                Raise(nameof(ShowsProjectRenameNote));
                Raise(nameof(ProjectRenameNote));
            }
        }
    }

    /// <summary>The title being typed for the selected entry.</summary>
    internal string DraftTitle
    {
        get => _draftTitle;
        set => Set(ref _draftTitle, value);
    }

    /// <summary>The name being typed for a group.</summary>
    internal string DraftGroupName
    {
        get => _draftGroupName;
        set => Set(ref _draftGroupName, value);
    }

    /// <summary>The groups an entry can be moved into.</summary>
    /// <remarks>
    /// The tree's own list, which comes from <see cref="Vault.ReadGroupPaths"/>: a group holding
    /// nothing is in it and the recycle bin is not, so every offered destination is one core will
    /// accept and a move is not a delete. Offering a typed path instead would make
    /// <see cref="OrganizeOutcome.DestinationMissing"/> the ordinary answer to a typo, where the
    /// add form would have created the group.
    /// </remarks>
    internal IReadOnlyList<GroupNode> MoveTargets
    {
        get => _moveTargets;
        private set => Set(ref _moveTargets, value);
    }

    /// <summary>The group the entry would move into.</summary>
    internal GroupNode? MoveTarget
    {
        get => _moveTarget;
        set => Set(ref _moveTarget, value);
    }

    /// <summary>What the organize form is looking at.</summary>
    internal string OrganizePrompt =>
        Selected is { } row ? EntryNameSanitizer.SanitizePath(row.Path).Text : string.Empty;

    /// <summary>Where a new group would go.</summary>
    internal string CreateGroupPrompt =>
        SelectedGroup is { IsEverything: false } group
            ? $"A new group inside {EntryNameSanitizer.SanitizePath(group.Path).Text}."
            : "A new group at the top level.";

    /// <summary>Which group is being renamed.</summary>
    internal string RenameGroupPrompt =>
        SelectedGroup is { IsEverything: false } group
            ? $"Rename {EntryNameSanitizer.SanitizePath(group.Path).Text}."
            : string.Empty;

    /// <summary>
    /// What organizing can do to an authorization, said wherever organizing happens.
    /// </summary>
    /// <remarks>
    /// A policy rule and an agent exposure are globs matched against an entry's group path and its
    /// title (<see cref="EntryExposure"/>, D-0021), so they follow neither the group nor the entry:
    /// renaming away from a granted path stops the rule matching and renaming onto one starts it.
    /// THREATS.md T-13 records that keypaste does not prevent this, and D-0275 records why there is
    /// no check in the write path. Neither is a reason to let somebody do it without being told, so
    /// this is a line on the form and never a refusal.
    /// </remarks>
    /// <remarks>
    /// An instance property over a static one, because a binding needs one — the same trade
    /// <see cref="ShellViewModel.Destinations_"/> makes, with the same suppression.
    /// </remarks>
#pragma warning disable CA1822
    internal string AccessNote => AccessNoteText;
#pragma warning restore CA1822

    /// <summary>The wording of <see cref="AccessNote"/>, for a test that has no screen.</summary>
    internal const string AccessNoteText =
        "Policy rules and agent exposures match paths, so this can stop one applying to these " +
        "entries and start another.";

    /// <summary>Whether the group being renamed is an env project.</summary>
    internal bool ShowsProjectRenameNote =>
        IsRenamingGroup && SelectedGroup is { IsEverything: false } group && IsProject(group.Path);

    /// <summary>What renaming this particular group also renames.</summary>
    internal string ProjectRenameNote
    {
        get
        {
            if (!ShowsProjectRenameNote || SelectedGroup is not { } group)
            {
                return string.Empty;
            }

            var project = EntryNameSanitizer.Sanitize(group.Name).Text;

            return $"This is a project. `keypaste run {project}` will stop finding it, and any rule "
                + $"or exposure written for {EnvConvention.RootGroup}/{project} stops matching "
                + "these entries while one written for the new name starts matching them.";
        }
    }

    /// <summary>Opens the organize form for the selected entry.</summary>
    internal RelayCommand OrganizeCommand { get; }

    /// <summary>Renames and moves in the one write core offers for both.</summary>
    internal RelayCommand ConfirmOrganizeCommand { get; }

    internal RelayCommand CancelOrganizeCommand { get; }

    internal RelayCommand BeginCreateGroupCommand { get; }

    internal RelayCommand ConfirmCreateGroupCommand { get; }

    internal RelayCommand BeginRenameGroupCommand { get; }

    internal RelayCommand ConfirmRenameGroupCommand { get; }

    internal RelayCommand CancelGroupCommand { get; }

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
            Select(null);
            Forget();

            // A half-entered password is as much a secret as a stored one, and the screen is about
            // to be disposed anyway. Clearing here means the lock holds on whichever path runs.
            NewPassword.Clear();
            IsAdding = false;
            return;
        }

        var wanted = _selection;

        var wantedGroup = _selectedGroup?.Path;

        _all = [.. vault.ReadEntries().Select(entry => new EntryRow(entry.Title, entry.GroupPath))];
        Groups = GroupNode.Flatten(vault.ReadGroupPaths());
        MoveTargets = Groups;
        Raise(nameof(TotalCount));

        // The tree is rebuilt from the vault, so the node the sidebar had is not in the new list.
        // Re-pointing at the one with the same path keeps the filter and the highlight where they
        // were; a group that is gone leaves the selection at everything. Assigned to the field
        // rather than the property, because Filter runs below either way.
        _selectedGroup = wantedGroup is null
            ? null
            : Groups.FirstOrDefault(node => string.Equals(node.Path, wantedGroup, StringComparison.Ordinal));

        Raise(nameof(SelectedGroup));

        // The vault changed underneath, so an answer about the old one is not an answer about this.
        _matchedQuery = null;

        Filter();

        Select(wanted is not null && _all.Any(row => row.Name == wanted) ? wanted : null);
    }

    /// <summary>
    /// Drops the query, the result and everything typed into a form.
    /// </summary>
    /// <remarks>
    /// A query is what somebody was looking for in this vault, and a result is a list of its entry
    /// names; neither belongs to a locked app. Called from the locked branch above and from
    /// <see cref="Dispose"/>, because the lock has to hold on whichever path runs.
    /// </remarks>
    private void Forget()
    {
        _search = string.Empty;
        _matchedQuery = string.Empty;
        _matches = [];
        _pinned = null;
        _draftTitle = string.Empty;
        _draftGroupName = string.Empty;
        _moveTarget = null;
        MoveTargets = [];
        IsOrganizing = false;
        IsCreatingGroup = false;
        IsRenamingGroup = false;

        Raise(nameof(Search));
        Raise(nameof(DraftTitle));
        Raise(nameof(DraftGroupName));
        Raise(nameof(MoveTarget));
    }

    /// <summary>Nothing derived from the vault outlives this.</summary>
    public void Dispose()
    {
        _all = [];
        Rows = [];
        Groups = [];
        Select(null);
        Forget();
        Detail = null;
        Offer(null, null);
        NewPassword.Dispose();
    }

    private EntryDetailViewModel? Build(EntryName name)
    {
        VaultEntry? found;
        try
        {
            // By name, not by path: two rows can share a path, and building the pane from the
            // wrong one is what made its copy button serve a secret nobody selected.
            found = _session.Unlocked?.Find(name);
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
        _selection = name;
        Reload();
    }

    /// <summary>Narrows the list to the selected group and the current query.</summary>
    /// <remarks>
    /// Runs on every group change and every reselect as well as every keystroke, so the core search
    /// is cached against the query it answered: only a changed query costs a read of the vault.
    /// </remarks>
    private void Filter()
    {
        var group = SelectedGroup;
        var query = Search.Trim();

        Match(query);

        List<EntryRow> rows =
        [
            .. _all
                .Where(row => group is null || group.Contains(row.GroupPath))
                .Where(row => query.Length == 0 || _matches.ContainsKey(row.Name))
                .Select(row => query.Length == 0
                    ? row
                    : row with { Fields = _matches[row.Name] })
        ];

        // An entry just renamed out of its own result stays where the person is looking at it. It is
        // held only until the query changes, which is when they asked a different question.
        if (_pinned is { } pin && !rows.Any(row => row.Name == pin))
        {
            if (_all.FirstOrDefault(row => row.Name == pin) is { } held)
            {
                rows.Insert(0, held);
            }
        }

        Rows = rows;
    }

    /// <summary>Asks core which entries the query is in, unless it already answered this one.</summary>
    private void Match(string query)
    {
        if (query.Length == 0)
        {
            _matchedQuery = string.Empty;
            _matches = [];
            return;
        }

        if (string.Equals(_matchedQuery, query, StringComparison.Ordinal))
        {
            return;
        }

        if (_session.Unlocked is not { } vault)
        {
            _matchedQuery = null;
            _matches = [];
            return;
        }

        // Last write wins where a vault holds two entries of one name: the list draws both rows and
        // either one being in the result is what puts the pair on screen.
        Dictionary<EntryName, MatchedFields> found = [];

        foreach (var match in vault.Search(query))
        {
            found[match.Name] = match.Fields;
        }

        _matchedQuery = query;
        _matches = found;
    }

    /// <summary>Moves the selection, or drops it, and rebuilds the pane when it moved.</summary>
    /// <remarks>
    /// <b>Only a changed identity rebuilds the pane.</b> A reload that lands on the same entry —
    /// which is every reload after an edit, a restore or an organize elsewhere — must leave the
    /// pane object alone: it keeps its own values current, and its history section is open because
    /// somebody opened it. Replacing it would dispose the pane in the middle of the interaction
    /// that asked for the reload.
    /// </remarks>
    private void Select(EntryName? name)
    {
        var moved = _selection != name;
        _selection = name;

        if (moved)
        {
            Detail = name is null ? null : Build(name);
        }

        IsConfirmingDelete = false;
        IsOrganizing = false;

        Raise(nameof(Selected));
        DeleteCommand.RaiseCanExecuteChanged();
        OrganizeCommand.RaiseCanExecuteChanged();
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
        Select(null);
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

    private void BeginOrganize()
    {
        if (Selected is not { } row)
        {
            return;
        }

        CloseOtherForms();
        DraftTitle = row.Title;
        MoveTarget = MoveTargets.FirstOrDefault(
            node => string.Equals(node.Path, row.GroupPath, StringComparison.Ordinal));
        IsOrganizing = true;
        Error = null;
    }

    private void CloseOrganize()
    {
        IsOrganizing = false;
        DraftTitle = string.Empty;
        MoveTarget = null;
        Error = null;
    }

    /// <summary>
    /// Renames and moves the selected entry, in the one write core offers for both.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One core call, never two.</b> <see cref="Vault.Relocate"/> validates the whole target
    /// before it mutates anything, so a refusal leaves the open vault exactly as it was. Calling
    /// <see cref="Vault.RenameEntry"/> and then <see cref="Vault.MoveEntry"/> would not: the first
    /// can succeed and the second be refused, and the rename nobody was told about would then be
    /// written by the next unrelated <see cref="Vault.Save"/> — an add, a delete, an edit on
    /// another screen. The form has one Save for that reason.
    /// </para>
    /// <para>
    /// The title goes through <see cref="EntryNameSanitizer"/> first, as the add form does, so a
    /// name that does not render as what it is never reaches the vault. The rejected text is not
    /// echoed back, for the reason given there.
    /// </para>
    /// </remarks>
    private void ConfirmOrganize()
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

        var title = DraftTitle.Trim();

        if (title.Length == 0)
        {
            Error = "An entry needs a name.";
            return;
        }

        var sanitized = EntryNameSanitizer.Sanitize(title);
        if (sanitized.WasAltered)
        {
            Error = $"That is not a name keypaste will create. Try '{sanitized.Text}'.";
            return;
        }

        var target = new EntryName(MoveTarget?.Path ?? string.Empty, sanitized.Text);

        OrganizeOutcome outcome;
        EntryName? result;

        try
        {
            outcome = vault.Relocate(row.Name, target, out result);

            if (outcome is not (OrganizeOutcome.Renamed
                or OrganizeOutcome.Moved
                or OrganizeOutcome.RenamedAndMoved))
            {
                Error = Refusal(outcome, target);
                return;
            }

            vault.Save();
        }
        catch (VaultChangedOnDiskException)
        {
            Error = "Something else changed this vault since you opened it. Lock and unlock to see it, then make your change again.";
            return;
        }
        catch (VaultException e)
        {
            Error = e.Message;
            return;
        }

        var moved = result!;

        CloseOrganize();
        Offer(null, Did(outcome, moved));

        // Held in the result even when its new name no longer answers the query that found it, so
        // the entry does not vanish from under somebody at the moment they renamed it.
        _pinned = moved;
        Reselect(moved);

        // A filter the entry has just left would hide what somebody is still looking at, so the
        // filter follows the entry rather than the other way round.
        if (SelectedGroup is { IsEverything: false } group && !group.Contains(moved.GroupPath))
        {
            SelectedGroup = Groups.FirstOrDefault(
                node => string.Equals(node.Path, moved.GroupPath, StringComparison.Ordinal))
                ?? Groups.FirstOrDefault(node => node.IsEverything);
        }
    }

    private void BeginCreateGroup()
    {
        CloseOtherForms();
        IsRenamingGroup = false;
        DraftGroupName = string.Empty;
        IsCreatingGroup = true;
        Error = null;
    }

    private void BeginRenameGroup()
    {
        if (SelectedGroup is not { IsEverything: false } group)
        {
            return;
        }

        CloseOtherForms();
        IsCreatingGroup = false;
        DraftGroupName = group.Name;
        IsRenamingGroup = true;
        Error = null;
    }

    private void CloseGroupForms()
    {
        IsCreatingGroup = false;
        IsRenamingGroup = false;
        DraftGroupName = string.Empty;
    }

    /// <summary>Closes whatever else is open, so one form is on screen at a time.</summary>
    private void CloseOtherForms()
    {
        Offer(null, null);
        IsAdding = false;
        IsConfirmingDelete = false;
        IsOrganizing = false;
    }

    private void ConfirmCreateGroup()
    {
        var parent = SelectedGroup is { IsEverything: false } group ? group.Path : string.Empty;

        Organize(
            (vault, name) =>
            {
                var outcome = vault.CreateGroup(parent, name, out var created);
                return (outcome, created);
            },
            GroupOutcome.Created,
            path => $"Created {EntryNameSanitizer.SanitizePath(path).Text}.",
            follow: false);
    }

    private void ConfirmRenameGroup()
    {
        if (SelectedGroup is not { IsEverything: false } selected)
        {
            return;
        }

        var path = selected.Path;

        Organize(
            (vault, name) =>
            {
                var outcome = vault.RenameGroup(path, name, out var renamed);
                return (outcome, renamed);
            },
            GroupOutcome.Renamed,
            renamed => $"Renamed to {EntryNameSanitizer.SanitizePath(renamed).Text}.",
            follow: true);
    }

    /// <summary>The half of a group operation that is the same for creating and renaming.</summary>
    private void Organize(
        Func<Vault, string, (GroupOutcome Outcome, string Path)> operation,
        GroupOutcome success,
        Func<string, string> said,
        bool follow)
    {
        if (_session.Unlocked is not { } vault)
        {
            Error = "The vault is locked.";
            return;
        }

        var name = DraftGroupName.Trim();

        if (name.Length == 0)
        {
            Error = "A group needs a name.";
            return;
        }

        var sanitized = EntryNameSanitizer.Sanitize(name);
        if (sanitized.WasAltered)
        {
            Error = $"That is not a name keypaste will create. Try '{sanitized.Text}'.";
            return;
        }

        string path;

        try
        {
            var (outcome, produced) = operation(vault, sanitized.Text);

            if (outcome != success)
            {
                Error = Refusal(outcome);
                return;
            }

            path = produced;
            vault.Save();
        }
        catch (VaultChangedOnDiskException)
        {
            Error = "Something else changed this vault since you opened it. Lock and unlock to see it, then make your change again.";
            return;
        }
        catch (VaultException e)
        {
            Error = e.Message;
            return;
        }

        CloseGroupForms();
        Reload();

        // A rename has to be followed: the sidebar was pointing at a path that no longer exists, and
        // Reload cannot re-point at it. Creating one must not be — somebody who makes a group in
        // order to move things into it would be dropped inside it, with an empty list and nothing
        // left to select.
        if (follow)
        {
            SelectedGroup = Groups.FirstOrDefault(
                node => string.Equals(node.Path, path, StringComparison.Ordinal)) ?? SelectedGroup;
        }

        Offer(null, said(path));
    }

    /// <summary>What the app says happened, for a write that went through.</summary>
    private static string Did(OrganizeOutcome outcome, EntryName moved)
    {
        var where = moved.GroupPath.Length == 0
            ? "the top level"
            : EntryNameSanitizer.SanitizePath(moved.GroupPath).Text;

        var title = EntryNameSanitizer.Sanitize(moved.Title).Text;

        return outcome switch
        {
            OrganizeOutcome.Renamed => $"Renamed to {title}.",
            OrganizeOutcome.Moved => $"Moved {title} to {where}.",
            _ => $"Moved {title} to {where} and renamed it.",
        };
    }

    /// <summary>
    /// What the app says when core refused an entry a new name or a new group.
    /// </summary>
    /// <remarks>
    /// The rejected name is never echoed. Where core refused on a naming rule, the rule is asked
    /// for its own reason rather than a second copy of it being written here, which is the move
    /// <see cref="ConfirmAdd"/> makes with the sanitizer.
    /// </remarks>
    private static string Refusal(OrganizeOutcome outcome, EntryName target)
    {
        switch (outcome)
        {
            case OrganizeOutcome.NothingMatched:
                return "That entry is not in this vault any more.";

            case OrganizeOutcome.DestinationMissing:
                return "That group is not in this vault any more.";

            case OrganizeOutcome.DestinationUnchanged:
                return "That is already its name and its group.";

            case OrganizeOutcome.DestinationOccupied:
                return "That group already has an entry with this name.";

            case OrganizeOutcome.DestinationAmbiguous:
                return "That would leave two entries answering to one path, and keypaste could not tell them apart afterwards.";

            case OrganizeOutcome.NameRefused:
                VaultNameRules.IsValidTitle(target.Title, out var why);
                return $"keypaste will not use that title: {why}.";

            case OrganizeOutcome.EnvNameRefused
                when string.Equals(target.GroupPath, EnvConvention.RootGroup, StringComparison.Ordinal):
                return "A variable belongs to a project, so pick a group inside env.";

            case OrganizeOutcome.EnvNameRefused:
                return "Under env a name has to be one an environment could export: capitals, digits and underscores, not starting with a digit.";

            case OrganizeOutcome.EnvNameCollides:
                return "That project already has a variable whose name differs from this one only in case. Linux would see two and Windows one.";

            default:
                return "That change could not be made.";
        }
    }

    /// <summary>What the app says when core refused a group a name.</summary>
    private string Refusal(GroupOutcome outcome)
    {
        switch (outcome)
        {
            case GroupOutcome.NothingMatched:
            case GroupOutcome.ParentMissing:
                return "That group is not in this vault any more.";

            case GroupOutcome.DestinationUnchanged:
                return "That is already its name.";

            case GroupOutcome.DestinationOccupied:
                return "Something in there already answers to that name. Nothing was changed.";

            case GroupOutcome.DestinationAmbiguous:
                return "That rename would leave two entries answering to one path, and keypaste could not tell them apart afterwards.";

            case GroupOutcome.NameRefused:
                VaultNameRules.IsValidGroupName(DraftGroupName.Trim(), out var why);
                return $"keypaste will not use that group name: {why}.";

            case GroupOutcome.NameReserved:
                return "env at the top level and the recycle bin are names keypaste assigns. A group cannot be created as one, renamed to one, or renamed away from one.";

            case GroupOutcome.EnvNameRefused:
                return "Under env a group is a project, so its name has to be one keypaste could resolve.";

            default:
                return "That change could not be made.";
        }
    }

    /// <summary>Whether this group path is an env project rather than an ordinary group.</summary>
    private static bool IsProject(string groupPath)
    {
        var slash = groupPath.IndexOf('/', StringComparison.Ordinal);

        return slash > 0
            && groupPath.AsSpan(0, slash).SequenceEqual(EnvConvention.RootGroup)
            && !groupPath.AsSpan(slash + 1).Contains('/');
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
