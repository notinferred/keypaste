using Keypaste.App.Navigation;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// The shell around the screens: the sidebar's order and counts, the titlebar search and the toast.
/// None of these needs an Avalonia application.
/// </summary>
public sealed class ShellViewModelTests : IDisposable
{
    private readonly TempVault _fixture = new();
    private readonly ManualClock _clock = new(AppClock.Start);
    private readonly AppVaultSession _session;

    public ShellViewModelTests()
    {
        using (var vault = Vault.Open(_fixture.Path_, TempVault.Password))
        {
            vault.AddEntry(new VaultEntry { Title = "github", Username = "me", Password = "gh-secret", GroupPath = "Work" });

            foreach (var (title, value) in new[] { ("DATABASE_URL", "db-secret"), ("STRIPE_KEY", "stripe-secret") })
            {
                var name = new EntryName("env/billing", title);
                vault.AddEntry(new VaultEntry { Title = title, Password = value, GroupPath = name.GroupPath });
                Assert.True(vault.SetFields(name, [new FieldWrite(title, value)]));
                Assert.True(vault.AddTag(name, "env:billing"));
            }

            vault.Save();
        }

        _session = new AppVaultSession(_clock);

        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_fixture.Path_, master.Value));
    }

    public void Dispose()
    {
        _session.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public void The_rail_holds_the_four_places_and_access_and_the_sidebar_the_vaults_places()
    {
        using var shell = Shell();

        Assert.Equal(["Items", "Agents", "Security", "Access, later", "Settings"], shell.RailPlaces.Select(item => item.Title));
        Assert.True(shell.RailPlaces[3].IsLater);
        Assert.Equal([1, 2, 3, 4], Destinations.Places.Select(d => d.Shortcut));
        Assert.All(Destinations.All.Where(d => !d.IsPlace), d => Assert.Equal(0, d.Shortcut));
        Assert.False(shell.ShowsAccounts);
        Assert.Equal(["Favorites", "All items 4", "Logins 2", "Environments 1", "Trash"], Titles(shell));
    }

    [Fact]
    public void Each_place_lists_its_kind_of_item_and_names_its_vault()
    {
        using var shell = Shell();

        shell.SelectedSidebarRow = shell.Place(ItemPlace.Logins);
        var entries = Assert.IsType<EntriesViewModel>(shell.Content);
        Assert.Equal("Logins", entries.ListTitle);
        Assert.Equal("test", entries.ListSubtitle);
        Assert.Equal(2, entries.Rows.Count);
        Assert.DoesNotContain(entries.Rows, row => row.Kind == EntryKind.Environment);
        Assert.Equal(shell.Place(ItemPlace.Logins), shell.SelectedSidebarRow);

        shell.SelectedSidebarRow = shell.Place(ItemPlace.Trash);
        Assert.IsType<TrashViewModel>(shell.Content);
        Assert.False(shell.HasBack);
        Assert.Equal(shell.Place(ItemPlace.Trash), shell.SelectedSidebarRow);

        shell.SelectedSidebarRow = shell.Place(ItemPlace.Environments);
        Assert.IsType<EnvSetsViewModel>(shell.Content);
        Assert.False(shell.HasBack);
        Assert.Contains(shell.SidebarRows, row => row is ProjectRow { Name: "billing" });
    }

    [Fact]
    public void The_star_tags_an_item_favorite_and_Favorites_lists_and_counts_it()
    {
        using var shell = Shell();
        var entries = Assert.IsType<EntriesViewModel>(shell.Content);
        entries.Selected = entries.Rows.Single(row => row.Title == "github");
        Assert.False(entries.Detail!.IsFavorite);
        Assert.Equal("Add to Favorites", entries.Detail.FavoriteLabel);

        entries.Detail.ToggleFavoriteCommand.Execute(null);

        Assert.True(entries.Detail.IsFavorite);
        Assert.Contains(ItemPlaces.FavoriteTag, _session.Unlocked!.Tags(new EntryName("Work", "github"))!);
        var favorites = shell.SidebarRows.OfType<PlaceRow>().Single(row => row.IsTop && row.Place == ItemPlace.Favorites);
        Assert.Equal("1", favorites.Count);

        shell.SelectedSidebarRow = favorites;
        var listed = Assert.IsType<EntriesViewModel>(shell.Content);
        Assert.Equal("Favorites", listed.ListTitle);
        Assert.Equal(["github"], listed.Rows.Select(row => row.Title));

        // Taken out, it stays listed while open, as an edit keeps its item, and Favorites chosen again lists nothing.
        listed.Selected = listed.Rows.Single();
        listed.Detail!.ToggleFavoriteCommand.Execute(null);
        favorites = shell.SidebarRows.OfType<PlaceRow>().Single(row => row.IsTop && row.Place == ItemPlace.Favorites);
        Assert.Equal(string.Empty, favorites.Count);
        shell.SelectedSidebarRow = shell.Place(ItemPlace.All);
        shell.SelectedSidebarRow = favorites;
        Assert.Empty(Assert.IsType<EntriesViewModel>(shell.Content).Rows);
    }

    /// <summary>The sidebar lists a vault's places and no group, and its All items lists the items of every group together.</summary>
    [Fact]
    public void The_sidebar_lists_no_group_and_All_items_lists_every_groups_items()
    {
        using var shell = Shell();

        Assert.DoesNotContain("Groups", Titles(shell));
        Assert.DoesNotContain("?", Titles(shell));

        shell.SelectedSidebarRow = shell.Place(ItemPlace.All);
        var entries = Assert.IsType<EntriesViewModel>(shell.Content);
        Assert.Contains(entries.Rows, row => row.GroupPath == "env/billing");
        Assert.Contains(entries.Rows, row => row.GroupPath == "Work");
    }

    private static IEnumerable<string> Titles(ShellViewModel shell) =>
        shell.SidebarRows.Select(row => row switch
        {
            PlaceRow place => place.Count.Length > 0 ? $"{place.Title} {place.Count}" : place.Title,
            AccountRow account => account.Title,
            VaultRow vault => vault.Title,
            ProjectRow project => project.Name,
            _ => "?",
        });

    [Fact]
    public void Each_digit_reaches_the_place_in_that_position_and_no_other_digit_does_anything()
    {
        using var shell = Shell();

        Assert.True(shell.GoTo(3));
        Assert.IsType<RecommendationsViewModel>(shell.Content);
        Assert.Equal("Security", shell.SelectedRailPlace?.Title);
        Assert.Null(shell.SelectedSidebarRow);

        Assert.True(shell.GoTo(4));
        Assert.IsType<SettingsViewModel>(shell.Content);

        Assert.True(shell.GoTo(2));
        Assert.IsType<AgentActivityViewModel>(shell.Content);
        Assert.Equal("Agents", shell.SelectedRailPlace?.Title);

        Assert.False(shell.GoTo(5));
        Assert.IsType<AgentActivityViewModel>(shell.Content);
    }

    [Fact]
    public void A_screen_under_a_place_keeps_its_place_selected_and_goes_back_to_it()
    {
        using var shell = Shell();

        shell.Current = Destinations.Of(DestinationKind.Log);
        Assert.Equal("Agents", shell.SelectedRailPlace?.Title);
        Assert.True(shell.HasBack);
        Assert.Equal("Agents", shell.BackTitle);
        Assert.Equal("Activity", shell.CurrentTitle);
        Assert.IsType<LogViewModel>(shell.Content);

        shell.BackCommand.Press();
        Assert.IsType<AgentActivityViewModel>(shell.Content);
        Assert.False(shell.HasBack);
    }

    [Fact]
    public void Settings_opens_each_section_from_its_list_and_keeps_settings_selected()
    {
        using var shell = Shell();
        shell.GoTo(4);
        var settings = Assert.IsType<SettingsViewModel>(shell.Content);

        shell.OpenSettings(SettingsSectionChoice.Browser);
        Assert.Same(settings, shell.Content);
        Assert.Equal(SettingsSectionChoice.Browser, settings.Section);

        shell.OpenSettings(SettingsSectionChoice.ShareLinks);
        Assert.IsType<SharingViewModel>(shell.Content);
        Assert.Equal("Share links", shell.CurrentTitle);

        shell.OpenSettings(SettingsSectionChoice.ScopedTokens);
        Assert.IsType<ScopedTokensViewModel>(shell.Content);
        Assert.Equal("Settings", shell.SelectedRailPlace?.Title);
        Assert.False(shell.HasBack);
        Assert.Equal(0, shell.Current.Shortcut);

        shell.OpenSettings(SettingsSectionChoice.Diagnostics);
        Assert.IsType<DiagnosticsViewModel>(shell.Content);

        shell.OpenSettings(SettingsSectionChoice.ThisVault);
        Assert.Equal(SettingsSectionChoice.ThisVault, Assert.IsType<SettingsViewModel>(shell.Content).Section);
    }

    [Fact]
    public void Agents_opens_its_activity()
    {
        using var shell = Shell();
        shell.GoTo(2);

        Assert.IsType<AgentActivityViewModel>(shell.Content).OpenActivityCommand.Press();

        Assert.IsType<LogViewModel>(shell.Content);
    }

    [Fact]
    public void The_sidebar_counts_entries_and_lists_projects_with_their_tagged_entries()
    {
        using var shell = Shell();

        Assert.Equal("4", shell.Place(ItemPlace.All).Count);
        Assert.Equal([new ProjectRow("billing", 2)], shell.Projects);
    }

    /// <summary>
    /// A project is its entries' own tags: the sidebar and Env profiles list one with no group
    /// under env, and an untagged entry under env/acme is neither a project nor a variable of acme.
    /// </summary>
    [Fact]
    public void Projects_come_from_tags_alone_and_an_untagged_env_entry_is_no_variable()
    {
        using var fixture = new TempVault();

        using (var vault = Vault.Open(fixture.Path_, TempVault.Password))
        {
            vault.AddEntry(new VaultEntry { Title = "OLD_KEY", Password = "old-secret", GroupPath = "env/acme" });

            foreach (var (title, tag, key) in new[] { ("Stripe", "env:acme", "STRIPE_KEY"), ("Shopify", "env:shop", "SHOPIFY_TOKEN") })
            {
                var name = new EntryName("services", title);
                vault.AddEntry(new VaultEntry { Title = title, Password = title + "-login", GroupPath = name.GroupPath });
                Assert.True(vault.SetFields(name, [new FieldWrite(key, title + "-secret")]));
                Assert.True(vault.AddTag(name, tag));
            }

            vault.Save();
        }

        using var session = new AppVaultSession(_clock);

        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(fixture.Path_, master.Value));
        }

        using var shell = new ShellViewModel(session, fixture.Home, authority: null, clock: _clock);

        Assert.Equal([new ProjectRow("acme", 1), new ProjectRow("shop", 1)], shell.Projects);

        shell.SelectedSidebarRow = shell.ProjectRowNamed("acme");
        var env = Assert.IsType<EnvSetsViewModel>(shell.Content);

        Assert.Equal(["acme", "shop"], env.Projects);
        Assert.Equal("acme", env.OpenProject?.Name);
        Assert.Equal(["STRIPE_KEY"], env.OpenProject!.Variables.Select(row => row.Key));
        Assert.DoesNotContain(env.OpenProject.Rows, row => row.Key == "OLD_KEY");
    }

    [Fact]
    public void Choosing_a_project_row_opens_it_and_selects_that_row_until_another_place()
    {
        using var shell = Shell();

        shell.SelectedSidebarRow = shell.ProjectRowNamed("billing");

        var env = Assert.IsType<EnvSetsViewModel>(shell.Content);
        Assert.Equal("billing", env.OpenProject?.Name);
        Assert.Equal(new ProjectRow("billing", 2), shell.SelectedSidebarRow);
        Assert.False(shell.HasBack);

        shell.SelectedSidebarRow = shell.Place(ItemPlace.All);

        Assert.IsType<EntriesViewModel>(shell.Content);
        Assert.Equal(shell.Place(ItemPlace.All), shell.SelectedSidebarRow);
        Assert.DoesNotContain(shell.SidebarRows, row => row is ProjectRow);
    }

    /// <summary>Environments opens its first project itself, and the sidebar highlights that project under the vault's Environments rather than a place.</summary>
    [Fact]
    public void The_project_Environments_opens_is_the_row_the_sidebar_highlights()
    {
        using var shell = Shell();

        shell.SelectedSidebarRow = shell.Place(ItemPlace.Environments);

        var env = Assert.IsType<EnvSetsViewModel>(shell.Content);
        Assert.Equal("billing", env.OpenProject?.Name);
        Assert.Equal(new ProjectRow("billing", 2), shell.SelectedSidebarRow);

        env.CloseCommand.Execute(null);
        Assert.Equal(shell.Place(ItemPlace.Environments), shell.SelectedSidebarRow);
    }

    [Fact]
    public void Items_plus_menu_starts_a_project_and_imports_into_the_first()
    {
        using var shell = Shell();

        shell.NewProjectCommand.Press();
        Assert.True(Assert.IsType<EnvSetsViewModel>(shell.Content).IsAdding);

        shell.GoTo(1);
        shell.ImportEnvCommand.Press();
        Assert.Equal("billing", Assert.IsType<EnvSetsViewModel>(shell.Content).OpenProject?.Name);
    }

    [Fact]
    public void The_search_filters_items_and_is_kept_across_a_trip_to_another_place()
    {
        using var shell = Shell();
        shell.Search = "github";
        Assert.Equal("github", Assert.IsType<EntriesViewModel>(shell.Content).Search);

        shell.GoTo(2);
        shell.GoTo(1);

        Assert.Equal("github", shell.Search);
        Assert.Equal("github", Assert.IsType<EntriesViewModel>(shell.Content).Search);
    }

    [Fact]
    public void Find_goes_to_items_from_another_place()
    {
        using var shell = Shell();
        shell.GoTo(2);
        var searched = 0;
        shell.SearchFocusRequested += (_, _) => searched += shell.Content is EntriesViewModel ? 1 : 0;

        shell.FocusSearch();

        Assert.Equal(1, searched);
    }

    [Fact]
    public void A_toast_goes_away_on_its_own()
    {
        using var shell = Shell();

        shell.ShowToast("Copied the username.", ToastTone.Success);
        Assert.NotEmpty(shell.Toasts.Shown);

        _clock.Advance(ToastStack.DurationOf("Copied the username."));

        Assert.Empty(shell.Toasts.Shown);
    }

    [Fact]
    public void Without_an_authority_the_agents_row_has_a_dot_that_is_not_live()
    {
        using var shell = Shell();
        var agents = shell.RailPlaces.Single(item => item.Destination.Kind == DestinationKind.AgentActivity);

        Assert.False(shell.AgentsServing);
        Assert.True(agents.HasDot);
        Assert.False(agents.DotLive);
        Assert.Equal("Agents · Not answering agents", agents.RailTip);
        Assert.False(shell.RailPlaces.Single(item => item.Destination.Kind == DestinationKind.Entries).HasDot);
    }

    [Fact]
    public async Task Share_from_an_item_is_a_dialog_that_toasts_closes_and_lists_the_link_under_settings()
    {
        using var server = new FakeShareServer { Now = _clock.GetUtcNow() };
        using var shell = new ShellViewModel(_session, _fixture.Home, authority: null, clipboard: new Clipboard.FakeClipboard(), clock: _clock)
        {
            ShareTransport = server,
        };

        shell.ShareCommand.Press(new EntryName("env/billing", "STRIPE_KEY"));
        Assert.True(shell.HasShare);
        Assert.IsType<EntriesViewModel>(shell.Content);
        var share = shell.Share!;
        Assert.Equal("env/billing/STRIPE_KEY", share.SelectedWhat);

        await share.CreateCommand.PressAsync();

        Assert.Equal("Link copied. Expires in 24h, 1 view.", shell.Toasts.Shown.Last().Message);
        Assert.False(shell.HasShare);

        shell.Current = Destinations.Of(DestinationKind.Sharing);
        var links = Assert.IsType<SharingViewModel>(shell.Content);
        await links.RefreshCommand.PressAsync();
        Assert.Single(links.Rows);
        Assert.False(shell.ShowsHeader);
    }

    [Fact]
    public void Cancel_closes_the_share_dialog_without_a_link()
    {
        using var shell = Shell();

        shell.ShareCommand.Press(new EntryName("Work", "github"));
        shell.Share!.CancelCommand.Press();

        Assert.False(shell.HasShare);
        Assert.Null(shell.Share);
    }

    private ShellViewModel Shell() => new(_session, _fixture.Home, authority: null, clock: _clock);
}
