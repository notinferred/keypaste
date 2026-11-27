using System.Globalization;
using Keypaste.App.Clipboard;
using Keypaste.App.Resources;
using Keypaste.App.Session;
using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>
/// The Entries screen: each vault's items as one flat list, searchable, and one entry's detail.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything here is rebuilt from the vault rather than cached across a lock.</b>
/// <see cref="ShellViewModel"/> disposes its content on every navigation and on every lock, so this
/// object's lifetime is bounded by an open vault.
/// </para>
/// <para>
/// <b>The list is flat.</b> An item keeps the group the file holds it in, which the CLI, search and agents read, but
/// this screen lists every item of the vault together and never moves one between groups. What must not be
/// duplicated is a <em>rule</em> — where an entry lives, what a name may contain — and those stay in
/// <see cref="EntryNameSanitizer"/> and the core's own addressing.
/// </para>
/// </remarks>
internal sealed partial class EntriesViewModel : ObservableObject, IDisposable, IHoldsDraft
{
    private readonly AppVaultSession _session;
    private readonly IReadOnlyList<AppVaultSession> _alsoListed;
    private readonly Func<AppVaultSession, string> _titleOf;
    private readonly ClipboardCountdown _clipboard;

    private IReadOnlyList<EntryRow> _all = [];
    private IReadOnlyList<EntryRow>? _sorted;
    private TableOrder? _sortedBy;
    private readonly EntryActivitySource? _activity;
    private readonly IWebLauncher? _web;
    private readonly Action<Action>? _post;
    private readonly IVaultFilePicker? _picker;
    private EntryName? _selection;
    private AppVaultSession? _selectionVault;
    private EntryName? _pinned;
    private AppVaultSession? _pinnedVault;
    private EntryDetailViewModel? _detail;
    private string _search = string.Empty;
    private string? _matchedQuery;
    private Dictionary<(AppVaultSession Vault, string Uuid, EntryName Name), EntryMatch> _matches = [];
    private Undoable? _undo;
    private NewItemViewModel? _newItem;

    internal EntriesViewModel(
        AppVaultSession session,
        ClipboardCountdown clipboard,
        EntryActivitySource? activity = null,
        IWebLauncher? web = null,
        Action<Action>? post = null,
        IVaultFilePicker? picker = null,
        TableState? table = null,
        IReadOnlyList<AppVaultSession>? alsoListed = null,
        Func<AppVaultSession, string>? titleOf = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clipboard);

        _session = session;
        _alsoListed = [.. (alsoListed ?? []).Where(other => !ReferenceEquals(other, session))];
        _titleOf = titleOf ?? (_ => string.Empty);
        _clipboard = clipboard;
        _activity = activity;
        _web = web;
        _post = post;
        _picker = picker;

        foreach (var listed in ListedVaults)
        {
            listed.Edited += OnVaultEdited;
        }

        Table = table ?? TableStates.NewItems();
        Table.OrderChanged += OnOrderChanged;

        BeginAddCommand = new RelayCommand(BeginAdd, () => !IsAdding);
        BeginAddOfKindCommand = new RelayCommand<ItemTemplate>(BeginAddOfKind, _ => !IsAdding);
        ChooseTemplateCommand = new RelayCommand(ChooseTemplate, () => !IsAdding);
        DeleteCommand = new RelayCommand(
            DeleteOrAsk,
            () => Selected is not null && !IsConfirmingDelete);
        ConfirmDeleteCommand = new RelayCommand(Delete, () => IsConfirmingDelete);
        CancelDeleteCommand = new RelayCommand(
            () => IsConfirmingDelete = false,
            () => IsConfirmingDelete);

        OrganizeCommand = new RelayCommand(BeginOrganize, () => Selected is { } row && IsHome(row) && !IsOrganizing);
        ConfirmOrganizeCommand = new RelayCommand(ConfirmOrganize, () => IsOrganizing);
        CancelOrganizeCommand = new RelayCommand(() => CloseOrganize(), () => IsOrganizing);

        DuplicateCommand = new RelayCommand(Duplicate, () => Selected is not null);

        Reload();

        if (_activity is not null)
        {
            _activity.Changed += OnActivity;
            ApplyActivity();
        }
    }

    /// <summary>Which of the vault's places the list shows: every item, or one kind of them.</summary>
    internal ItemPlace Place
    {
        get => _place;
        set
        {
            if (SetProperty(ref _place, value))
            {
                OnPropertyChanged(nameof(ListTitle));
                OnPropertyChanged(nameof(ListEmptyNote));
                OnPropertyChanged(nameof(ListEmptyOffersImport));
                _pinned = null;
                Filter();
                ClosePaneTheListHid();
            }
        }
    }

    private ItemPlace _place;

    /// <summary>The vault's title, which the item's kind line and the list's header name.</summary>
    internal string VaultTitle { get; init; } = string.Empty;

    /// <summary>The title of the open item's vault, which the pane's kind line names.</summary>
    internal string ItemVaultTitle => ListsSeveralVaults && _selectionVault is { } vault ? _titleOf(vault) : VaultTitle;

    /// <summary>What the list's header says under its title, muted: the vault, or All Accounts.</summary>
    [ObservableProperty]
    internal partial string ListSubtitle { get; set; } = string.Empty;

    /// <summary>Whether the list holds the items of several vaults, as All Accounts and Favorites do, each row naming its vault.</summary>
    internal bool ListsSeveralVaults => _alsoListed.Count > 0;

    /// <summary>The vaults whose items the list holds, this one first.</summary>
    private IEnumerable<AppVaultSession> ListedVaults => [_session, .. _alsoListed];

    /// <summary>The vaults whose items the list holds, this one first, which a lock of any of them makes the shell read again.</summary>
    internal IReadOnlyList<AppVaultSession> Vaults => [.. ListedVaults];

    /// <summary>The vault a row's item is in.</summary>
    private AppVaultSession SessionOf(EntryRow row) => row.Vault ?? _session;

    /// <summary>The vault of the open item when <paramref name="item"/> names it, UUID and all, or null.</summary>
    internal AppVaultSession? VaultOfOpen(EntryName item) =>
        Detail?.Name is { } open && open == item && string.Equals(open.Uuid, item.Uuid, StringComparison.Ordinal) ? _selectionVault : null;

    /// <summary>Whether a row's item is in this screen's own vault, which the list's forms change.</summary>
    private bool IsHome(EntryRow row) => ReferenceEquals(SessionOf(row), _session);

    private bool SameVault(EntryRow row, AppVaultSession? vault) => vault is null || ReferenceEquals(SessionOf(row), vault);

    private bool IsSelected(EntryRow row) => row.Is(_selection) && SameVault(row, _selectionVault);

    /// <summary>The list as drawn: the rows, under a heading for each day range while it runs by when items changed.</summary>
    internal IReadOnlyList<object> ListedRows
    {
        get
        {
            if (Table.Order?.Column != TableStates.Modified || Rows.Count == 0)
            {
                return [.. Rows];
            }

            var now = _session.Clock.GetLocalNow();
            List<object> listed = [];
            RowSection? last = null;

            foreach (var row in Rows)
            {
                var section = RowSections.Of(row.Modified, now);

                if (section != last)
                {
                    listed.Add(section);
                    last = section;
                }

                listed.Add(row);
            }

            return listed;
        }
    }

    /// <summary>Each item's tags by its group and title, which its kind reads.</summary>
    internal static Dictionary<(string GroupPath, string Title), IReadOnlyList<string>> TagsByName(Vault vault)
    {
        ArgumentNullException.ThrowIfNull(vault);

        Dictionary<(string, string), IReadOnlyList<string>> tags = [];

        foreach (var tagged in vault.ReadTags())
        {
            tags.TryAdd((tagged.Name.GroupPath, tagged.Name.Title), tagged.Tags);
        }

        return tags;
    }

    /// <summary>What the list header names, which is what the search covers: the place showing.</summary>
    internal string ListTitle => ItemPlaces.Title(Place);

    /// <summary>How the list is sorted and sized.</summary>
    internal TableState Table { get; }

    /// <summary>What matches the search and the place, in the table's order.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ListedRows), nameof(ListCount), nameof(ShowsListEmpty), nameof(ShowsPlaceholder), nameof(Selected), nameof(DeletePrompt))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand), nameof(OrganizeCommand), nameof(DuplicateCommand))]
    internal partial IReadOnlyList<EntryRow> Rows { get; private set; } = [];

    /// <summary>How many items the list shows, beside its title, as the sidebar counts a place; empty while it shows none.</summary>
    internal string ListCount => Rows.Count > 0 ? Rows.Count.ToString(CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>Whether the list has nothing to show, so it says why instead of standing blank.</summary>
    internal bool ShowsListEmpty => Rows.Count == 0 && _session.Unlocked is not null;

    /// <summary>Why the list is empty: nothing matched the filter, or there is nothing where it is looking.</summary>
    internal string ListEmptyNote =>
        Search.Trim() is { Length: > 0 } query
            ? $"No items match “{DisplayTextSanitizer.Sanitize(query, 64).Text}”."
            : Place == ItemPlace.Favorites
                ? Strings.NoFavoritesYet
            : Place != ItemPlace.All
                ? $"No items in {ListTitle}. {Strings.NewItemAddsOneHere}"
                : "No items in this vault yet.";

    /// <summary>Whether a search found nothing, so clearing it is the next step.</summary>
    internal bool ListEmptyOffersClearSearch => Search.Trim().Length > 0;

    /// <summary>Clears the search box, which the shell owns.</summary>
    internal Action? ClearSearchBox { get; set; }

    /// <summary>Hides any value on screen before the open item is read again; the shell's.</summary>
    internal Action? EndReveals { get; init; }

    [RelayCommand]
    private void ClearSearch() => ClearSearchBox?.Invoke();

    /// <summary>
    /// Whether the empty list also offers Import .kdbx: the vault holds no item at all, as a new one
    /// does, so the first thing a KeePass user may want is to bring their database in.
    /// </summary>
    internal bool ListEmptyOffersImport => TotalCount == 0 && !ListEmptyOffersClearSearch && Place == ItemPlace.All;

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
                OnPropertyChanged(nameof(ListEmptyOffersClearSearch));
                OnPropertyChanged(nameof(ListEmptyOffersImport));
                // A new query is a new question, so an entry held in the list by the last one stops
                // being held by this one.
                _pinned = null;
                ReturnToList();
                Filter();
                ChooseFirstMatchForHidden();
            }
        }
    }

    /// <summary>The pane shows only what the list does, so a query that hides the chosen item chooses its first match instead, and closes the pane when nothing matches.</summary>
    private void ChooseFirstMatchForHidden()
    {
        if (_selection is null || Rows.Any(IsSelected))
        {
            return;
        }

        if (!IsAdding && Rows.Count > 0)
        {
            MoveSelection(Rows[0].Name, SessionOf(Rows[0]));
        }
        else
        {
            Select(null);
        }
    }

    /// <summary>Return in the search: the first match, unless the arrows or the query already chose one the list shows.</summary>
    internal void ChooseFirstMatch()
    {
        if (Selected is null && Rows.Count > 0)
        {
            Selected = Rows[0];
        }
    }

    /// <summary>↓ or ↑ in the search moves the choice through the matches, starting at the first, as Spotlight's highlight moves.</summary>
    /// <param name="by">1 for the next match, -1 for the one before.</param>
    internal void MoveChoice(int by)
    {
        if (Rows.Count == 0)
        {
            return;
        }

        var at = Selected is { } chosen ? Rows.ToList().IndexOf(chosen) : -1;
        Selected = Rows[at < 0 ? 0 : Math.Clamp(at + by, 0, Rows.Count - 1)];
    }

    /// <summary>A search asks for the list, so a new item's form holding nothing to lose gives way to it; the shell has already asked about one that does.</summary>
    private void ReturnToList()
    {
        if (IsAdding && PaneDraft is null)
        {
            NewItem = null;
        }
    }

    /// <summary>The pane shows only what the list does, so the place just chosen closes an item it hid.</summary>
    private void ClosePaneTheListHid()
    {
        if (_selection is not null && !Rows.Any(IsSelected))
        {
            Select(null);
        }
    }

    /// <summary>The selected row, or null.</summary>
    /// <remarks>
    /// <para>
    /// <b>Held as the name that reaches the item rather than as the row object, so what the list does to its rows
    /// does not decide the selection.</b> The list writes null back into this property whenever the
    /// row it had selected leaves <see cref="Rows"/>. A null from the list is therefore ignored;
    /// <see cref="Select"/> is how the selection is deliberately dropped or moved: by a query or place that
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
        get => _selection is not null && !IsAdding
            ? Rows.FirstOrDefault(IsSelected)
            : null;
        set
        {
            // The list writes the row it was just told to show, which is no choice to act on.
            if (value is not { } row || (!IsAdding && IsSelected(row)))
            {
                return;
            }

            if (PaneDraft is { } draft && AskDiscard is { } ask)
            {
                ask(draft, () => MoveSelection(row.Name, SessionOf(row)), () => OnPropertyChanged(nameof(Selected)));
                return;
            }

            MoveSelection(row.Name, SessionOf(row));
        }
    }

    private bool _changeHeldOff;

    private void MoveSelection(EntryName name, AppVaultSession vault)
    {
        if (IsAdding)
        {
            CancelAdd();
        }

        Select(name, vault);
        ShowHeldChange();
    }

    /// <summary>How this screen asks "Discard …?" before choosing a row loses a draft; the shell's, or none to lose it unasked.</summary>
    internal Action<string, Action, Action?>? AskDiscard { get; init; }

    /// <inheritdoc/>
    public string? UnsavedDraft => PaneDraft;

    // What choosing another row would lose.
    private string? PaneDraft => _newItem?.UnsavedDraft ?? OrganizeDraft() ?? Detail?.UnsavedDraft;

    internal Func<string?> NewItemDraft => () => _newItem?.UnsavedDraft;

    internal Func<string?> OrganizeDraft => () =>
        IsOrganizing && Selected is { } row && DraftTitle.Trim() != row.Title ? "this new name" : null;

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

    /// <summary>What the confirmation asks, which only a vault with its Trash turned off asks, since there a delete is for good.</summary>
    internal string DeletePrompt =>
        Selected is { } row ? $"Delete {row.DisplayName} for good?" : string.Empty;

    /// <summary>Whether the open item's Copy password holds the view's amber: not while a form here shows a primary of its own, since a view has one amber element.</summary>
    internal bool CopyPasswordIsPrimary =>
        !IsAdding && !IsOrganizing && _detail is not { HasOwnPrimary: true };

    /// <summary>The list's sorts: each of its columns, and which way the one in force runs.</summary>
    internal IReadOnlyList<SortChoice> SortChoices =>
    [
        .. Table.Columns
            .Select(column => new SortChoice(
                column.Key,
                column.Name,
                Table.HeadName(column.Key),
                Table.Order is { AsRowsArrive: false } order && order.Column == column.Key ? (order.Descending ? "arrow-down" : "arrow-up") : null)),
    ];

    /// <summary>A sort's choice from the list's header: its column, and again to reverse.</summary>
    [RelayCommand]
    private void SortBy(string column) => Table.Press(column);

    /// <summary>The way back from an item that stands in the list's place, asking first about a draft it would lose.</summary>
    [RelayCommand]
    private void CloseItem()
    {
        if (PaneDraft is { } draft && AskDiscard is { } ask)
        {
            ask(draft, DropPane, static () => { });
            return;
        }

        DropPane();
    }

    private void DropPane()
    {
        NewItem = null;
        Select(null);
    }

    /// <summary>Find asks for the search, so a comparison gives way and, where the detail stands in the list's place, the item closes too.</summary>
    /// <param name="listIsHidden">Whether the view is showing the detail in the list's place.</param>
    internal void MakeSearchReachable(bool listIsHidden)
    {
        if (listIsHidden)
        {
            CloseItem();
        }

        ReturnToList();
    }

    /// <summary>Whether an item or the new-item form stands in the detail's place, so a width that cannot hold both shows it instead of the list.</summary>
    internal bool HasOpenPane => _detail is not null;

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
                previous.KindChanged -= OnKindChanged;
            }

            if (value is not null)
            {
                value.PropertyChanged += OnDetailChanged;
                value.KindChanged += OnKindChanged;
            }

            if (SetProperty(ref _detail, value))
            {
                OnPropertyChanged(nameof(CopyPasswordIsPrimary));
                OnPropertyChanged(nameof(HasOpenPane));
                OnPropertyChanged(nameof(ShowsPlaceholder));
                // Disposed on the way out, not left to a collection: it holds a username, a URL and
                // a notes field read from an open vault, and the lock has to mean something.
                previous?.Dispose();
            }
        }
    }

    /// <summary>Whether the pane's place says to choose an entry: nothing is open and nothing is being made.</summary>
    internal bool ShowsPlaceholder => _detail is null && !IsAdding && Rows.Count > 0;

    /// <summary>A calm sentence when something did not work, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    internal partial string? Error { get; private set; }

    internal bool HasError => Error is not null;

    [RelayCommand]
    private void DismissError() => Error = null;

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

    /// <summary>The New item form while it is open.</summary>
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
                OnPropertyChanged(nameof(CopyPasswordIsPrimary));
                OnPropertyChanged(nameof(HasOpenPane));
                OnPropertyChanged(nameof(ShowsPlaceholder));
                OnPropertyChanged(nameof(DeletePrompt));
                BeginAddCommand.NotifyCanExecuteChanged();
                BeginAddOfKindCommand.NotifyCanExecuteChanged();
                ChooseTemplateCommand.NotifyCanExecuteChanged();
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
            OnPropertyChanged(nameof(CopyPasswordIsPrimary));
        }
    }

    /// <summary>Whether the organize form is showing for the selected entry.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CopyPasswordIsPrimary))]
    [NotifyPropertyChangedFor(nameof(OrganizeTitleProblem), nameof(HasOrganizeTitleProblem), nameof(RenameAgentNote), nameof(RenamePrompt))]
    [NotifyCanExecuteChangedFor(nameof(OrganizeCommand), nameof(ConfirmOrganizeCommand), nameof(CancelOrganizeCommand))]
    internal partial bool IsOrganizing { get; private set; }

    /// <summary>The rename form's heading, naming what it renames.</summary>
    internal string RenamePrompt => $"Rename {_detail?.DisplayTitle}";

    /// <summary>Copies the selected item beside it, under a new title, and selects the copy.</summary>
    internal RelayCommand DuplicateCommand { get; }

    /// <summary>The title being typed for the selected entry.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OrganizeTitleProblem), nameof(HasOrganizeTitleProblem))]
    internal partial string DraftTitle { get; set; } = string.Empty;

    /// <summary>Why the name typed in Rename cannot be used, or null.</summary>
    internal string? OrganizeTitleProblem =>
        !IsOrganizing ? null
        : DraftTitle.Trim().Length == 0 ? "An item needs a name."
        : EntryNameSanitizer.Sanitize(DraftTitle.Trim()) is { WasAltered: true } sanitized ? $"That is not a name keypaste will create. Try '{sanitized.Text}'."
        : null;

    internal bool HasOrganizeTitleProblem => OrganizeTitleProblem is not null;

    /// <summary>What renaming the open item can do to what agents see, said only while an attached agent sees it by its title, or null.</summary>
    /// <remarks>
    /// What an agent can see by place is globs matched against an item's group path and title (<see cref="EntryExposure"/>), so a rename
    /// can take the item out of its view; THREATS.md T-13 records that keypaste does not prevent this, so it is a line on the form, never a refusal.
    /// </remarks>
    internal string? RenameAgentNote =>
        _detail?.SeeingByName is { Count: > 0 } agents
            ? $"{string.Join(", ", agents)} sees {_detail.DisplayTitle} by its title; renaming it can hide it from {string.Join(", ", agents)}."
            : null;

    /// <summary>Says what a drag did beyond the move, which has no form to carry <see cref="RenameAgentNote"/>.</summary>
    internal Action<string, ToastTone>? Toast { get; init; }

    /// <summary>Opens the organize form for the selected entry.</summary>
    internal RelayCommand OrganizeCommand { get; }

    /// <summary>Renames the selected entry in its own group.</summary>
    internal RelayCommand ConfirmOrganizeCommand { get; }

    internal RelayCommand CancelOrganizeCommand { get; }

    /// <summary>⌘N: the form of the kind the place holds, or a login's where it holds every kind.</summary>
    internal RelayCommand BeginAddCommand { get; }

    /// <summary>Opens the form of the template it is given.</summary>
    internal RelayCommand<ItemTemplate> BeginAddOfKindCommand { get; }

    /// <summary>New's More templates… and ⇧⌘N: the grid of templates.</summary>
    internal RelayCommand ChooseTemplateCommand { get; }

    /// <summary>The pane's label column, shared by every item while the app runs.</summary>
    internal LabelColumn PaneLabels { get; init; } = new();

    /// <summary>Goes to Activity, where each agent's use of the item is told in full; the shell's.</summary>
    internal Action? ShowActivity { get; init; }

    [RelayCommand]
    private void OpenActivity() => ShowActivity?.Invoke();

    /// <summary>The open vaults a new item can be saved in, the one in view first; the shell gives them, and alone the vault in view is the one.</summary>
    internal Func<IReadOnlyList<SaveInChoice>>? SaveInPlaces { get; init; }

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
            _sorted = null;
            Rows = [];
            Select(null);
            Forget();

            // A half-entered password is as much a secret as a stored one, and the screen is about
            // to be disposed anyway. Closing the form here disposes it on whichever path runs.
            NewItem = null;
            return;
        }

        var wanted = _selection;
        var wantedVault = _selectionVault;

        _all = [.. RowsOf(_session, vault), .. _alsoListed.SelectMany(other => other.Unlocked is { } open ? RowsOf(other, open) : [])];
        _sorted = null;
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(ListEmptyOffersImport));
        OnPropertyChanged(nameof(ListEmptyNote));

        // The vault changed underneath, so an answer about the old one is not an answer about this.
        _matchedQuery = null;

        Filter();

        SelectIfListed(wanted, wantedVault);
        TellPaneAboutItsName(Detail);
        ApplyActivity();
    }

    /// <summary>One vault's items as the list's rows, naming the vault where the list holds several; names and times, never a value.</summary>
    private List<EntryRow> RowsOf(AppVaultSession session, Vault vault)
    {
        IReadOnlyList<VaultEntry> entries;
        IReadOnlyDictionary<string, DateTimeOffset> modified;
        HashSet<(string, string)> favorites;
        Dictionary<(string GroupPath, string Title), IReadOnlyList<string>> tags;

        try
        {
            entries = vault.ReadEntries();
            modified = vault.ReadModifiedTimes();
            tags = TagsByName(vault);
            favorites = [.. tags.Where(tagged => tagged.Value.Contains(ItemPlaces.FavoriteTag, StringComparer.Ordinal)).Select(tagged => tagged.Key)];
        }
        catch (ObjectDisposedException) when (!ReferenceEquals(session, _session))
        {
            // Another listed vault locked as it was read; its rows go with it.
            return [];
        }

        var tells = SameNameTells.Of(entries);
        HashSet<string> heldTwice = [.. entries.GroupBy(entry => entry.Uuid!, StringComparer.Ordinal).Where(holders => holders.Count() > 1).Select(holders => holders.Key)];
        var vaultName = ListsSeveralVaults ? _titleOf(session) : string.Empty;

        return [.. entries
            .Select(entry => new EntryRow(entry.Title, entry.GroupPath)
            {
                Uuid = entry.Uuid!,
                Vault = session,
                VaultName = vaultName,
                SharesItsUuid = heldTwice.Contains(entry.Uuid!),
                SharedNameTell = tells.Of(EntryName.Of(entry) with { Uuid = entry.Uuid }),
                Kind = EntryKinds.Of(entry, tags.GetValueOrDefault((entry.GroupPath, entry.Title), [])),
                Username = entry.Username,
                Url = entry.Url,
                Modified = modified.TryGetValue(entry.Uuid!, out var changed) ? changed : null,
                IsFavorite = favorites.Contains((entry.GroupPath, entry.Title)),
                InProject = tags.GetValueOrDefault((entry.GroupPath, entry.Title), []).Any(tag => ProjectTag.Read(tag).Kind == ProjectTagKind.Member),
                ChangedText = modified.TryGetValue(entry.Uuid!, out var at) ? UseText.Changed(at) : string.Empty,
            })];
    }

    /// <summary>Reads the vault again after a merge or a terminal's edit, and the open item too, which <see cref="Reload"/> leaves to the pane that made the change; a draft or an edit holds this off until its form closes.</summary>
    /// <remarks>A moved or renamed item already has a new pane, and a removed one none.</remarks>
    internal void ShowChangeFromElsewhere()
    {
        if (UnsavedDraft is not null || Detail is { IsEditing: true })
        {
            _changeHeldOff = true;
            return;
        }

        _changeHeldOff = false;
        EndReveals?.Invoke();

        var open = Detail;
        Reload();

        if (open is not null && ReferenceEquals(open, Detail))
        {
            open.ReadAgain();
        }
    }

    private void ShowHeldChange()
    {
        if (_changeHeldOff)
        {
            ShowChangeFromElsewhere();
        }
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
        var lastUseMoved = false;

        // Rows holds rows of _all, or a query's copies of them, and each object takes the picture once.
        HashSet<EntryRow> applied = new(ReferenceEqualityComparer.Instance);

        // The picture is this vault's own; another listed vault's items keep no dot.
        foreach (var row in _all.Concat(Rows).Where(IsHome))
        {
            if (!applied.Add(row))
            {
                continue;
            }

            var lastUsed = row.LastUsedTicks;
            row.Apply(picture, now);
            lastUseMoved |= lastUsed != row.LastUsedTicks;
        }

        Detail?.Apply(picture);

        if (!lastUseMoved)
        {
            return;
        }

        if (_sortedBy?.Column == TableStates.LastUsed)
        {
            _sorted = null;
        }

        if (Table.Order?.Column == TableStates.LastUsed && Listed() is var listed && !listed.Select(row => row.Uuid).SequenceEqual(Rows.Select(row => row.Uuid), StringComparer.Ordinal))
        {
            Rows = listed;
        }
    }

    // A sort is no question about the vault: the selection, the pane and whatever an edit is holding in the list stay.
    private void OnOrderChanged()
    {
        Rows = Listed();
        OnPropertyChanged(nameof(SortChoices));
    }

    /// <summary>
    /// Drops the query, the result and everything typed into a form.
    /// </summary>
    /// <remarks>
    /// A query is what somebody was looking for in this vault, and a result is a list of its entry
    /// names; neither belongs to a locked app. Called from <see cref="Reload"/> when the vault is
    /// locked and from <see cref="Dispose"/>, because the lock has to hold on whichever path runs.
    /// </remarks>
    private void Forget()
    {
        _search = string.Empty;
        _matchedQuery = string.Empty;
        _matches = [];
        _pinned = null;
        DraftTitle = string.Empty;
        IsOrganizing = false;

        OnPropertyChanged(nameof(Search));
        OnPropertyChanged(nameof(ListEmptyNote));
        OnPropertyChanged(nameof(ListEmptyOffersClearSearch));
        OnPropertyChanged(nameof(ListEmptyOffersImport));
    }

    /// <summary>Nothing derived from the vault outlives this.</summary>
    public void Dispose()
    {
        foreach (var listed in ListedVaults)
        {
            listed.Edited -= OnVaultEdited;
        }

        Table.OrderChanged -= OnOrderChanged;

        if (_activity is not null)
        {
            _activity.Changed -= OnActivity;
        }

        _all = [];
        _sorted = null;
        Rows = [];
        Select(null);
        Forget();
        Detail = null;
        Offer(null, null);
        NewItem = null;
    }

    private EntryDetailViewModel? Build(EntryName name, AppVaultSession session)
    {
        VaultEntry? found;
        try
        {
            // By the row's item, not its path or its title: two rows can share either, and building
            // the pane from the wrong one is what made its copy button serve a secret nobody selected.
            found = session.Unlocked?.Find(name);
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
        var detail = new EntryDetailViewModel(session, _clipboard, entry, message => Error = message, moved => Reselect(moved, session), _web, _post, _picker) { AskDiscard = AskDiscard, FormClosed = ShowHeldChange };
        TellPaneAboutItsName(detail);
        return detail;
    }

    /// <summary>Tells the pane whether another item has its name now, which a write elsewhere can change while it stays open.</summary>
    private void TellPaneAboutItsName(EntryDetailViewModel? detail)
    {
        if (detail is not null && _all.FirstOrDefault(row => row.Is(detail.Name) && SameVault(row, _selectionVault)) is { } row)
        {
            detail.ShowSharedName(row.SharesItsName, row.SharedNameTell is { Length: 0 });
        }
    }

    /// <summary>Reads the list again and lands on the entry a restore left behind, unless a merge took it to Trash.</summary>
    /// <remarks>
    /// An <see cref="EntryRow"/> is a record, so reselecting an equal row changes nothing.
    /// </remarks>
    private void OnKindChanged(EntryName name) => Reselect(name, _session);

    private void Reselect(EntryName name, AppVaultSession vault)
    {
        Reload();
        SelectIfListed(name, vault);
        ShowHeldChange();
    }

    /// <summary>Selects what the vault still holds, and holds it in the list until the next query or place, because an edit or another program's save is not the person hiding it.</summary>
    private void SelectIfListed(EntryName? name, AppVaultSession? vault)
    {
        var row = _all.FirstOrDefault(row => row.Is(name) && SameVault(row, vault));
        Select(row?.Name, row is null ? null : SessionOf(row));

        if (_selection is { } selected && !Rows.Any(IsSelected))
        {
            Pin(selected, _selectionVault);
            Filter();
        }
    }

    private void Pin(EntryName? name, AppVaultSession? vault)
    {
        _pinned = name;
        _pinnedVault = vault;
    }

    /// <summary>Narrows the list to the place and the current query.</summary>
    /// <remarks>
    /// Runs on every place change and every reselect as well as every keystroke, so the core search
    /// is cached against the query it answered: only a changed query costs a read of the vault.
    /// </remarks>
    private void Filter() => Rows = Listed();

    private List<EntryRow> Listed()
    {
        var query = Search.Trim();

        Match(query);

        List<EntryRow> rows = [];

        foreach (var row in Sorted().Where(row => Place == ItemPlace.Favorites ? row.IsFavorite : ItemPlaces.Holds(Place, row.Kind)))
        {
            if (query.Length == 0)
            {
                rows.Add(row);
            }
            else if (_matches.TryGetValue((SessionOf(row), row.Uuid, row.Name), out var match))
            {
                rows.Add(row with { Fields = match.Fields, Tags = match.Tags });
            }
        }

        // An entry just renamed out of its own result stays where the person is looking at it. It is
        // held only until the query changes, which is when they asked a different question.
        if (_pinned is { } pin && !rows.Any(row => row.Is(pin) && SameVault(row, _pinnedVault)))
        {
            if (_all.FirstOrDefault(row => row.Is(pin) && SameVault(row, _pinnedVault)) is { } held)
            {
                rows.Insert(0, held);
            }
        }

        return rows;
    }

    /// <summary>Every row under the order in force, sorted once per reload, order or change of a sorted last use rather than per keystroke.</summary>
    /// <remarks>The sort is stable and no key reads what a query's copy changes, so filtering this keeps the order sorting the filtered rows gives.</remarks>
    private IReadOnlyList<EntryRow> Sorted()
    {
        if (_sorted is null || _sortedBy != Table.Order)
        {
            _sortedBy = Table.Order;
            _sorted = [.. TableSorting.Sorted(_all, _sortedBy, SortValueOf, TieBetween)];
        }

        return _sorted;
    }

    /// <summary>What a row shows in a column, so the list reads in the order its words do. Only names and times: a row holds no value to read.</summary>
    private static SortValue SortValueOf(EntryRow row, string column) => column switch
    {
        TableStates.Title => new(row.DisplayTitle),
        TableStates.Username => new(row.DisplayUsername),
        TableStates.Url => new(row.DisplayUrl),
        TableStates.Modified => SortValue.At(row.ModifiedTicks),
        _ => SortValue.At(row.LastUsedTicks),
    };

    private static int TieBetween(EntryRow left, EntryRow right)
    {
        var compared = TableSorting.CompareText(left.DisplayTitle, right.DisplayTitle);
        compared = compared != 0 ? compared : TableSorting.CompareText(left.GroupPath, right.GroupPath);
        compared = compared != 0 ? compared : string.CompareOrdinal(left.Title, right.Title);
        compared = compared != 0 ? compared : string.CompareOrdinal(left.GroupPath, right.GroupPath);
        compared = compared != 0 ? compared : TableSorting.CompareText(left.DisplayUsername, right.DisplayUsername);
        return compared != 0 ? compared : string.CompareOrdinal(left.Uuid, right.Uuid);
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

        _matchedQuery = query;
        _matches = [];

        // A damaged file can hold one UUID under two names, or both under one, where the later match stands for both rows.
        foreach (var match in vault.Search(query))
        {
            _matches[(_session, match.Uuid, match.Name)] = match;
        }

        foreach (var other in _alsoListed)
        {
            try
            {
                foreach (var match in other.Unlocked?.Search(query) ?? [])
                {
                    _matches[(other, match.Uuid, match.Name)] = match;
                }
            }
            catch (ObjectDisposedException)
            {
                // That vault locked as it was searched.
            }
        }
    }

    /// <summary>Moves the selection, or drops it, and rebuilds the pane when it moved.</summary>
    /// <remarks>
    /// <b>Only another item or a changed name rebuilds the pane.</b> A reload that lands on the same entry —
    /// which is every reload after an edit, a restore or an organize elsewhere — must leave the
    /// pane object alone: it keeps its own values current, and its history section is open because
    /// somebody opened it. Replacing it would dispose the pane in the middle of the interaction
    /// that asked for the reload.
    /// </remarks>
    private void Select(EntryName? name, AppVaultSession? vault = null)
    {
        vault = name is null ? null : vault ?? _session;
        var moved = _selection?.Uuid != name?.Uuid || _selection != name || !ReferenceEquals(_selectionVault, vault);
        _selection = name;
        _selectionVault = vault;

        if (moved)
        {
            // The add form stands in the pane's place, so choosing an entry to look at closes it.
            if (name is not null && IsAdding)
            {
                CancelAdd();
            }

            Detail = name is null ? null : Build(name, vault!);

            if (Detail is not null && _activity?.Current is { } picture)
            {
                Detail.Apply(picture);
            }
        }

        IsConfirmingDelete = false;
        IsOrganizing = false;

        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(ItemVaultTitle));
        OnPropertyChanged(nameof(DeletePrompt));
        DeleteCommand.NotifyCanExecuteChanged();
        OrganizeCommand.NotifyCanExecuteChanged();
        DuplicateCommand.NotifyCanExecuteChanged();
    }

    private void BeginAdd() => Open(ItemPlaces.TemplateOf(Place) ?? ItemTemplate.Login);

    private void BeginAddOfKind(ItemTemplate kind) => Open(kind);

    private void ChooseTemplate() => Open(null);

    /// <summary>Opens New item in the vault in view: the template's form, or the grid when none is given.</summary>
    private void Open(ItemTemplate? template)
    {
        if (_session.Unlocked is null)
        {
            Error = "The vault is locked. Unlock it, then try again.";
            return;
        }

        Notice = null;
        Error = null;
        NewItem = new NewItemViewModel(SaveInChoices(), _clipboard, template);
    }

    private IReadOnlyList<SaveInChoice> SaveInChoices()
    {
        var places = SaveInPlaces?.Invoke() ?? [];
        var inView = places.FirstOrDefault(place => ReferenceEquals(place.Session, _session)) ?? new SaveInChoice(_session, AccountSection.LocalTitle, VaultTitle);
        return [inView, .. places.Where(place => !ReferenceEquals(place.Session, _session) && place.Session.IsUnlocked)];
    }

    [RelayCommand(CanExecute = nameof(IsAdding))]
    private void CancelAdd()
    {
        NewItem = null;
        Error = null;
        ShowHeldChange();
    }

    private bool CanCreate => _newItem is { HasTitle: true };

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void ConfirmAdd()
    {
        if (_newItem is not { } form || form.Create() is not { } name)
        {
            return;
        }

        var savedIn = form.SaveIn;
        NewItem = null;
        Error = null;
        Reload();
        Selected = Rows.FirstOrDefault(row => row.Name == name && SameVault(row, savedIn.Session));

        // An item saved in a vault this list leaves out, or of a kind its place does not list, is not in view to select.
        if (Selected is null)
        {
            Toast?.Invoke($"Saved {EntryNameSanitizer.Sanitize(name.Title).Text} in {savedIn.Vault}.", ToastTone.Success);
        }

        ShowHeldChange();
    }

    private void DeleteOrAsk()
    {
        if (Selected is { } row && SessionOf(row).Unlocked?.RecyclesDeletedEntries == true)
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

        var session = SessionOf(row);
        var write = session.Write(
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
            Error = "That item is not in this vault any more. Choose another.";
            Reload();
            return;
        }

        var (outcome, recycled) = write.Value;
        var name = row.DisplayName;

        Select(null);
        Reload();

        // Last, because Reload and the selection it clears both drop a pending offer.
        Offer(
            outcome == DeletionOutcome.Recycled
                ? new Undoable(
                    vault => PutBack(vault, recycled),
                    () => Reselect(row.Name, session),
                    Restores: true,
                    Vault: session)
                : null,
            outcome == DeletionOutcome.Recycled
                ? $"Moved {name} to Trash"
                : $"Deleted {name}.");
    }

    private static bool PutBack(Vault vault, RecycledEntryId recycled) =>
        vault.RestoreRecycled(recycled) is RestoreOutcome.Restored or RestoreOutcome.RestoredToRoot;

    /// <summary>Reverses the last delete, rename or duplicate through its inverse write.</summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_undo is not { } last)
        {
            return;
        }

        var write = (last.Vault ?? _session).Write(last.Reverse, reversed => reversed);

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
        IsOrganizing = true;
        Error = null;
    }

    private void CloseOrganize()
    {
        IsOrganizing = false;
        DraftTitle = string.Empty;
        Error = null;
        ShowHeldChange();
    }

    /// <summary>
    /// Renames the selected entry in the group the file holds it in, in one write.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Vault.Relocate"/> validates the whole target before it mutates anything, so a refusal leaves the
    /// open vault exactly as it was.
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

        Organize(row, TargetOf(row));
    }

    /// <summary>The item's new name in its own group: a title the person left as it opened stays as a merge since has made it.</summary>
    private EntryName TargetOf(EntryRow row)
    {
        var title = DraftTitle.Trim();
        VaultEntry? now;

        try
        {
            now = _session.Unlocked?.Find(row.Name);
        }
        catch (VaultException)
        {
            now = null;
        }

        return new EntryName(
            now?.GroupPath ?? row.GroupPath,
            now is not null && string.Equals(title, row.Title, StringComparison.Ordinal) ? now.Title : title);
    }

    /// <summary>Whether no form or draft on this screen would be left behind by a drag taking an item or a file to its item.</summary>
    internal bool HoldsNoForm => UnsavedDraft is null && !IsAdding && !IsOrganizing && Detail is not { IsEditing: true };

    private bool Organize(EntryRow row, EntryName target)
    {
        var write = _session.Write(
            vault => (Outcome: vault.Relocate(row.Name, target, out var relocated), Moved: relocated),
            relocation => relocation.Outcome is OrganizeOutcome.Renamed or OrganizeOutcome.Moved or OrganizeOutcome.RenamedAndMoved);

        if (write.Problem("make your change again") is { } problem)
        {
            Error = problem;
            return false;
        }

        if (write.Outcome == WriteOutcome.NothingToSave)
        {
            Error = Refusal(write.Value.Outcome, target);
            return false;
        }

        var moved = write.Value.Moved!;

        CloseOrganize();

        // Renaming one of a pair leaves a name core will not give a second item, so that change has no way back to offer.
        Offer(
            _session.Unlocked?.RelocationRefusal(moved, row.Name) is null
                ? new Undoable(
                    vault => vault.Relocate(moved, row.Name, out _) is OrganizeOutcome.Renamed or OrganizeOutcome.Moved or OrganizeOutcome.RenamedAndMoved,
                    () => FollowRelocated(row.Name))
                : null,
            Did(moved));
        FollowRelocated(moved);
        return true;
    }

    private void FollowRelocated(EntryName moved)
    {
        // Held in the result even when its new name no longer answers the query that found it, so
        // the entry does not vanish from under somebody at the moment they renamed it.
        Pin(moved, _session);
        Reselect(moved, _session);
    }

    /// <summary>Closes whatever else is open, so one form is on screen at a time.</summary>
    private void CloseOtherForms()
    {
        Notice = null;
        NewItem = null;
        IsConfirmingDelete = false;
        IsOrganizing = false;
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

        var session = SessionOf(row);
        var write = session.Write(
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
                : "That item is not in this vault any more. Choose another.";
            return;
        }

        Error = null;
        Pin(made, session);
        Reselect(made, session);
        Offer(
            new Undoable(vault => vault.RemoveEntry(made, out _) != DeletionOutcome.NothingMatched, () => Reselect(row.Name, session), Vault: session),
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

    /// <summary>What the app says a rename did.</summary>
    private static string Did(EntryName renamed) => $"Renamed to {EntryNameSanitizer.Sanitize(renamed.Title).Text}.";

    /// <summary>
    /// What the app says when core refused an entry a new name.
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
                return "That item is not in this vault any more. Choose another.";

            case OrganizeOutcome.DestinationUnchanged:
                return "That is already its name. Type another, or cancel.";

            case OrganizeOutcome.DestinationOccupied:
                return "Another item beside it already has this name. Choose another name.";

            case OrganizeOutcome.DestinationAmbiguous:
                return "That would leave two items answering to one path, and keypaste could not tell them apart afterwards. Choose another name.";

            case OrganizeOutcome.NameRefused:
                VaultNameRules.IsValidTitle(target.Title, out var why);
                return $"keypaste will not use that title: {why}. Choose another.";

            default:
                return "That change could not be made. Check the name, then try again.";
        }
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

    private sealed record Undoable(Func<Vault, bool> Reverse, Action? Show = null, bool Restores = false, AppVaultSession? Vault = null);
}
