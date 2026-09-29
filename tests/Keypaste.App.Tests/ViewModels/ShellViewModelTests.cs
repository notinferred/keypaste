using Keypaste.App.Navigation;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// The shell around the screens: the sidebar's order and counts, the titlebar search and the toast.
/// None of these needs an Avalonia application.
/// </summary>
public sealed class ShellViewModelTests : IDisposable
{
    private readonly TempVault _fixture = new();
    private readonly ManualClock _clock = new();
    private readonly AppVaultSession _session;

    public ShellViewModelTests()
    {
        using (var vault = Vault.Open(_fixture.Path_, TempVault.Password))
        {
            vault.AddEntry(new VaultEntry { Title = "github", Username = "me", Password = "gh-secret", GroupPath = "Work" });
            vault.AddEntry(new VaultEntry { Title = "DATABASE_URL", Password = "db-secret", GroupPath = "env/billing" });
            vault.AddEntry(new VaultEntry { Title = "STRIPE_KEY", Password = "stripe-secret", GroupPath = "env/billing" });
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
        Assert.Equal(["Items", "Work", "env", "Projects", "billing", "Agents"], Titles(shell));
    }

    [Fact]
    public void A_group_in_the_tree_folds_and_choosing_it_shows_its_items()
    {
        using var shell = Shell();
        var env = shell.SidebarRows.OfType<GroupRow>().Single(row => row.Path == "env");

        Assert.True(env.HasChildren);
        Assert.False(env.IsExpanded);

        shell.FoldCommand.Execute(env);
        Assert.Equal(["Items", "Work", "env", "billing", "Projects", "billing", "Agents"], Titles(shell));

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
        Assert.Equal(["Items", "Projects", "billing", "Agents"], Titles(shell));
        Assert.True(entries.SelectedGroup?.IsEverything);
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

        shell.Current = Destinations.Of(DestinationKind.Log);
        Assert.Equal("Settings", shell.SelectedFooter?.Title);
        Assert.True(shell.HasBack);
        Assert.Equal("Settings", shell.BackTitle);
        Assert.False(Assert.IsType<LogViewModel>(shell.Content).IsAgentHistory);

        shell.BackCommand.Execute(null);
        Assert.IsType<SettingsViewModel>(shell.Content);
        Assert.False(shell.HasBack);

        shell.Current = Destinations.Of(DestinationKind.AgentHistory);
        Assert.Equal("Agents", shell.SelectedMain?.Title);
        var history = Assert.IsType<LogViewModel>(shell.Content);
        Assert.True(history.IsAgentHistory);
        Assert.Equal("History", history.Title);
    }

    [Fact]
    public void Settings_advanced_opens_the_activity_log_and_the_share_links()
    {
        using var shell = Shell();
        shell.GoTo(4);

        Assert.IsType<SettingsViewModel>(shell.Content).OpenActivityLogCommand.Execute(null);
        Assert.IsType<LogViewModel>(shell.Content);

        shell.BackCommand.Execute(null);
        Assert.IsType<SettingsViewModel>(shell.Content).OpenShareLinksCommand.Execute(null);
        Assert.IsType<SharingViewModel>(shell.Content);
        Assert.Equal("Share links", shell.CurrentTitle);
    }

    [Fact]
    public void Settings_advanced_opens_the_scoped_tokens_and_diagnostics()
    {
        using var shell = Shell();
        shell.GoTo(4);

        Assert.IsType<SettingsViewModel>(shell.Content).OpenScopedTokensCommand.Execute(null);
        Assert.IsType<ScopedTokensViewModel>(shell.Content);
        Assert.Equal("Back to Settings", $"Back to {shell.BackTitle}");

        shell.BackCommand.Execute(null);
        Assert.IsType<SettingsViewModel>(shell.Content).OpenDiagnosticsCommand.Execute(null);
        Assert.IsType<DiagnosticsViewModel>(shell.Content);
        Assert.Equal("Diagnostics", shell.CurrentTitle);
    }

    [Fact]
    public void Agents_opens_its_history()
    {
        using var shell = Shell();
        shell.GoTo(2);

        Assert.IsType<AgentActivityViewModel>(shell.Content).OpenHistoryCommand.Execute(null);

        Assert.True(Assert.IsType<LogViewModel>(shell.Content).IsAgentHistory);
    }

    [Fact]
    public void The_sidebar_counts_entries_and_lists_projects_with_their_variables()
    {
        using var shell = Shell();

        Assert.Equal("4", shell.MainNav.Single(item => item.Destination.Kind == DestinationKind.Entries).Count);
        Assert.Equal([new ProjectRow("billing", 2)], shell.Projects);
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

        shell.BackCommand.Execute(null);

        Assert.IsType<EntriesViewModel>(shell.Content);
        Assert.Equal("Items", Assert.IsType<NavItem>(shell.SelectedSidebarRow).Title);
    }

    [Fact]
    public void Items_plus_menu_starts_a_project_and_imports_into_the_one_in_view()
    {
        using var shell = Shell();

        shell.NewProjectCommand.Execute(null);
        Assert.True(Assert.IsType<EnvSetsViewModel>(shell.Content).IsAdding);

        shell.GoTo(1);
        shell.ImportEnvCommand.Execute(null);
        Assert.Equal("billing", Assert.IsType<EnvSetsViewModel>(shell.Content).OpenProject?.Name);
    }

    [Fact]
    public void The_titlebar_search_moves_to_items_and_filters_them()
    {
        using var shell = Shell();
        shell.GoTo(2);

        shell.Search = "github";

        var entries = Assert.IsType<EntriesViewModel>(shell.Content);
        Assert.Equal("github", entries.Search);
    }

    [Fact]
    public void The_titlebar_search_says_its_scope_and_can_widen_it()
    {
        using var shell = Shell();
        var entries = Assert.IsType<EntriesViewModel>(shell.Content);
        Assert.Null(shell.SearchScope);
        Assert.Equal("Search all items", shell.SearchPlaceholder);

        entries.SelectedGroup = entries.Groups.Single(group => group.Path == "Work");

        Assert.Equal("Work", shell.SearchScope);
        Assert.Equal("Search in this group", shell.SearchPlaceholder);

        shell.ClearScopeCommand.Execute(null);

        Assert.Null(shell.SearchScope);
        Assert.True(entries.SelectedGroup?.IsEverything);
    }

    [Fact]
    public void A_toast_goes_away_on_its_own()
    {
        using var shell = Shell();

        shell.ShowToast("Copied the username.");
        Assert.True(shell.HasToast);

        _clock.Advance(ShellViewModel.ToastDuration);

        Assert.False(shell.HasToast);
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

        shell.ShareCommand.Execute("env/billing/STRIPE_KEY");
        Assert.True(shell.HasShare);
        Assert.IsType<EntriesViewModel>(shell.Content);
        var share = shell.Share!;
        Assert.Equal("env/billing/STRIPE_KEY", share.SelectedWhat);

        await share.CreateCommand.ExecuteAsync();

        Assert.Equal("Link copied. Expires in 24h, 1 view.", shell.Toast);
        Assert.False(shell.HasShare);

        shell.Current = Destinations.Of(DestinationKind.Sharing);
        var links = Assert.IsType<SharingViewModel>(shell.Content);
        await links.RefreshCommand.ExecuteAsync();
        Assert.Single(links.Rows);
        Assert.False(shell.ShowsHeader);
    }

    [Fact]
    public void Cancel_closes_the_share_dialog_without_a_link()
    {
        using var shell = Shell();

        shell.ShareCommand.Execute("Work/github");
        shell.Share!.CancelCommand.Execute(null);

        Assert.False(shell.HasShare);
        Assert.Null(shell.Share);
    }

    private ShellViewModel Shell() => new(_session, _fixture.Home, authority: null, clock: _clock);
}
