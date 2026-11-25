using System.Globalization;
using Keypaste.App.Navigation;
using Keypaste.App.Resources;
using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Ipc;

namespace Keypaste.App.ViewModels;

/// <summary>The rail, the sidebar and the way they lay out at each width.</summary>
internal sealed partial class ShellViewModel
{
    private readonly Dictionary<AppVaultSession, VaultSection> _sections = [];
    private VaultSection _itemsVault;
    private ItemPlace _place;
    private object? _entriesRow;
    private bool _allAccounts;
    private bool _rebuildingSidebar;
    private string _lockStates = string.Empty;
    private (VaultSection Vault, string Path)? _creating;

    /// <summary>A picker that asks nothing, for a shell composed without one.</summary>
    private sealed class NoFilePicker : IVaultFilePicker
    {
        internal static NoFilePicker Instance { get; } = new();

        public Task<string?> PickExistingAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickNewAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickExportDestinationAsync(string suggestedName) => Task.FromResult<string?>(null);

        public Task<string?> PickKeyfileAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickDotEnvAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
    }

    /// <summary>The vaults open in the app, whose sections the sidebar lists; null where only the vault in view is open.</summary>
    internal OpenVaults? Vaults
    {
        get => _vaults;
        init
        {
            _vaults = value;

            // The open vaults watch the vault in view's file once they hold it.
            if (OpenOf(_session) is not null)
            {
                _ownWatch?.Dispose();
                _ownWatch = null;
            }

            Count();
        }
    }

    private readonly OpenVaults? _vaults;

    /// <summary>The local account and its vaults, the one in view first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsAccounts))]
    internal partial AccountSection Account { get; private set; }

    /// <summary>Whether the sidebar draws the account and a section for each vault: only once the account holds a second vault.</summary>
    internal bool ShowsAccounts => Account.Vaults.Count > 1;

    /// <summary>The vault whose places Items, Environments and Trash show.</summary>
    internal VaultSection VaultInView => _itemsVault;

    /// <summary>Whether the person hid the sidebar with View › Hide sidebar; it stays hidden at every width and across locks.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsSidebar), nameof(ContentIsTight))]
    internal partial bool SidebarHidden { get; private set; }

    /// <summary>Whether the window is under <see cref="Views.WindowChrome.NarrowBelow"/>, where the sidebar gives way to the rail, the list and the item.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsSidebar), nameof(SidebarOverlays), nameof(ContentIsTight))]
    internal partial bool IsNarrow { get; set; }

    /// <summary>Whether View › Show sidebar opened the sidebar over the content of a narrow window.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsSidebar), nameof(SidebarOverlays), nameof(ContentIsTight))]
    private partial bool SidebarOpenedOver { get; set; }

    /// <summary>Whether Items, one of its vault places, is in view: the places the sidebar lists.</summary>
    internal bool ShowsItemsPlace => Destinations.PlaceOf(_current).Kind == DestinationKind.Entries;

    /// <summary>Whether the sidebar is on the screen: beside the content on Items unless hidden, or over it in a narrow window.</summary>
    internal bool ShowsSidebar => ShowsItemsPlace && (IsNarrow ? SidebarOpenedOver : !SidebarHidden);

    /// <summary>Whether the sidebar lies over the content rather than beside it.</summary>
    internal bool SidebarOverlays => ShowsItemsPlace && IsNarrow && SidebarOpenedOver;

    /// <summary>The window's width, in the design's pixels, which <see cref="ContentIsTight"/> is judged from.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContentIsTight))]
    internal partial double WindowWidth { get; set; }

    /// <summary>Whether the content is under <see cref="Views.WindowChrome.ContentTightBelow"/> wide, where the log drops a column, whatever takes the rest of the window.</summary>
    internal bool ContentIsTight =>
        WindowWidth - Views.WindowChrome.RailWidth - (ShowsSidebar && !SidebarOverlays ? Views.WindowChrome.SidebarWidth : 0) < Views.WindowChrome.ContentTightBelow;

    /// <summary>View › Hide sidebar and Show sidebar: the person's kept choice in a wide window, the sidebar over the content in a narrow one.</summary>
    [RelayCommand]
    internal void ToggleSidebar()
    {
        if (IsNarrow)
        {
            SidebarOpenedOver = !SidebarOpenedOver;

            if (SidebarOpenedOver && !ShowsItemsPlace)
            {
                Current = Destinations.Of(DestinationKind.Entries);
            }

            return;
        }

        SidebarHidden = !SidebarHidden;

        if (!SidebarHidden && !ShowsItemsPlace)
        {
            Current = Destinations.Of(DestinationKind.Entries);
        }
    }

    /// <summary>Opens the sidebar over the content of a narrow window, for an item held over the rail while dragged.</summary>
    internal void OpenSidebarOverlay() => SidebarOpenedOver = IsNarrow;

    /// <summary>Closes a sidebar lying over the content: a press outside it, a choice in it, or Escape.</summary>
    internal void CloseSidebarOverlay() => SidebarOpenedOver = false;

    partial void OnIsNarrowChanged(bool value) => SidebarOpenedOver = false;

    partial void OnSidebarHiddenChanged(bool value) => SidebarOpenedOver = false;

    /// <summary>The rail's places in order: Items, Agents, Security, Access kept for later, and Settings at its foot.</summary>
    internal IReadOnlyList<NavItem> RailPlaces { get; }

    /// <summary>Security on the rail, whose badge counts what needs review.</summary>
    internal NavItem SecurityPlace => RailPlaces.Single(item => item.Destination.Kind == DestinationKind.Recommendations);

    /// <summary>The rail's row for the place <see cref="Current"/> is, or is under.</summary>
    internal NavItem? SelectedRailPlace
    {
        get => RailPlaces.FirstOrDefault(item => item.Destination == Destinations.PlaceOf(_current));
        set
        {
            if (value is null || value.IsLater)
            {
                return;
            }

            if (value.Destination.Kind == DestinationKind.Entries && ShowsItemsPlace)
            {
                return;
            }

            Current = value.Destination;
        }
    }

    /// <summary>
    /// The sidebar in reading order: Favorites, then the one vault's places, or, with several vaults, All Accounts, the local account's
    /// heading and each vault with its places; the projects under a vault's Environments while they are in view.
    /// </summary>
    /// <remarks>One list, so the arrow keys walk the sidebar as a person reads it; Left and Right fold a vault.</remarks>
    [ObservableProperty]
    internal partial IReadOnlyList<object> SidebarRows { get; private set; } = [];

    /// <summary>The vault's projects, by name, with how many entries each holds.</summary>
    internal IReadOnlyList<ProjectRow> Projects
    {
        get => _projects;
        private set
        {
            if (_projects.SequenceEqual(value))
            {
                return;
            }

            _projects = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasProjects));
            RebuildSidebar();
        }
    }

    internal bool HasProjects => _projects.Count > 0;

    /// <summary>The sidebar row for where the shell is, or null on a place the sidebar does not list. Choosing a row goes there.</summary>
    internal object? SelectedSidebarRow
    {
        get
        {
            if (!ShowsItemsPlace)
            {
                return null;
            }

            switch (_current.Kind)
            {
                case DestinationKind.EnvSets:
                    return (object?)(_openProject is { } open ? SidebarRows.OfType<ProjectRow>().FirstOrDefault(row => row.Name == open) : null)
                        ?? PlaceRowOf(_itemsVault, ItemPlace.Environments);
                case DestinationKind.Trash:
                    return PlaceRowOf(_itemsVault, ItemPlace.Trash);
            }

            if (_itemsVault.IsLocked && !_allAccounts)
            {
                return SidebarRows.OfType<VaultRow>().FirstOrDefault(row => row.Vault == _itemsVault);
            }

            if (_place == ItemPlace.Favorites)
            {
                return SidebarRows.OfType<PlaceRow>().FirstOrDefault(row => row.IsTop && row.Place == ItemPlace.Favorites);
            }

            if (_place != ItemPlace.All)
            {
                return PlaceRowOf(_itemsVault, _place);
            }

            return StillListed(_entriesRow)
                ?? (_allAccounts ? SidebarRows.OfType<PlaceRow>().FirstOrDefault(row => row.IsTop && row.Place == ItemPlace.All) : PlaceRowOf(_itemsVault, ItemPlace.All));
        }
        set
        {
            // Replacing the rows makes the list re-select, and write back, whichever row it had.
            if (_rebuildingSidebar || value is null || Equals(value, SelectedSidebarRow))
            {
                return;
            }

            switch (value)
            {
                case PlaceRow { Vault: null } top:
                    ShowPlace(_itemsVault, top.Place, top, allAccounts: true);
                    break;
                case PlaceRow { Place: ItemPlace.Environments, Vault: { } vault }:
                    ShowVaultScreen(vault, DestinationKind.EnvSets);
                    break;
                case PlaceRow { Place: ItemPlace.Trash, Vault: { } vault }:
                    ShowVaultScreen(vault, DestinationKind.Trash);
                    break;
                case PlaceRow { Vault: { } vault } place:
                    ShowPlace(vault, place.Place, place);
                    break;
                case VaultRow vaultRow:
                    ShowPlace(vaultRow.Vault, ItemPlace.All, vaultRow);
                    break;
                case ProjectRow project:
                    OpenProject(project.Name);
                    break;
                case NavItem item:
                    SelectedRailPlace = item;
                    break;
            }

            if (value is not VaultRow)
            {
                CloseSidebarOverlay();
            }
        }
    }

    /// <summary>Locks one vault and leaves every other open; with only the vault in view open, that is the app's lock.</summary>
    internal Task LockAloneAsync(VaultSection vault)
    {
        ArgumentNullException.ThrowIfNull(vault);

        return OpenOf(vault.Session) is { } open
            ? Vaults!.LockAloneAsync(open)
            : vault.Session.LockAsync(VaultLockReason.Manual);
    }

    /// <summary>Locks every open vault together, as Lock, the idle lock and Ctrl/Cmd+L do.</summary>
    private Task LockAllAsync() => Vaults?.LockAllAsync(VaultLockReason.Manual) ?? _session.LockAsync(VaultLockReason.Manual);

    /// <summary>Add vault…: opens an existing vault into the local account, its own claim, pipe and rules, and shows its unlock form.</summary>
    [RelayCommand]
    private async Task OpenVaultAsync()
    {
        CloseSidebarOverlay();

        if (_picker is null || Vaults is null || await _picker.PickExistingAsync().ConfigureAwait(true) is not { } path || _disposed)
        {
            return;
        }

        if (!Core.KdbxHeader.IsVaultFile(path))
        {
            ShowToast("That isn't a KeePass vault. Choose a .kdbx file.", ToastTone.Warning);
            return;
        }

        if (Vaults.Holding(path) is null && Vaults.Listed.Count >= Core.Settings.LocalAccount.MaximumVaults)
        {
            ShowToast($"{AccountSection.LocalTitle} holds {Core.Settings.LocalAccount.MaximumVaults} vaults at most. Close one first.", ToastTone.Warning);
            return;
        }

        ShowVault(SectionOf(Vaults.Add(path).Session));
    }

    /// <summary>Add vault…: creates a vault where the person chooses and opens it into the local account.</summary>
    [RelayCommand]
    private async Task CreateVaultAsync()
    {
        CloseSidebarOverlay();

        if (_picker is null || Vaults is null || await _picker.PickNewAsync().ConfigureAwait(true) is not { } path || _disposed)
        {
            return;
        }

        if (Vaults.Listed.Count >= Core.Settings.LocalAccount.MaximumVaults)
        {
            ShowToast($"{AccountSection.LocalTitle} holds {Core.Settings.LocalAccount.MaximumVaults} vaults at most. Close one first.", ToastTone.Warning);
            return;
        }

        var section = SectionOf(Vaults.Add().Session);
        _creating = (section, path);
        ShowVault(section);
    }

    /// <summary>A vault's menu: closes it and takes it out of the local account; its file stays where it is.</summary>
    internal void CloseVault(VaultSection vault)
    {
        ArgumentNullException.ThrowIfNull(vault);

        if (OpenOf(vault.Session) is not { } open)
        {
            return;
        }

        Leave(() =>
        {
            if (_itemsVault == vault)
            {
                _itemsVault = SectionOf(_session);
            }

            Vaults!.Remove(open);
            Count();
            Show(_current);
        });
    }

    /// <summary>The open vault the shell was built on, whose own screens it shows and whose lock alone builds the window again on another.</summary>
    internal AppVaultSession Anchor => _session;

    /// <summary>Lists every item of an open vault's section, or shows its unlock form while it is locked.</summary>
    internal void ShowVaultOf(AppVaultSession session)
    {
        if (Account.Vaults.FirstOrDefault(vault => vault.Session == session) is { } vault)
        {
            ShowVault(vault);
        }
    }

    /// <summary>Lists every item of a vault, or shows its unlock form while it is locked.</summary>
    private void ShowVault(VaultSection vault)
    {
        Count();

        if ((SidebarRows.OfType<VaultRow>().FirstOrDefault(row => row.Vault == vault) ?? (object?)PlaceRowOf(vault, ItemPlace.All)) is { } row)
        {
            ShowPlace(vault, ItemPlace.All, row);
        }
    }

    /// <summary>
    /// What a page's list asked keypaste to show, the window already forward: a locked vault's unlock form, New login in
    /// the site's vault with the page's address filled in, or the window as it is.
    /// </summary>
    /// <param name="what">What the person pressed.</param>
    /// <param name="vaultName">The vault the list named, by its file's name, or empty for the vault in view.</param>
    /// <param name="url">The page's address for a new login, or empty.</param>
    internal void ShowAsked(BrowserShow what, string vaultName, string url)
    {
        if (_disposed || what == BrowserShow.Open)
        {
            return;
        }

        var named = vaultName.Length > 0 && Vaults?.Named(vaultName) is { } open ? SectionOf(open.Session) : null;

        if (what == BrowserShow.Unlock)
        {
            if (named is { IsLocked: true })
            {
                Leave(() => ShowVault(named));
            }

            return;
        }

        var vault = named ?? VaultForScreens;

        Leave(() =>
        {
            Search = string.Empty;
            Count();

            if (vault.IsLocked)
            {
                ShowVault(vault);
                return;
            }

            ShowPlace(vault, ItemPlace.Logins, PlaceRowOf(vault, ItemPlace.Logins) ?? (object)vault);

            if (Content is EntriesViewModel entries)
            {
                entries.BeginAddOfKindCommand.Press(ItemTemplate.Login);

                if (entries.NewItem is { } form && url.Length > 0)
                {
                    form.Url = url;
                }
            }
        });
    }

    /// <summary>A locked vault's form in the list's room: unlocking it, or creating the vault just added.</summary>
    private LockedVaultViewModel LockedVault()
    {
        var vault = _itemsVault;
        var unlock = new UnlockViewModel(
            vault.Session,
            Home,
            _picker ?? NoFilePicker.Instance,
            () => Post(() => Unlocked(vault)),
            _post,
            paste: Clipboard.TryPasteIntoAsync);

        if (_creating is { } creating && creating.Vault == vault)
        {
            unlock.BeginCreate(creating.Path);

            // Cancelling the create form drops the place it was for; a created vault is unlocked by then.
            unlock.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(UnlockViewModel.IsCreating) && !unlock.IsCreating && !vault.Session.IsUnlocked)
                {
                    Post(() => DropCreated(vault));
                }
            };
        }
        else if (vault.VaultPath.Length > 0)
        {
            unlock.Offer(vault.VaultPath);
        }

        return new LockedVaultViewModel(vault, unlock);
    }

    private void DropCreated(VaultSection vault)
    {
        if (_disposed || _creating?.Vault != vault || OpenOf(vault.Session) is not { } open)
        {
            return;
        }

        _creating = null;
        _itemsVault = SectionOf(_session);
        Vaults!.Close(open);
        Count();
        ShowEveryItem();
    }

    /// <summary>A vault unlocked from its form: its places are counted and its items listed.</summary>
    private void Unlocked(VaultSection vault)
    {
        if (_disposed)
        {
            return;
        }

        if (_creating?.Vault == vault)
        {
            _creating = null;
        }

        CountWhenAVaultChanged();

        if (_itemsVault == vault && Content is LockedVaultViewModel)
        {
            ShowVault(vault);
        }
    }

    /// <summary>The open vault of a section's session, or null where only the vault in view is open.</summary>
    private OpenVault? OpenOf(AppVaultSession session) =>
        Vaults?.All.FirstOrDefault(open => ReferenceEquals(open.Session, session));

    /// <summary>The authority serving a vault, or null.</summary>
    private AppAuthority? AuthorityOf(AppVaultSession session) =>
        OpenOf(session)?.Authority ?? (ReferenceEquals(session, _session) ? Authority : null);

    /// <summary>The unlocked vaults' sections, the one in view first.</summary>
    private IReadOnlyList<VaultSection> UnlockedSections => [.. Account.Vaults.Where(vault => !vault.IsLocked)];

    /// <summary>A screen's vault picker: each unlocked vault, after every one of them where <paramref name="withAll"/>.</summary>
    private List<VaultChoice> VaultChoices(bool withAll)
    {
        List<VaultChoice> choices = withAll ? [new VaultChoice(Strings.AllVaults, null)] : [];
        choices.AddRange(UnlockedSections.Select(vault => new VaultChoice(vault.Title, vault.Session)));
        return choices;
    }

    /// <summary>The section a picker's choice names, or <paramref name="otherwise"/> when it is not unlocked now.</summary>
    private VaultSection Chosen(VaultSection? chosen, VaultSection otherwise) =>
        chosen is { IsLocked: false } && Account.Vaults.Contains(chosen) ? chosen : otherwise;

    /// <summary>The vault a screen that acts on one vault shows first: the vault in view, or the window's own while that is locked.</summary>
    private VaultSection VaultForScreens => _itemsVault.IsLocked ? SectionOf(_session) : _itemsVault;

    /// <summary>The rail's Items pressed again: every item of the vault in view.</summary>
    internal void ShowEveryItem()
    {
        if (PlaceRowOf(_itemsVault, ItemPlace.All) is { } all)
        {
            ShowPlace(_itemsVault, ItemPlace.All, all);
        }
    }

    /// <summary>New's More templates…: the grid of New item's templates, saving in the vault in view.</summary>
    [RelayCommand]
    private void NewItem() => OnItemsPlace()?.ChooseTemplateCommand.Press();

    /// <summary>One of New's templates: its form, saving in the vault in view.</summary>
    [RelayCommand]
    private void NewItemOfKind(ItemTemplate kind) => OnItemsPlace()?.BeginAddOfKindCommand.Press(kind);

    /// <summary>The heading of New's menu, naming the vault it adds to.</summary>
    internal string NewItemHeading => $"New in {_itemsVault.Title}";

    private EntriesViewModel? OnItemsPlace()
    {
        CloseSidebarOverlay();

        if (_current.Kind != DestinationKind.Entries)
        {
            Current = Destinations.Of(DestinationKind.Entries);
        }

        return Content as EntriesViewModel;
    }

    /// <summary>What the title bar's search says while empty: the vault it searches.</summary>
    internal string SearchPlaceholder => _allAccounts || _itemsVault.IsLocked || _itemsVault.Title.Length == 0 ? Strings.Search : $"{Strings.Search} {_itemsVault.Title}";

    /// <summary>The row now listed for one chosen before the rows were rebuilt, which a fold or a count replaces with an equal one.</summary>
    private object? StillListed(object? chosen) => chosen switch
    {
        VaultRow vault => SidebarRows.OfType<VaultRow>().FirstOrDefault(row => row.Vault == vault.Vault),
        PlaceRow place => SidebarRows.OfType<PlaceRow>().FirstOrDefault(row => row.Vault == place.Vault && row.Place == place.Place),
        _ => null,
    };

    /// <summary>What the list's header says under its title: the vault in view, or All Accounts.</summary>
    private string ListSubtitle() => _allAccounts ? Strings.AllAccounts : _itemsVault.Title;

    private PlaceRow? PlaceRowOf(VaultSection vault, ItemPlace place) =>
        SidebarRows.OfType<PlaceRow>().FirstOrDefault(row => row.Vault == vault && row.Place == place);

    /// <summary>The section of an open vault's session, made the first time it is asked for.</summary>
    internal VaultSection SectionOf(AppVaultSession session)
    {
        if (ReferenceEquals(session, _itemsVault.Session))
        {
            return _itemsVault;
        }

        if (!_sections.TryGetValue(session, out var section))
        {
            section = new VaultSection(session, () => Vaults?.PathOf(session));
            _sections[session] = section;
        }

        return section;
    }

    /// <summary>Lists one of a vault's places on Items.</summary>
    private void ShowPlace(VaultSection vault, ItemPlace place, object row, bool allAccounts = false) => Leave(() =>
    {
        var switched = vault != _itemsVault || _allAccounts != allAccounts;
        _itemsVault = vault;
        _place = place;
        _entriesRow = row;
        _allAccounts = allAccounts;

        if (switched && _current.Kind == DestinationKind.Entries)
        {
            Show(_current);
        }
        else
        {
            MoveTo(Destinations.Of(DestinationKind.Entries));
        }

        if (Content is EntriesViewModel entries)
        {
            entries.Place = place;
            entries.ListSubtitle = ListSubtitle();
        }

        OnPropertyChanged(nameof(SelectedSidebarRow));
        OnPropertyChanged(nameof(SearchPlaceholder));
        OnPropertyChanged(nameof(NewItemHeading));
    });

    /// <summary>Lists every item of <paramref name="vault"/> on Items.</summary>
    private void ShowEveryItemNow(VaultSection vault)
    {
        var switched = vault != _itemsVault || _allAccounts;
        _itemsVault = vault;
        _place = ItemPlace.All;
        _allAccounts = false;

        if (switched && _current.Kind == DestinationKind.Entries)
        {
            Show(_current);
        }
        else
        {
            MoveTo(Destinations.Of(DestinationKind.Entries));
        }

        if (Content is EntriesViewModel entries)
        {
            entries.Place = ItemPlace.All;
            entries.ListSubtitle = ListSubtitle();
        }

        OnPropertyChanged(nameof(SelectedSidebarRow));
    }

    /// <summary>Shows a vault's Environments or Trash.</summary>
    private void ShowVaultScreen(VaultSection vault, DestinationKind kind) => Leave(() =>
    {
        var switched = vault != _itemsVault;
        _itemsVault = vault;
        _allAccounts = false;

        if (switched && _current.Kind == kind)
        {
            Show(_current);
        }
        else
        {
            MoveTo(Destinations.Of(kind));
        }

        OnPropertyChanged(nameof(SelectedSidebarRow));
    });

    /// <summary>Folds or unfolds a vault's row.</summary>
    internal RelayCommand<object> FoldCommand { get; }

    /// <summary>
    /// Opens or closes a vault's row, or flips it when <paramref name="open"/> is null.
    /// </summary>
    internal void Fold(object? row, bool? open)
    {
        switch (row)
        {
            case VaultRow { IsExpandable: true } vaultRow:
                vaultRow.Vault.IsExpanded = open ?? !vaultRow.Vault.IsExpanded;
                break;
            default:
                return;
        }

        RebuildSidebar();
    }

    private List<object> SidebarOf()
    {
        List<object> rows = [new PlaceRow(null, ItemPlace.Favorites, Shown(Account.Vaults.Sum(vault => vault.CountOf(ItemPlace.Favorites))))];

        if (ShowsAccounts)
        {
            rows.Add(new PlaceRow(null, ItemPlace.All, Shown(Account.Vaults.Sum(vault => vault.CountOf(ItemPlace.All)))));
            rows.Add(new AccountRow(Account.Title));
        }

        foreach (var vault in Account.Vaults)
        {
            if (ShowsAccounts)
            {
                rows.Add(new VaultRow(vault, vault.IsExpanded, vault.IsLocked, SaveMarkOf(vault)));
            }

            if (vault.IsLocked || (ShowsAccounts && !vault.IsExpanded))
            {
                continue;
            }

            foreach (var place in ItemPlaces.InOrder)
            {
                var count = vault.CountOf(place);

                if (count == 0 && !ItemPlaces.ShownEmpty(place))
                {
                    continue;
                }

                rows.Add(new PlaceRow(vault, place, place == ItemPlace.Trash ? string.Empty : Shown(count)));

                if (place == ItemPlace.Environments && vault == _itemsVault && _current.Kind == DestinationKind.EnvSets)
                {
                    rows.AddRange(_projects);
                }
            }
        }

        return rows;
    }

    private static string Shown(int count) => count > 0 ? count.ToString(CultureInfo.InvariantCulture) : string.Empty;

    private void ShowSecurityCount()
    {
        var needsReview = Recommendations.NeedsReview + _scans.Values.Sum(scan => scan.NeedsReview);
        SecurityPlace.Count = needsReview > 0 ? needsReview.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    /// <summary>Replaces the sidebar's rows when they differ, keeping the list from writing back the row it had.</summary>
    private void RebuildSidebar()
    {
        var rows = SidebarOf();

        if (rows.SequenceEqual(SidebarRows))
        {
            return;
        }

        _rebuildingSidebar = true;

        try
        {
            SidebarRows = rows;
        }
        finally
        {
            _rebuildingSidebar = false;
        }

        OnPropertyChanged(nameof(SelectedSidebarRow));
    }

    /// <summary>The open vaults' sections, the one in view first, keeping each one's folds.</summary>
    private IReadOnlyList<VaultSection> Sections()
    {
        var sessions = Vaults?.Listed.Select(vault => vault.Session).ToList() ?? [_session];

        if (!sessions.Contains(_session))
        {
            sessions.Insert(0, _session);
        }

        foreach (var gone in _sections.Keys.Where(session => !sessions.Contains(session)).ToList())
        {
            _sections.Remove(gone);
        }

        return [.. sessions.Select(SectionOf)];
    }

    /// <summary>Counts each vault's places and the projects of the vault in view for the sidebar, and its lock state.</summary>
    /// <param name="edited">The vault an edit changed, which with any vault opened, closed, locked or unlocked since its count is all that is counted again; null counts every vault.</param>
    private void Count(AppVaultSession? edited = null)
    {
        if (_disposed)
        {
            return;
        }

        var inView = SectionOf(_session);
        _sections[_session] = inView;
        var sections = Sections();

        if (!sections.Contains(_itemsVault))
        {
            _itemsVault = inView;
        }

        // Every edit raises its own vault's count, so a vault neither edited nor opened again since its count still holds it.
        foreach (var section in sections.Where(section => edited is null || ReferenceEquals(section.Session, edited) || !section.CountsVaultOpenNow))
        {
            section.Count();
        }

        _lockStates = LockStates(sections);

        if (!sections.SequenceEqual(Account.Vaults))
        {
            Account = Account with { Vaults = sections };
        }

        FollowVaults();
        KeepScans();

        IReadOnlyList<ProjectListing> projects = [];

        if (_itemsVault.Session.IsUnlocked)
        {
            // Null when the vault locked as its section read it.
            if (_itemsVault.Projects is not { } listed)
            {
                return;
            }

            projects = listed;
        }

        _projects = [.. projects.Select(project => new ProjectRow(project.Name, project.Environments.SelectMany(environment => environment.Members).Distinct().Count()))];
        OnPropertyChanged(nameof(Projects));
        OnPropertyChanged(nameof(HasProjects));
        RebuildSidebar();
    }

    /// <summary>Counts again when another vault opened, locked or closed since the last count; the status tick asks.</summary>
    private void CountWhenAVaultChanged()
    {
        // The share dialog holds its item's title and a typed passphrase, so it closes with its vault's lock whichever count saw the lock first.
        if (_shareVault is { IsUnlocked: false })
        {
            CloseShare();
        }

        if (Vaults is null || string.Equals(LockStates([.. Vaults.Listed.Select(vault => SectionOf(vault.Session))]), _lockStates, StringComparison.Ordinal))
        {
            return;
        }

        if (_itemsVault.IsLocked)
        {
            _place = ItemPlace.All;
            _entriesRow = null;
        }

        // Nothing read from a vault outlives its lock, a screen of several vaults reads them again, and a locked vault's place shows its form.
        var stale = _contentVaults.Any(session => !session.IsUnlocked) || _contentSpansVaults
            || (ShowsItemsPlace && _itemsVault.IsLocked && Content is not LockedVaultViewModel && !_allAccounts)
            || Content is LockedVaultViewModel { Vault.IsLocked: false };

        if (stale && ShowsItemsPlace && _itemsVault.IsLocked && !_allAccounts && _current.Kind != DestinationKind.Entries)
        {
            MoveTo(Destinations.Of(DestinationKind.Entries));
        }
        else if (stale)
        {
            Show(_current);
        }

        Count();
        OnPropertyChanged(nameof(SearchPlaceholder));
        OnPropertyChanged(nameof(NewItemHeading));
        OnPropertyChanged(nameof(SelectedSidebarRow));
    }

    private static string LockStates(IReadOnlyList<VaultSection> sections) =>
        string.Join(",", sections.Select(section => string.Create(CultureInfo.InvariantCulture, $"{section.Session.GetHashCode()}:{section.IsLocked}")));

    private void SetCount(DestinationKind kind, int count)
    {
        foreach (var item in RailPlaces.Where(item => item.Destination.Kind == kind))
        {
            item.Count = count > 0 ? count.ToString(CultureInfo.InvariantCulture) : string.Empty;
        }
    }
}
