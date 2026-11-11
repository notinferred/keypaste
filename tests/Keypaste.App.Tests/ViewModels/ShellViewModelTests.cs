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
    public void The_sidebar_lists_the_four_places_with_the_group_tree_and_the_projects_beneath_items()
    {
        using var shell = Shell();

        Assert.Equal(["Items", "Agents"], shell.MainNav.Select(item => item.Title));
        Assert.Equal(["Trash", "Settings"], shell.FooterNav.Select(item => item.Title));
        Assert.Equal([1, 2, 3, 4], Destinations.Places.Select(d => d.Shortcut));
        Assert.All(Destinations.All.Where(d => !d.IsPlace), d => Assert.Equal(0, d.Shortcut));
        Assert.Equal(["Items", "Groups", "Work", "env", "Projects", "billing", "Recommendations", "Agents"], Titles(shell));
    }

    [Fact]
    public void A_group_in_the_tree_folds_and_choosing_it_shows_its_items()
    {
        using var shell = Shell();
        var env = shell.SidebarRows.OfType<GroupRow>().Single(row => row.Path == "env");

        Assert.True(env.HasChildren);
        Assert.False(env.IsExpanded);

        shell.FoldCommand.Press(env);
        Assert.Equal(["Items", "Groups", "Work", "env", "billing", "Projects", "billing", "Recommendations", "Agents"], Titles(shell));

        shell.SelectedSidebarRow = shell.SidebarRows.OfType<GroupRow>().Single(row => row.Path == "env/billing");
        var entries = Assert.IsType<EntriesViewModel>(shell.Content);
        Assert.Equal("env/billing", entries.SelectedGroup?.Path);
        Assert.Equal(["DATABASE_URL", "STRIPE_KEY"], entries.Rows.Select(row => row.Title).Order());
        Assert.Equal("env/billing", Assert.IsType<GroupRow>(shell.SelectedSidebarRow).Path);

        // Folding away the group Items shows shows the folded group instead, as KeePassXC does.
        shell.Fold(shell.SidebarRows.OfType<GroupRow>().Single(row => row.Path == "env"), open: false);
        Assert.Equal("env", entries.SelectedGroup?.Path);
        Assert.Equal("env", Assert.IsType<GroupRow>(shell.SelectedSidebarRow).Path);

        // Items folds the whole tree and shows every item.
        shell.Fold(shell.MainNav[0], open: false);
        Assert.Equal(["Items", "Groups", "Projects", "billing", "Recommendations", "Agents"], Titles(shell));
        Assert.True(entries.SelectedGroup?.IsEverything);
        Assert.Same(shell.MainNav[0], shell.SelectedSidebarRow);
    }

    /// <summary>A group deleted while the sidebar shows it leaves the sidebar's tree at once.</summary>
    [Fact]
    public void A_deleted_group_leaves_the_tree()
    {
        using var shell = Shell();

        shell.SelectedSidebarRow = shell.SidebarRows.OfType<GroupRow>().Single(row => row.Path == "Work");
        var entries = Assert.IsType<EntriesViewModel>(shell.Content);
        entries.BeginDeleteGroupCommand.Press();

        Assert.Null(entries.Error);
        Assert.Equal(["Items", "Groups", "env", "Projects", "billing", "Recommendations", "Agents"], Titles(shell));
        Assert.Same(shell.MainNav[0], shell.SelectedSidebarRow);
    }

    private static IEnumerable<string> Titles(ShellViewModel shell) =>
        shell.SidebarRows.Select(row => row switch
        {
            NavItem item => item.Title,
            GroupRow group => group.Title,
            SidebarHeading heading => heading.Title,
            ProjectRow project => project.Name,
            _ => "?",
        });

    [Fact]
    public void Each_digit_reaches_the_place_in_that_position_and_no_other_digit_does_anything()
    {
        using var shell = Shell();

        Assert.True(shell.GoTo(3));
        Assert.IsType<TrashViewModel>(shell.Content);
        Assert.Equal("Trash", shell.SelectedFooter?.Title);

        Assert.True(shell.GoTo(4));
        Assert.IsType<SettingsViewModel>(shell.Content);

        Assert.True(shell.GoTo(2));
        Assert.IsType<AgentActivityViewModel>(shell.Content);
        Assert.Equal("Agents", shell.SelectedMain?.Title);

        Assert.False(shell.GoTo(5));
        Assert.IsType<AgentActivityViewModel>(shell.Content);
    }

    [Fact]
    public void A_screen_under_a_place_keeps_its_place_selected_and_goes_back_to_it()
    {
        using var shell = Shell();

        shell.Current = Destinations.Of(DestinationKind.Advanced);
        Assert.Equal("Settings", shell.SelectedFooter?.Title);
        Assert.True(shell.HasBack);
        Assert.Equal("Settings", shell.BackTitle);

        shell.BackCommand.Press();
        Assert.IsType<SettingsViewModel>(shell.Content);
        Assert.False(shell.HasBack);

        shell.Current = Destinations.Of(DestinationKind.Log);
        Assert.Equal("Agents", shell.SelectedMain?.Title);
        Assert.Equal("Agents", shell.BackTitle);
        Assert.Equal("Activity", shell.CurrentTitle);
        Assert.IsType<LogViewModel>(shell.Content);

        shell.BackCommand.Press();
        Assert.IsType<AgentActivityViewModel>(shell.Content);
        Assert.False(shell.HasBack);
    }

    [Fact]
    public void Settings_opens_advanced_as_a_screen_under_it_and_back_returns_to_settings()
    {
        using var shell = Shell();
        shell.GoTo(4);

        Assert.IsType<SettingsViewModel>(shell.Content).OpenAdvancedCommand.Press();

        Assert.IsType<AdvancedViewModel>(shell.Content);
        Assert.Equal("Advanced", shell.CurrentTitle);
        Assert.Equal("Settings", shell.SelectedFooter?.Title);
        Assert.Equal("Back to Settings", $"Back to {shell.BackTitle}");
        Assert.Equal(0, shell.Current.Shortcut);

        shell.BackCommand.Press();
        Assert.IsType<SettingsViewModel>(shell.Content);
        Assert.False(shell.HasBack);
    }

    [Fact]
    public void Settings_advanced_opens_the_share_links()
    {
        using var shell = Shell();
        shell.GoTo(4);
        Assert.IsType<SettingsViewModel>(shell.Content).OpenAdvancedCommand.Press();

        Assert.IsType<AdvancedViewModel>(shell.Content).OpenShareLinksCommand.Press();
        Assert.IsType<SharingViewModel>(shell.Content);
        Assert.Equal("Share links", shell.CurrentTitle);
    }

    [Fact]
    public void Settings_advanced_opens_the_scoped_tokens_and_diagnostics()
    {
        using var shell = Shell();
        shell.GoTo(4);
        Assert.IsType<SettingsViewModel>(shell.Content).OpenAdvancedCommand.Press();

        Assert.IsType<AdvancedViewModel>(shell.Content).OpenScopedTokensCommand.Press();
        Assert.IsType<ScopedTokensViewModel>(shell.Content);
        Assert.Equal("Back to Advanced", $"Back to {shell.BackTitle}");
        Assert.Equal("Settings", shell.SelectedFooter?.Title);

        shell.BackCommand.Press();
        Assert.IsType<AdvancedViewModel>(shell.Content).OpenDiagnosticsCommand.Press();
        Assert.IsType<DiagnosticsViewModel>(shell.Content);
        Assert.Equal("Diagnostics", shell.CurrentTitle);
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

        Assert.Equal("4", shell.MainNav.Single(item => item.Destination.Kind == DestinationKind.Entries).Count);
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

        shell.SelectedSidebarRow = shell.SidebarRows.OfType<ProjectRow>().Single(row => row.Name == "acme");
        var env = Assert.IsType<EnvSetsViewModel>(shell.Content);

        Assert.Equal(["acme", "shop"], env.Projects);
        Assert.Equal("acme", env.OpenProject?.Name);
        Assert.Equal(["STRIPE_KEY"], env.OpenProject!.Variables.Select(row => row.Key));
        Assert.DoesNotContain(env.OpenProject.Rows, row => row.Key == "OLD_KEY");
    }

    [Fact]
    public void Choosing_a_project_row_opens_it_and_selects_that_row_until_back()
    {
        using var shell = Shell();

        shell.SelectedSidebarRow = shell.SidebarRows.OfType<ProjectRow>().Single();

        var env = Assert.IsType<EnvSetsViewModel>(shell.Content);
        Assert.Equal("billing", env.OpenProject?.Name);
        Assert.Equal(new ProjectRow("billing", 2), shell.SelectedSidebarRow);
        Assert.Equal("Items", shell.BackTitle);

        shell.BackCommand.Press();

        Assert.IsType<EntriesViewModel>(shell.Content);
        Assert.Equal("Items", Assert.IsType<NavItem>(shell.SelectedSidebarRow).Title);
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
        var agents = shell.MainNav.Single(item => item.Destination.Kind == DestinationKind.AgentActivity);

        Assert.False(shell.AgentsServing);
        Assert.True(agents.HasDot);
        Assert.False(agents.DotLive);
        Assert.Equal("not serving agents", agents.Detail);
        Assert.False(shell.MainNav.Single(item => item.Destination.Kind == DestinationKind.Entries).HasDot);
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
