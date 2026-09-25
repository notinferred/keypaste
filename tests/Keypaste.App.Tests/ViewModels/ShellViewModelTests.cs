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
    public void The_sidebar_leads_with_secrets_and_keeps_settings_and_trash_at_the_bottom()
    {
        using var shell = Shell();

        Assert.Equal(["Secrets", "Agents", "Activity", "Env profiles"], shell.MainNav.Select(item => item.Title));
        Assert.Equal(["Settings", "Trash"], shell.FooterNav.Select(item => item.Title));
        Assert.Equal(Enumerable.Range(1, Destinations.All.Count), Destinations.All.Select(d => d.Shortcut));
    }

    [Fact]
    public void A_digit_reaches_the_row_in_that_position()
    {
        using var shell = Shell();

        Assert.True(shell.GoTo(4));

        Assert.IsType<EnvSetsViewModel>(shell.Content);
        Assert.Null(shell.SelectedFooter);
        Assert.Equal("Env profiles", shell.SelectedMain?.Title);
    }

    [Fact]
    public void The_sidebar_counts_entries_and_lists_projects_with_their_variables()
    {
        using var shell = Shell();

        Assert.Equal("4", shell.MainNav.Single(item => item.Destination.Kind == DestinationKind.Entries).Count);
        Assert.Equal([new ProjectRow("billing", 2)], shell.Projects);
    }

    [Fact]
    public void Opening_a_project_from_the_sidebar_opens_it_in_env_profiles()
    {
        using var shell = Shell();

        shell.OpenProjectCommand.Execute("billing");

        var env = Assert.IsType<EnvSetsViewModel>(shell.Content);
        Assert.Equal("billing", env.OpenProject?.Name);
    }

    [Fact]
    public void The_titlebar_search_moves_to_secrets_and_filters_them()
    {
        using var shell = Shell();
        shell.GoTo(3);

        shell.Search = "github";

        var entries = Assert.IsType<EntriesViewModel>(shell.Content);
        Assert.Equal("github", entries.Search);
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
    public void Without_an_authority_the_mcp_card_says_it_is_stopped()
    {
        using var shell = Shell();

        Assert.False(shell.McpRunning);
        Assert.Equal("stopped", shell.McpState);
    }

    private ShellViewModel Shell() => new(_session, _fixture.Home, authority: null, clock: _clock);
}
