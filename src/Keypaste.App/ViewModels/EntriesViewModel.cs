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
internal sealed partial class EntriesViewModel : ObservableObject, IDisposable, IHoldsDraft
{
    private readonly AppVaultSession _session;
    private readonly ClipboardCountdown _clipboard;

    private IReadOnlyList<EntryRow> _all = [];
    private readonly EntryActivitySource? _activity;
    private readonly IWebLauncher? _web;
    private readonly Action<Action>? _post;
    private readonly IVaultFilePicker? _picker;
    private GroupNode? _selectedGroup;
    private EntryName? _selection;
    private EntryName? _pinned;
    private EntryDetailViewModel? _detail;
    private string _search = string.Empty;
    private string? _matchedQuery;
    private Dictionary<EntryName, EntryMatch> _matches = [];
    private Undoable? _undo;
    private NewItemViewModel? _newItem;

    internal EntriesViewModel(
        AppVaultSession session,
        ClipboardCountdown clipboard,
        EntryActivitySource? activity = null,
        IWebLauncher? web = null,
        Action<Action>? post = null,
        IVaultFilePicker? picker = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clipboard);

        _session = session;
        _clipboard = clipboard;
        _activity = activity;
        _web = web;
        _post = post;
        _picker = picker;

        _session.Edited += OnVaultEdited;

        BeginAddCommand = new RelayCommand(BeginAdd, () => !IsAdding);
        DeleteCommand = new RelayCommand(
            DeleteOrAsk,
            () => Selected is not null && !IsConfirmingDelete);
        ConfirmDeleteCommand = new RelayCommand(Delete, () => IsConfirmingDelete);
        CancelDeleteCommand = new RelayCommand(
            () => IsConfirmingDelete = false,
            () => IsConfirmingDelete);

        OrganizeCommand = new RelayCommand(BeginOrganize, () => Selected is not null && !IsOrganizing);
        ConfirmOrganizeCommand = new RelayCommand(ConfirmOrganize, () => IsOrganizing);
        CancelOrganizeCommand = new RelayCommand(() => CloseOrganize(), () => IsOrganizing);

        BeginCreateGroupCommand = new RelayCommand(BeginCreateGroup, () => !IsCreatingGroup);
        ConfirmCreateGroupCommand = new RelayCommand(ConfirmCreateGroup, () => IsCreatingGroup);
        BeginRenameGroupCommand = new RelayCommand(
            BeginRenameGroup,
            () => !IsRenamingGroup && SelectedGroup is { IsEverything: false });
        ConfirmRenameGroupCommand = new RelayCommand(ConfirmRenameGroup, () => IsRenamingGroup);
        BeginMoveGroupCommand = new RelayCommand(
            BeginMoveGroup,
            () => !IsMovingGroup && SelectedGroup is { IsEverything: false });
        ConfirmMoveGroupCommand = new RelayCommand(ConfirmMoveGroup, () => IsMovingGroup);
        BeginDeleteGroupCommand = new RelayCommand(
            BeginDeleteGroup,
            () => !IsConfirmingGroupDelete && SelectedGroup is { IsEverything: false });
        ConfirmDeleteGroupCommand = new RelayCommand(ConfirmDeleteGroup, () => IsConfirmingGroupDelete);
        CancelGroupCommand = new RelayCommand(
            CloseGroupForms,
            () => IsCreatingGroup || IsRenamingGroup || IsMovingGroup || IsConfirmingGroupDelete);
        DuplicateCommand = new RelayCommand(Duplicate, () => Selected is not null);

        Reload();

        if (_activity is not null)
        {
            _activity.Changed += OnActivity;
            ApplyActivity();
        }
    }

    /// <summary>The group tree, flattened, with "All items" first.</summary>
    [ObservableProperty]
    internal partial IReadOnlyList<GroupNode> Groups { get; private set; } = [];

    /// <summary>Which group is showing. Null means every entry.</summary>
    internal GroupNode? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (SetProperty(ref _selectedGroup, value))
            {
                OnPropertyChanged(nameof(CanRenameGroup));
                OnPropertyChanged(nameof(ListTitle));
                OnPropertyChanged(nameof(PaneNamesGroup));
                OnPropertyChanged(nameof(ListEmptyOffersSearchAll));
                OnPropertyChanged(nameof(ListEmptyNote));
                OnPropertyChanged(nameof(ListEmptyOffersImport));
                OnPropertyChanged(nameof(CreateGroupPrompt));
                OnPropertyChanged(nameof(RenameGroupPrompt));
                OnPropertyChanged(nameof(MoveGroupPrompt));
                OnPropertyChanged(nameof(GroupDeletePrompt));
                OnPropertyChanged(nameof(ShowsProjectRenameNote));
                OnPropertyChanged(nameof(ProjectRenameNote));
                BeginRenameGroupCommand.NotifyCanExecuteChanged();
                BeginMoveGroupCommand.NotifyCanExecuteChanged();
                BeginDeleteGroupCommand.NotifyCanExecuteChanged();

                if (_pinned is { } pin && !Shows(value, pin.GroupPath))
                {
                    _pinned = null;
                }

                Filter();
                ClosePaneTheListHid();
                CloseGroupForms();
            }
        }
    }

    /// <summary>Whether a group is showing, which Rename beside its name then renames.</summary>
    internal bool CanRenameGroup => SelectedGroup is { IsEverything: false };

    /// <summary>What the list header names, which is what the search covers: every item, or the group showing.</summary>
    internal string ListTitle =>
        SelectedGroup is { IsEverything: false } group ? EntryNameSanitizer.SanitizePath(group.Path).Text : "All items";

    /// <summary>Shows every item again, which widens the search to all of them.</summary>
    internal void ShowEveryItem() => SelectedGroup = Groups.FirstOrDefault(group => group.IsEverything);

    /// <summary>What matches the search and the selected group.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsListEmpty), nameof(Selected), nameof(DeletePrompt), nameof(OrganizePrompt))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand), nameof(OrganizeCommand), nameof(DuplicateCommand))]
    internal partial IReadOnlyList<EntryRow> Rows { get; private set; } = [];

    /// <summary>Whether the list has nothing to show, so it says why instead of standing blank.</summary>
    internal bool ShowsListEmpty => Rows.Count == 0 && _session.Unlocked is not null;

    /// <summary>Why the list is empty: nothing matched the filter, or there is nothing where it is looking.</summary>
    internal string ListEmptyNote =>
        Search.Trim() is { Length: > 0 } query
            ? $"No items match “{DisplayTextSanitizer.Sanitize(query, 64).Text}”."
            : SelectedGroup is { IsEverything: false }
                ? $"No items in {ListTitle}."
                : "No items in this vault yet.";

    /// <summary>Whether a search found nothing, so clearing it is the next step.</summary>
    internal bool ListEmptyOffersClearSearch => Search.Trim().Length > 0;

    /// <summary>Whether that search looked in one group, so looking everywhere is a next step too.</summary>
    internal bool ListEmptyOffersSearchAll => ListEmptyOffersClearSearch && CanRenameGroup;

    /// <summary>Clears the search box, which the shell owns.</summary>
    internal Action? ClearSearchBox { get; set; }

    /// <summary>Widens the search to every item through the shell, which keeps a draft from being lost.</summary>
    internal Action? WidenSearch { get; set; }

    [RelayCommand]
    private void ClearSearch() => ClearSearchBox?.Invoke();

    [RelayCommand]
    private void SearchAllItems() => WidenSearch?.Invoke();

    /// <summary>Whether the empty list offers New, which it does when a filter is not the reason.</summary>
    internal bool ListEmptyOffersNew => Search.Trim().Length == 0;

    /// <summary>
    /// Whether the empty list also offers Import .kdbx: the vault holds no item at all, as a new one
    /// does, so the first thing a KeePass user may want is to bring their database in.
    /// </summary>
    internal bool ListEmptyOffersImport => TotalCount == 0 && ListEmptyOffersNew && SelectedGroup is null or { IsEverything: true };

    /// <summary>The search box.</summary>
    /// <remarks>
    /// <para>
    /// <b>The matching happens in <see cref="Vault.Search"/>, not here.</b> A query covers titles, group
    /// paths, usernames, URLs and the entry's own tags, and core returns names, a
    /// <see cref="MatchedFields"/> and the tags matched, so the rule for what a query may reach is
    /// core's alone.
    /// </para>
    /// <para>
    /// A password, a note and a custom field cannot be searched: core never reads them, so a query is
    /// never a way to confirm a secret without opening the entry that holds it.
    /// </para>
    /// </remarks>
    internal string Search
    {
        get => _search;
        set
        {
            if (SetProperty(ref _search, value))
            {
                OnPropertyChanged(nameof(ListEmptyNote));
                OnPropertyChanged(nameof(ListEmptyOffersNew));
                OnPropertyChanged(nameof(ListEmptyOffersClearSearch));
                OnPropertyChanged(nameof(ListEmptyOffersSearchAll));
                OnPropertyChanged(nameof(ListEmptyOffersImport));
                // A new query is a new question, so an entry held in the list by the last one stops
                // being held by this one.
                _pinned = null;
                ReturnToList();
                Filter();
                ClosePaneTheListHid();
            }
        }
    }

    /// <summary>A search asks for the list, so a form or a comparison holding nothing to lose gives way to it; the shell has already asked about one that does.</summary>
    private void ReturnToList()
    {
        if (!ShowsList && PaneDraft is null)
        {
            NewItem = null;
            Detail?.LeaveWholeView();
        }
    }

    /// <summary>The pane shows only what the list does, so the group or query just chosen closes an item it hid.</summary>
    private void ClosePaneTheListHid()
    {
        if (_selection is { } name && !Rows.Any(row => row.Name == name))
        {
            Select(null);
        }
    }

    /// <summary>The selected row, or null.</summary>
    /// <remarks>
    /// <para>
    /// <b>Held as the entry's name rather than as the row object, so what the list does to its rows
    /// does not decide the selection.</b> The list writes null back into this property whenever the
    /// row it had selected leaves <see cref="Rows"/>. A null from the list is therefore ignored;
    /// <see cref="Select"/> is how the selection is deliberately dropped: by a query or group that
    /// hides it, <see cref="Dispose"/>, the locked branch of <see cref="Reload"/> and a completed delete.
    /// </para>
    /// <para>
    /// The getter looks the row up in the current list, so an equal row selected again is the same
    /// value and the list highlights it the moment it comes back.
    /// </para>
    /// <para>
    /// While the add form stands in the pane's place no row is selected, so the list does not
    /// highlight an entry the pane is not showing; choosing one closes the form.
    /// </para>
    /// </remarks>
    internal EntryRow? Selected
    {
        get => _selection is { } name && !IsAdding
            ? Rows.FirstOrDefault(row => row.Name == name)
            : null;
        set
        {
            // The list writes the row it was just told to show, which is no choice to act on.
            if (value is not { } row || (!IsAdding && row.Name == _selection))
            {
                return;
            }

            if (PaneDraft is { } draft && AskDiscard is { } ask)
            {
                ask(draft, () => MoveSelection(row.Name), () => OnPropertyChanged(nameof(Selected)));
                return;
            }

            MoveSelection(row.Name);
        }
    }

    private string? _renamedGroupName;

    private void MoveSelection(EntryName name)
    {
        if (IsAdding)
        {
            CancelAdd();
        }

        Select(name);
    }

    /// <summary>How this screen asks "Discard …?" before choosing a row loses a draft; the shell's, or none to lose it unasked.</summary>
    internal Action<string, Action, Action?>? AskDiscard { get; init; }

    /// <inheritdoc/>
    public string? UnsavedDraft => PaneDraft ?? GroupDraft();

    // What choosing another row would lose; a group's form stays open across that.
    private string? PaneDraft => _newItem?.UnsavedDraft ?? OrganizeDraft() ?? Detail?.UnsavedDraft;

    internal Func<string?> NewItemDraft => () => _newItem?.UnsavedDraft;

    internal Func<string?> OrganizeDraft => () =>
        IsOrganizing && Selected is { } row && DraftTitle.Trim() != row.Title ? "this new name" : null;

    internal Func<string?> GroupDraft => () =>
        (IsCreatingGroup && DraftGroupName.Trim().Length > 0)
        || (IsRenamingGroup && DraftGroupName.Trim() != _renamedGroupName)
            ? "this group's name"
            : null;

    /// <summary>
    /// Whether the delete confirmation is showing.
    /// </summary>
    /// <remarks>
    /// A second click rather than a modal, and the one place in this screen where
    /// <c>KpDanger</c> appears — <c>Tokens.axaml</c> reserves it for exactly this. In a vault with
    /// a recycle bin the entry can be recovered; in one whose owner turned the bin off it cannot,
    /// and <see cref="DeletePrompt"/> says which vault this is rather than guessing.
    /// </remarks>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand), nameof(ConfirmDeleteCommand), nameof(CancelDeleteCommand))]
    internal partial bool IsConfirmingDelete { get; private set; }

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

            return $"Delete {name}? There is no undo.";
        }
    }

    /// <summary>
    /// Whether "+ New" is the screen's primary action: not while a form here shows a primary of its own
    /// or the search beside it shows its focus, since a view has one amber element.
    /// </summary>
    internal bool AddIsPrimary =>
        !SearchHasFocus && !IsAdding && !IsOrganizing && !IsCreatingGroup && !IsRenamingGroup && !IsMovingGroup && _detail is not { HasOwnPrimary: true };

    /// <summary>Whether the pane says the item's group: only while the list and its Group column are away, and the header does not already name it.</summary>
    internal bool PaneNamesGroup => !ShowsList && _detail is { GroupPath.Length: > 0 } detail && detail.GroupPath != SelectedGroup?.Path;

    /// <summary>Whether the search in the list header holds the keyboard.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddIsPrimary))]
    internal partial bool SearchHasFocus { get; set; }

    /// <summary>
    /// Whether the list shows above the item's preview. A new item, an edit and a comparison of two
    /// revisions take the whole view instead, as KeePassXC's editor does.
    /// </summary>
    internal bool ShowsList => !IsAdding && _detail is not { TakesWholeView: true };

    /// <summary>The selected entry's fields, or null when nothing is selected.</summary>
    internal EntryDetailViewModel? Detail
    {
        get => _detail;
        private set
        {
            var previous = _detail;

            if (previous is not null)
            {
                previous.PropertyChanged -= OnDetailChanged;
            }

            if (value is not null)
            {
                value.PropertyChanged += OnDetailChanged;
            }

            if (SetProperty(ref _detail, value))
            {
                OnPropertyChanged(nameof(AddIsPrimary));
                OnPropertyChanged(nameof(ShowsList));
                OnPropertyChanged(nameof(PaneNamesGroup));
                OnPropertyChanged(nameof(ShowsPlaceholder));
                // Disposed on the way out, not left to a collection: it holds a username, a URL and
                // a notes field read from an open vault, and the lock has to mean something.
                previous?.Dispose();
            }
        }
    }

    /// <summary>Whether the pane's place says to choose an entry: nothing is open and nothing is being made.</summary>
    internal bool ShowsPlaceholder => _detail is null && !IsAdding;

    /// <summary>A calm sentence when something did not work, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    internal partial string? Error { get; private set; }

    internal bool HasError => Error is not null;

    /// <summary>What the last deletion did, or null.</summary>
    /// <remarks>
    /// Reported after the act, from the outcome core returned, so a vault whose recycle bin was
    /// switched off between the confirmation and the deletion cannot make this line wrong. The CLI
    /// says the same thing for the same reason.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    internal partial string? Notice { get; private set; }

    internal bool HasNotice => Notice is not null;

    /// <summary>Whether the last deletion can still be taken back from here.</summary>
    internal bool CanUndo => _undo is not null;

    /// <summary>Whether the last change was a delete, which the notice's Restore link puts back.</summary>
    internal bool OffersRestore => _undo is { Restores: true };

    /// <summary>How many entries the vault holds, for the empty state.</summary>
    internal int TotalCount => _all.Count;

    /// <summary>Whether the add form is showing.</summary>
    internal bool IsAdding => _newItem is not null;

    /// <summary>The New item form while it is open (N.4).</summary>
    internal NewItemViewModel? NewItem
    {
        get => _newItem;
        private set
        {
            var old = _newItem;

            if (SetProperty(ref _newItem, value))
            {
                if (old is not null)
                {
                    old.PropertyChanged -= OnNewItemChanged;
                }

                if (value is not null)
                {
                    value.PropertyChanged += OnNewItemChanged;
                }

                OnPropertyChanged(nameof(IsAdding));
                OnPropertyChanged(nameof(Selected));
                OnPropertyChanged(nameof(AddIsPrimary));
                OnPropertyChanged(nameof(ShowsList));
                OnPropertyChanged(nameof(PaneNamesGroup));
                OnPropertyChanged(nameof(ShowsPlaceholder));
                OnPropertyChanged(nameof(DeletePrompt));
                OnPropertyChanged(nameof(OrganizePrompt));
                BeginAddCommand.NotifyCanExecuteChanged();
                CancelAddCommand.NotifyCanExecuteChanged();
                ConfirmAddCommand.NotifyCanExecuteChanged();
                DeleteCommand.NotifyCanExecuteChanged();
                OrganizeCommand.NotifyCanExecuteChanged();
                DuplicateCommand.NotifyCanExecuteChanged();
                old?.Dispose();
            }
        }
    }

    private void OnNewItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NewItemViewModel.Title))
        {
            ConfirmAddCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnDetailChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EntryDetailViewModel.HasOwnPrimary))
        {
            OnPropertyChanged(nameof(AddIsPrimary));
        }

        if (e.PropertyName == nameof(EntryDetailViewModel.TakesWholeView))
        {
            OnPropertyChanged(nameof(ShowsList));
            OnPropertyChanged(nameof(PaneNamesGroup));
        }
    }

    /// <summary>Whether the organize form is showing for the selected entry.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddIsPrimary))]
    [NotifyPropertyChangedFor(nameof(OrganizeTitleProblem), nameof(HasOrganizeTitleProblem))]
    [NotifyCanExecuteChangedFor(nameof(OrganizeCommand), nameof(ConfirmOrganizeCommand), nameof(CancelOrganizeCommand))]
    internal partial bool IsOrganizing { get; private set; }

    /// <summary>Whether the new-group form is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddIsPrimary))]
    [NotifyCanExecuteChangedFor(nameof(BeginCreateGroupCommand), nameof(ConfirmCreateGroupCommand), nameof(CancelGroupCommand))]
    internal partial bool IsCreatingGroup { get; private set; }

    /// <summary>Whether the rename-group form is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddIsPrimary), nameof(ShowsProjectRenameNote), nameof(ProjectRenameNote))]
    [NotifyCanExecuteChangedFor(nameof(BeginRenameGroupCommand), nameof(ConfirmRenameGroupCommand), nameof(CancelGroupCommand))]
    internal partial bool IsRenamingGroup { get; private set; }

    /// <summary>Whether the move-group form is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddIsPrimary))]
    [NotifyCanExecuteChangedFor(nameof(BeginMoveGroupCommand), nameof(ConfirmMoveGroupCommand), nameof(CancelGroupCommand))]
    internal partial bool IsMovingGroup { get; private set; }

    /// <summary>Whether the group delete confirmation is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GroupDeletePrompt))]
    [NotifyCanExecuteChangedFor(nameof(BeginDeleteGroupCommand), nameof(ConfirmDeleteGroupCommand), nameof(CancelGroupCommand))]
    internal partial bool IsConfirmingGroupDelete { get; private set; }

    /// <summary>The group the selected group would move into, from <see cref="MoveTargets"/>.</summary>
    [ObservableProperty]
    internal partial GroupNode? GroupMoveTarget { get; set; }

    /// <summary>Which group is being moved.</summary>
    internal string MoveGroupPrompt =>
        SelectedGroup is { IsEverything: false } group
            ? $"Move {EntryNameSanitizer.SanitizePath(group.Path).Text} and everything in it into another group."
            : string.Empty;

    /// <summary>
    /// What the group delete confirmation asks, naming how many items go with the group and whether
    /// they can come back.
    /// </summary>
    /// <remarks>
    /// Read from the vault for the reason <see cref="DeletePrompt"/> gives, and counted from the list,
    /// which holds every item under the group, its subgroups' included.
    /// </remarks>
    internal string GroupDeletePrompt
    {
        get
        {
            if (SelectedGroup is not { IsEverything: false } group)
            {
                return string.Empty;
            }

            var name = EntryNameSanitizer.SanitizePath(group.Path).Text;
            var count = _all.Count(row => group.Contains(row.GroupPath));
            var items = count == 1 ? "1 item" : $"{count} items";

            return count == 0 && group.Children.Count == 0
                ? $"Delete {name}? This vault has no Trash, so there is no undo."
                : $"{name} holds {(count == 0 ? "groups" : items)}, and this vault has no Trash, so keypaste deletes a group only once it is empty. Move or delete what is in it first.";
        }
    }

    /// <summary>Opens the form that moves the selected group into another.</summary>
    internal RelayCommand BeginMoveGroupCommand { get; }

    /// <summary>Moves the selected group, with everything in it, in one write.</summary>
    internal RelayCommand ConfirmMoveGroupCommand { get; }

    /// <summary>Deletes the selected group into the recycle bin, or asks first where the vault has none.</summary>
    internal RelayCommand BeginDeleteGroupCommand { get; }

    /// <summary>Deletes the selected group with everything in it, having been confirmed.</summary>
    internal RelayCommand ConfirmDeleteGroupCommand { get; }

    /// <summary>Copies the selected item beside it, under a new title, and selects the copy.</summary>
    internal RelayCommand DuplicateCommand { get; }

    /// <summary>The title being typed for the selected entry.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OrganizeTitleProblem), nameof(HasOrganizeTitleProblem))]
    internal partial string DraftTitle { get; set; } = string.Empty;

    /// <summary>Why the name typed in Rename or move cannot be used, or null.</summary>
    internal string? OrganizeTitleProblem =>
        !IsOrganizing ? null
        : DraftTitle.Trim().Length == 0 ? "An item needs a name."
        : EntryNameSanitizer.Sanitize(DraftTitle.Trim()) is { WasAltered: true } sanitized ? $"That is not a name keypaste will create. Try '{sanitized.Text}'."
        : null;

    internal bool HasOrganizeTitleProblem => OrganizeTitleProblem is not null;

    /// <summary>The name being typed for a group.</summary>
    [ObservableProperty]
    internal partial string DraftGroupName { get; set; } = string.Empty;

    /// <summary>The groups an entry can be moved into.</summary>
    /// <remarks>
    /// The tree's own list, which comes from <see cref="Vault.ReadGroupPaths"/>: a group holding
    /// nothing is in it and the recycle bin is not, so every offered destination is one core will
    /// accept and a move is not a delete. Offering a typed path instead would make
    /// <see cref="OrganizeOutcome.DestinationMissing"/> the ordinary answer to a typo, where the
    /// add form would have created the group.
    /// </remarks>
    [ObservableProperty]
    internal partial IReadOnlyList<GroupNode> MoveTargets { get; private set; } = [];

    /// <summary>The group the entry would move into.</summary>
    [ObservableProperty]
    internal partial GroupNode? MoveTarget { get; set; }

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
    /// What an agent can see by place, in its settings, its ceilings or its bridge's exposure, is globs
    /// matched against an entry's group path and its title (<see cref="EntryExposure"/>), so it follows
    /// neither the group nor the entry: renaming away from a path an agent sees takes the entry out of
    /// its view, and renaming onto one brings it in. THREATS.md T-13 records that keypaste does not
    /// prevent this. That is no reason to
    /// let somebody do it without being told, so this is a line on the form and never a refusal.
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
        "What an agent can see by place is matched against paths, so this can take these items " +
        "out of an agent's view or bring them into one.";

    /// <summary>Whether the group being renamed is where keypaste puts a project's new keys.</summary>
    internal bool ShowsProjectRenameNote =>
        IsRenamingGroup && SelectedGroup is { IsEverything: false } group && IsProjectHome(group.Path);

    /// <summary>What renaming this group changes about where keypaste puts new keys.</summary>
    internal string ProjectRenameNote
    {
        get
        {
            if (!ShowsProjectRenameNote || SelectedGroup is not { } group)
            {
                return string.Empty;
            }

            var project = EntryNameSanitizer.Sanitize(group.Name).Text;

            return $"New keys for {project} go to items in {EnvConvention.RootGroup}/{project}, which keypaste "
                + "creates again after this rename. Moved items keep their tags, and with them their projects.";
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
            Select(null);
            Forget();

            // A half-entered password is as much a secret as a stored one, and the screen is about
            // to be disposed anyway. Closing the form here disposes it on whichever path runs.
            NewItem = null;
            return;
        }

        var wanted = _selection;

        var wantedGroup = _selectedGroup?.Path;

        _all = [.. vault.ReadEntries()
            .Select(entry => new EntryRow(entry.Title, entry.GroupPath)
            {
                Kind = EntryKinds.Of(entry),
                Username = entry.Username,
                Url = entry.Url,
            })];
        Groups = GroupNode.Flatten(vault.ReadGroupPaths());
        MoveTargets = Groups;
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(ListEmptyOffersImport));

        // The tree is rebuilt from the vault, so the node the sidebar had is not in the new list.
        // Re-pointing at the one with the same path keeps the filter and the highlight where they
        // were; a first visit, or a group that is gone, highlights everything. Assigned to the field
        // rather than the property, because Filter runs below either way.
        _selectedGroup = Groups.FirstOrDefault(node => string.Equals(node.Path, wantedGroup ?? string.Empty, StringComparison.Ordinal))
            ?? Groups.FirstOrDefault(node => node.IsEverything);

        OnPropertyChanged(nameof(SelectedGroup));
        OnPropertyChanged(nameof(CanRenameGroup));
        OnPropertyChanged(nameof(ListTitle));
        OnPropertyChanged(nameof(ListEmptyNote));
        OnPropertyChanged(nameof(ListEmptyOffersImport));
        OnPropertyChanged(nameof(CreateGroupPrompt));
        OnPropertyChanged(nameof(RenameGroupPrompt));
        OnPropertyChanged(nameof(MoveGroupPrompt));
        OnPropertyChanged(nameof(GroupDeletePrompt));
        OnPropertyChanged(nameof(ShowsProjectRenameNote));
        OnPropertyChanged(nameof(ProjectRenameNote));
        BeginRenameGroupCommand.NotifyCanExecuteChanged();
        BeginMoveGroupCommand.NotifyCanExecuteChanged();
        BeginDeleteGroupCommand.NotifyCanExecuteChanged();

        // The vault changed underneath, so an answer about the old one is not an answer about this.
        _matchedQuery = null;

        Filter();

        SelectIfListed(wanted);
        ApplyActivity();
    }

    private void OnActivity(object? sender, EventArgs e) => ApplyActivity();

    /// <summary>Gives every row and the open pane the latest picture of what agents did, never a value.</summary>
    private void ApplyActivity()
    {
        if (_activity?.Current is not { } picture)
        {
            return;
        }

        var now = _session.Clock.GetUtcNow();

        foreach (var row in _all)
        {
            row.Apply(picture, now);
        }

        Detail?.Apply(picture);
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
        DraftTitle = string.Empty;
        DraftGroupName = string.Empty;
        MoveTarget = null;
        GroupMoveTarget = null;
        MoveTargets = [];
        IsOrganizing = false;
        IsCreatingGroup = false;
        IsRenamingGroup = false;
        IsMovingGroup = false;
        IsConfirmingGroupDelete = false;

        OnPropertyChanged(nameof(Search));
        OnPropertyChanged(nameof(ListEmptyNote));
        OnPropertyChanged(nameof(ListEmptyOffersNew));
        OnPropertyChanged(nameof(ListEmptyOffersClearSearch));
        OnPropertyChanged(nameof(ListEmptyOffersSearchAll));
        OnPropertyChanged(nameof(ListEmptyOffersImport));
    }

    /// <summary>Nothing derived from the vault outlives this.</summary>
    public void Dispose()
    {
        _session.Edited -= OnVaultEdited;

        if (_activity is not null)
        {
            _activity.Changed -= OnActivity;
        }

        _all = [];
        Rows = [];
        Groups = [];
        Select(null);
        Forget();
        Detail = null;
        Offer(null, null);
        NewItem = null;
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
            Error = "That item could not be read. The vault may have locked: unlock it, then try again.";
            return null;
        }

        Error = null;
        return new EntryDetailViewModel(_session, _clipboard, entry, message => Error = message, Reselect, _web, _post, _picker);
    }

    /// <summary>Reads the list again and lands on the entry a restore left behind, unless a merge took it to Trash.</summary>
    /// <remarks>
    /// <see cref="Reload"/> alone is not enough: it keeps the selection by path, and a restored
    /// revision from before a rename gives the entry a different one. It is also not too much: an
    /// <see cref="EntryRow"/> is a record, so reselecting an equal row changes nothing.
    /// </remarks>
    private void Reselect(EntryName name)
    {
        Reload();
        SelectIfListed(name);
    }

    /// <summary>Selects what the vault still holds, and holds it in the list until the next query or group, because an edit or another program's save is not the person hiding it.</summary>
    private void SelectIfListed(EntryName? name)
    {
        Select(name is not null && _all.Any(row => row.Name == name) ? name : null);

        if (_selection is { } selected && !Rows.Any(row => row.Name == selected))
        {
            _pinned = selected;
            Filter();
        }
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
                .Where(row => Shows(group, row.GroupPath))
                .Where(row => query.Length == 0 || _matches.ContainsKey(row.Name))
                .Select(row => query.Length == 0
                    ? row
                    : row with { Fields = _matches[row.Name].Fields, Tags = _matches[row.Name].Tags })
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

    /// <summary>Whether an entry in <paramref name="groupPath"/> is listed while <paramref name="group"/> is selected: a folder lists its children's entries too.</summary>
    private static bool Shows(GroupNode? group, string groupPath) => group is null || group.Contains(groupPath);

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
        Dictionary<EntryName, EntryMatch> found = [];

        foreach (var match in vault.Search(query))
        {
            found[match.Name] = match;
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
            // The add form stands in the pane's place, so choosing an entry to look at closes it.
            if (name is not null && IsAdding)
            {
                CancelAdd();
            }

            Detail = name is null ? null : Build(name);

            if (Detail is not null && _activity?.Current is { } picture)
            {
                Detail.Apply(picture);
            }
        }

        IsConfirmingDelete = false;
        IsOrganizing = false;

        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(DeletePrompt));
        OnPropertyChanged(nameof(OrganizePrompt));
        DeleteCommand.NotifyCanExecuteChanged();
        OrganizeCommand.NotifyCanExecuteChanged();
        DuplicateCommand.NotifyCanExecuteChanged();
    }

    private void BeginAdd()
    {
        if (_session.Unlocked is not { } vault)
        {
            Error = "The vault is locked. Unlock it, then try again.";
            return;
        }

        Notice = null;
        Error = null;
        NewItem = new NewItemViewModel(_session, _clipboard, vault.ReadGroupPaths(), SelectedGroup is { IsEverything: false } group ? group.Path : null);
    }

    [RelayCommand(CanExecute = nameof(IsAdding))]
    private void CancelAdd()
    {
        NewItem = null;
        Error = null;
    }

    private bool CanCreate => _newItem is { HasTitle: true };

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void ConfirmAdd()
    {
        if (_newItem?.Create() is not { } name)
        {
            return;
        }

        NewItem = null;
        Error = null;
        Reload();
        Selected = Rows.FirstOrDefault(row => row.Name == name);
    }

    private void DeleteOrAsk()
    {
        if (_session.Unlocked?.RecyclesDeletedEntries == true)
        {
            Delete();
        }
        else
        {
            IsConfirmingDelete = true;
        }
    }

    private void Delete()
    {
        Offer(null, null);

        if (Selected is not { } row)
        {
            return;
        }

        // The row is addressed by its name rather than its path: two entries can share a path.
        var write = _session.Write(
            vault => (Outcome: vault.RemoveEntry(row.Name, out var id), Recycled: id),
            removal => removal.Outcome != DeletionOutcome.NothingMatched);

        if (write.Problem("delete this again") is { } problem)
        {
            Error = problem;
            return;
        }

        IsConfirmingDelete = false;

        if (write.Outcome == WriteOutcome.NothingToSave)
        {
            Error = "That item is not in this vault any more.";
            Reload();
            return;
        }

        var (outcome, recycled) = write.Value;
        var name = EntryNameSanitizer.SanitizePath(row.Path).Text;

        Select(null);
        Reload();

        // Last, because Reload and the selection it clears both drop a pending offer.
        Offer(
            outcome == DeletionOutcome.Recycled
                ? new Undoable(
                    vault => vault.RestoreRecycled(recycled) is RestoreOutcome.Restored or RestoreOutcome.RestoredToRoot,
                    () => Reselect(row.Name),
                    Restores: true)
                : null,
            outcome == DeletionOutcome.Recycled
                ? $"Moved {name} to the trash."
                : $"Deleted {name}. This vault has no Trash, so nothing can put it back.");
    }

    /// <summary>Reverses the last delete, rename, move, duplicate or group change through its inverse write.</summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_undo is not { } last)
        {
            return;
        }

        var write = _session.Write(last.Reverse, reversed => reversed);

        if (write.Problem("undo this again") is { } problem)
        {
            Error = problem;
            return;
        }

        Offer(null, null);

        if (write.Outcome != WriteOutcome.Saved)
        {
            Error = "That could not be undone, because the vault has changed since. Nothing was changed. Make the change by hand.";
            return;
        }

        Error = null;
        Reload();
        last.Show?.Invoke();
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

        if (HasOrganizeTitleProblem)
        {
            return;
        }

        var target = new EntryName(MoveTarget?.Path ?? string.Empty, DraftTitle.Trim());

        var write = _session.Write(
            vault => (Outcome: vault.Relocate(row.Name, target, out var relocated), Moved: relocated),
            relocation => relocation.Outcome is OrganizeOutcome.Renamed or OrganizeOutcome.Moved or OrganizeOutcome.RenamedAndMoved);

        if (write.Problem("make your change again") is { } problem)
        {
            Error = problem;
            return;
        }

        if (write.Outcome == WriteOutcome.NothingToSave)
        {
            Error = Refusal(write.Value.Outcome, target);
            return;
        }

        var moved = write.Value.Moved!;

        CloseOrganize();
        Offer(
            new Undoable(
                vault => vault.Relocate(moved, row.Name, out _) is OrganizeOutcome.Renamed or OrganizeOutcome.Moved or OrganizeOutcome.RenamedAndMoved,
                () => FollowRelocated(row.Name)),
            Did(write.Value.Outcome, moved));
        FollowRelocated(moved);
    }

    private void FollowRelocated(EntryName moved)
    {
        // Held in the result even when its new name no longer answers the query that found it, so
        // the entry does not vanish from under somebody at the moment they renamed it.
        _pinned = moved;
        Reselect(moved);

        // A filter the entry has just left would hide what somebody is still looking at, so the
        // filter follows the entry rather than the other way round.
        if (SelectedGroup is { IsEverything: false } group && !Shows(group, moved.GroupPath))
        {
            SelectedGroup = Groups.FirstOrDefault(
                node => string.Equals(node.Path, moved.GroupPath, StringComparison.Ordinal))
                ?? Groups.FirstOrDefault(node => node.IsEverything);
        }
    }

    private void BeginCreateGroup()
    {
        CloseOtherForms();
        CloseGroupForms();
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
        CloseGroupForms();
        DraftGroupName = group.Name;
        _renamedGroupName = group.Name;
        IsRenamingGroup = true;
        Error = null;
    }

    private void BeginMoveGroup()
    {
        if (SelectedGroup is not { IsEverything: false } group)
        {
            return;
        }

        CloseOtherForms();
        CloseGroupForms();
        var slash = group.Path.LastIndexOf('/');
        var parent = slash < 0 ? string.Empty : group.Path[..slash];
        GroupMoveTarget = MoveTargets.FirstOrDefault(node => string.Equals(node.Path, parent, StringComparison.Ordinal));
        IsMovingGroup = true;
        Error = null;
    }

    private void BeginDeleteGroup()
    {
        if (SelectedGroup is not { IsEverything: false })
        {
            return;
        }

        CloseOtherForms();
        CloseGroupForms();
        Error = null;

        if (_session.Unlocked?.RecyclesDeletedEntries == true)
        {
            ConfirmDeleteGroup();
        }
        else
        {
            IsConfirmingGroupDelete = true;
        }
    }

    private void CloseGroupForms()
    {
        IsCreatingGroup = false;
        IsRenamingGroup = false;
        IsMovingGroup = false;
        IsConfirmingGroupDelete = false;
        DraftGroupName = string.Empty;
        GroupMoveTarget = null;
    }

    /// <summary>Closes whatever else is open, so one form is on screen at a time.</summary>
    private void CloseOtherForms()
    {
        Notice = null;
        NewItem = null;
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
            follow: false,
            path => new Undoable(vault => vault.DeleteGroup(path) is GroupOutcome.Recycled or GroupOutcome.DeletedPermanently));
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
            follow: true,
            renamed => new Undoable(
                vault => vault.RenameGroup(renamed, selected.Name, out _) == GroupOutcome.Renamed,
                () => ShowGroup(path)));
    }

    /// <summary>Moves the selected group into the chosen one, in one core write, and follows it there.</summary>
    /// <remarks>
    /// The destinations are the item move's own (<see cref="MoveTargets"/>); one core refuses, such as the
    /// group itself or a group inside it, says why rather than being hidden from the list.
    /// </remarks>
    private void ConfirmMoveGroup()
    {
        if (SelectedGroup is not { IsEverything: false } selected)
        {
            return;
        }

        var path = selected.Path;
        var destination = GroupMoveTarget?.Path ?? string.Empty;
        var write = _session.Write(
            vault => (Outcome: vault.MoveGroup(path, destination, out var moved), Path: moved),
            result => result.Outcome == GroupOutcome.Moved);

        if (write.Problem("make your change again") is { } problem)
        {
            Error = problem;
            return;
        }

        if (write.Outcome == WriteOutcome.NothingToSave)
        {
            Error = Refusal(write.Value.Outcome);
            return;
        }

        var movedPath = write.Value.Path;
        var from = ParentOf(path);
        var where = destination.Length == 0 ? "the top level" : EntryNameSanitizer.SanitizePath(destination).Text;

        CloseGroupForms();
        Reload();

        // The sidebar was pointing at a path that no longer exists, so it follows the group, as a rename does.
        ShowGroup(movedPath);
        Error = null;
        Offer(
            new Undoable(vault => vault.MoveGroup(movedPath, from, out _) == GroupOutcome.Moved, () => ShowGroup(path)),
            $"Moved {EntryNameSanitizer.Sanitize(selected.Name).Text} to {where}.");
    }

    /// <summary>Deletes the selected group with everything in it, in one core write, and shows the group it was in.</summary>
    /// <remarks>
    /// Where the vault has a recycle bin the whole group goes there and Trash lists each of its items, which
    /// is where they come back from; without one only an empty group goes, for good.
    /// </remarks>
    private void ConfirmDeleteGroup()
    {
        if (SelectedGroup is not { IsEverything: false } selected)
        {
            return;
        }

        var path = selected.Path;
        var count = _all.Count(row => selected.Contains(row.GroupPath));
        var write = _session.Write(
            vault => vault.DeleteGroup(path),
            outcome => outcome is GroupOutcome.Recycled or GroupOutcome.DeletedPermanently);

        if (write.Problem("delete it again") is { } problem)
        {
            Error = problem;
            return;
        }

        if (write.Outcome == WriteOutcome.NothingToSave)
        {
            Error = Refusal(write.Value);
            return;
        }

        var name = EntryNameSanitizer.SanitizePath(path).Text;
        var parent = ParentOf(path);

        CloseGroupForms();
        Reload();
        SelectedGroup = Groups.FirstOrDefault(node => string.Equals(node.Path, parent, StringComparison.Ordinal))
            ?? Groups.FirstOrDefault(node => node.IsEverything);
        Error = null;
        Offer(
            write.Value == GroupOutcome.Recycled
                ? new Undoable(vault => vault.RestoreRecycledGroup(path) == GroupOutcome.Restored, () => ShowGroup(path), Restores: true)
                : null,
            write.Value == GroupOutcome.DeletedPermanently
                ? $"Deleted {name}. This vault has no Trash, so nothing can put it back."
                : count == 0
                    ? $"Moved {name} to the trash."
                    : $"Moved {name} and the {(count == 1 ? "1 item" : $"{count} items")} in it to the trash. Trash puts each one back.");
    }

    /// <summary>
    /// Copies the selected item beside it in one core write, selects the copy and says which project tags
    /// it left out.
    /// </summary>
    /// <remarks>
    /// The copy is held in the result and followed as an organized item is, so it does not vanish under a
    /// query its new title no longer answers. A project takes one item for each variable, so the
    /// copy is in no project until somebody tags it, and the note says so.
    /// </remarks>
    private void Duplicate()
    {
        if (Selected is not { } row)
        {
            return;
        }

        CloseOtherForms();

        var write = _session.Write(
            vault => (Outcome: vault.CloneEntry(row.Name, out var copy, out var dropped), Copy: copy, Dropped: dropped),
            result => result.Outcome == OrganizeOutcome.Copied);

        if (write.Problem("duplicate it again") is { } problem)
        {
            Error = problem;
            return;
        }

        if (write.Outcome == WriteOutcome.NothingToSave || write.Value.Copy is not { } made)
        {
            Error = write.Value.Outcome == OrganizeOutcome.NameRefused
                ? "keypaste could not give a copy of this item a title it accepts. Rename the item, then duplicate it."
                : "That item is not in this vault any more.";
            return;
        }

        Error = null;
        _pinned = made;
        Reselect(made);
        Offer(
            new Undoable(vault => vault.RemoveEntry(made, out _) != DeletionOutcome.NothingMatched, () => Reselect(row.Name)),
            Duplicated(row.Title, made.Title, write.Value.Dropped));
    }

    /// <summary>What the app says a duplicate made, naming any project tag the copy left out and never a value.</summary>
    private static string Duplicated(string title, string copy, IReadOnlyList<string> dropped)
    {
        var made = $"Duplicated {EntryNameSanitizer.Sanitize(title).Text} as {EntryNameSanitizer.Sanitize(copy).Text}.";

        if (dropped.Count == 0)
        {
            return made;
        }

        var tags = string.Join(", ", dropped.Select(tag => EntryNameSanitizer.Sanitize(tag).Text));
        var which = dropped.Count == 1 ? $"the project tag {tags}" : $"the project tags {tags}";

        return $"{made} The copy leaves out {which}, because a project takes one item for each variable. Tag the copy to put it in another environment.";
    }

    /// <summary>The half of a group operation that is the same for creating and renaming.</summary>
    private void Organize(
        Func<Vault, string, (GroupOutcome Outcome, string Path)> operation,
        GroupOutcome success,
        Func<string, string> said,
        bool follow,
        Func<string, Undoable> undo)
    {
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

        var write = _session.Write(vault => operation(vault, sanitized.Text), result => result.Outcome == success);

        if (write.Problem("make your change again") is { } problem)
        {
            Error = problem;
            return;
        }

        if (write.Outcome == WriteOutcome.NothingToSave)
        {
            Error = Refusal(write.Value.Outcome);
            return;
        }

        var path = write.Value.Path;

        CloseGroupForms();
        Reload();

        // A rename has to be followed: the sidebar was pointing at a path that no longer exists, and
        // Reload cannot re-point at it. Creating one must not be — somebody who makes a group in
        // order to move things into it would be dropped inside it, with an empty list and nothing
        // left to select.
        if (follow)
        {
            ShowGroup(path);
        }

        Offer(undo(path), said(path));
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
                return "That item is not in this vault any more.";

            case OrganizeOutcome.DestinationMissing:
                return "That group is not in this vault any more.";

            case OrganizeOutcome.DestinationUnchanged:
                return "That is already its name and its group.";

            case OrganizeOutcome.DestinationOccupied:
                return "That group already has an item with this name.";

            case OrganizeOutcome.DestinationAmbiguous:
                return "That would leave two items answering to one path, and keypaste could not tell them apart afterwards. Choose another name.";

            case OrganizeOutcome.NameRefused:
                VaultNameRules.IsValidTitle(target.Title, out var why);
                return $"keypaste will not use that title: {why}.";

            default:
                return "That change could not be made. Check the name and the group, then try again.";
        }
    }

    /// <summary>What the app says when core refused a group a name, a move or a delete.</summary>
    private string Refusal(GroupOutcome outcome)
    {
        switch (outcome)
        {
            case GroupOutcome.NothingMatched:
            case GroupOutcome.ParentMissing:
                return "That group is not in this vault any more.";

            case GroupOutcome.DestinationUnchanged:
                return IsMovingGroup ? "The group is already there." : "That is already its name.";

            case GroupOutcome.DestinationOccupied:
                return "Something in there already answers to that name. Nothing was changed. Choose another name.";

            case GroupOutcome.DestinationAmbiguous:
                return "That change would leave two items answering to one path, and keypaste could not tell them apart afterwards. Choose another name.";

            case GroupOutcome.DestinationInside:
                return "A group cannot go inside itself or a group within it. Choose another group.";

            case GroupOutcome.NotEmpty:
                return "This vault has no Trash, so keypaste deletes a group only once it is empty. Move or delete what is in it first.";

            case GroupOutcome.NameRefused:
                VaultNameRules.IsValidGroupName(DraftGroupName.Trim(), out var why);
                return $"keypaste will not use that group name: {why}.";

            case GroupOutcome.NameReserved:
                return "env at the top level and the Trash are names keypaste assigns. A group cannot be created as one, renamed to one "
                    + "or moved to the top level as env; env is not renamed, moved or deleted, and a group holding the Trash is not deleted. Choose another name or group.";

            default:
                return "That change could not be made. Check the name and the group, then try again.";
        }
    }

    /// <summary>Whether this group is one level under the env root, where keypaste puts a project's new keys.</summary>
    private static bool IsProjectHome(string groupPath)
    {
        var slash = groupPath.IndexOf('/', StringComparison.Ordinal);

        return slash > 0
            && groupPath.AsSpan(0, slash).SequenceEqual(EnvConvention.RootGroup)
            && !groupPath.AsSpan(slash + 1).Contains('/');
    }

    private void ShowGroup(string path) =>
        SelectedGroup = Groups.FirstOrDefault(node => string.Equals(node.Path, path, StringComparison.Ordinal)) ?? SelectedGroup;

    private static string ParentOf(string groupPath)
    {
        var slash = groupPath.LastIndexOf('/');
        return slash < 0 ? string.Empty : groupPath[..slash];
    }

    /// <summary>Holds the last change's undo, or drops it, with what the notice says.</summary>
    private void Offer(Undoable? undo, string? notice)
    {
        _undo = undo;
        Notice = notice;
        NotifyUndoChanged();
    }

    // Any write, from anywhere, ends the undo, so a reverse never overwrites newer work; a write here offers its own once it returns.
    private void OnVaultEdited(object? sender, VaultEdit edit)
    {
        _undo = null;

        if (_post is { } post)
        {
            post(NotifyUndoChanged);
        }
        else
        {
            NotifyUndoChanged();
        }
    }

    private void NotifyUndoChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(OffersRestore));
        UndoCommand.NotifyCanExecuteChanged();
    }

    private sealed record Undoable(Func<Vault, bool> Reverse, Action? Show = null, bool Restores = false);
}
