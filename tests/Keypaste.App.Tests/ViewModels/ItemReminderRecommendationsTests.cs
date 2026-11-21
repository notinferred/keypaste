using Keypaste.App.Navigation;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Recommendations;
using Keypaste.Core.Settings;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// Items expiring within Settings' reminder and items tagged needs-update are listed in Recommendations by item, counted quietly,
/// dismissed as other findings are, and open their provider's page only through the browser and only when pressed.
/// </summary>
public sealed class ItemReminderRecommendationsTests : IDisposable
{
    private const string _secret = "Vq7#tR2p!Lx9@mZ4wK8$bN3e";

    private static readonly EntryName _github = new("keys", "GitHub");
    private static readonly EntryName _stripe = new("keys", "Stripe");
    private static readonly EntryName _bank = new("logins", "Bank");

    private readonly TempVault _fixture = new();
    private readonly ManualClock _clock = new(AppClock.Start);
    private readonly RecordingLauncher _web = new();
    private readonly AppVaultSession _session;

    public ItemReminderRecommendationsTests()
    {
        using (var vault = ClaimedVault.Open(_fixture.Path_, TempVault.Password))
        {
            Add(vault, _github, "https://github.com", ItemKind.ApiKey);
            vault.SetExpiryUnchecked(_github, AppClock.Start.AddDays(3));
            Add(vault, _bank, "https://bank.example.com/login", ItemKind.Login);
            vault.SetExpiryUnchecked(_bank, AppClock.Start.AddDays(20));
            Add(vault, _stripe, string.Empty, ItemKind.ApiKey);
            vault.AddTag(_stripe, ItemChangeRules.NeedsUpdateTag);
            vault.Save();
        }

        _session = new AppVaultSession(_clock);
        Unlock();
    }

    public void Dispose()
    {
        _session.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public async Task Unlocking_lists_an_item_expiring_within_14_days_and_one_marked_as_needing_an_update()
    {
        using var shell = Shell();
        await shell.Recommendations.Checking;

        Assert.Equal(
            [("Expires soon", "Their expiry date is within the next 14 days."), ("Marked as needing an update", "Tagged needs-update by you or an agent.")],
            shell.Recommendations.ReminderGroups.Select(group => (group.Heading, group.Reason)));

        var expiring = Assert.Single(shell.Recommendations.ReminderGroups[0].Rows);
        Assert.Equal("GitHub", expiring.Entry);
        Assert.StartsWith("Expires ", expiring.Detail, StringComparison.Ordinal);
        Assert.Equal(("Open key page", "https://github.com/settings/tokens"), (expiring.PageLabel, expiring.PageAddress));

        var marked = Assert.Single(shell.Recommendations.ReminderGroups[1].Rows);
        Assert.Equal("Stripe", marked.Entry);
        Assert.StartsWith("Marked ", marked.Detail, StringComparison.Ordinal);
        Assert.False(marked.HasPage);
        Assert.False(marked.OpenPageCommand.CanExecute(null));

        Assert.Equal("2", shell.SecurityPlace.Count);
        Assert.Empty(shell.Recommendations.Passwords);
        Assert.Empty(_web.Opened);
    }

    [Fact]
    public async Task The_setting_widens_narrows_and_turns_off_the_list_at_once_and_reaches_the_file()
    {
        using var shell = Shell();
        await shell.Recommendations.Checking;
        using var settings = new SettingsViewModel(_session, _fixture.Home, shell.Preferences, _ => { });

        Assert.Equal(["Off", "7 days", "14 days", "30 days"], settings.ExpiryReminderChoices.Select(choice => choice.Label));
        Assert.Equal(AppSettings.DefaultExpiryReminderDays, settings.ExpiryReminder.Days);

        settings.ExpiryReminder = settings.ExpiryReminderChoices.Single(choice => choice.Days == 30);
        await shell.Recommendations.Checking;

        Assert.Equal([_github, _bank, _stripe], shell.Recommendations.Reminders.Select(row => row.Finding.Entry));
        Assert.Equal("Their expiry date is within the next 30 days.", shell.Recommendations.ReminderGroups[0].Reason);
        Assert.Equal("Open password page", shell.Recommendations.Reminders[1].PageLabel);
        Assert.Equal("https://bank.example.com/.well-known/change-password", shell.Recommendations.Reminders[1].PageAddress);
        Assert.Equal(30, AppSettings.Load(KeypasteHome.SettingsPath(_fixture.Home)).ExpiryReminderDays);

        settings.ExpiryReminder = settings.ExpiryReminderChoices.Single(choice => choice.Days == 0);
        await shell.Recommendations.Checking;

        Assert.Equal([ReminderReason.NeedsUpdate], shell.Recommendations.Reminders.Select(row => row.Finding.Reason));
        Assert.Equal("1", shell.SecurityPlace.Count);
    }

    [Fact]
    public async Task A_dismissed_reminder_survives_a_lock_and_is_listed_again_once_its_expiry_changes()
    {
        using (var shell = Shell())
        {
            await shell.Recommendations.Checking;
            shell.Recommendations.Reminders.Single(row => row.Finding.Entry == _github).DismissCommand.Press();

            Assert.Equal("1", shell.SecurityPlace.Count);
        }

        var stored = File.ReadAllText(KeypasteHome.RecommendationsPath(_fixture.Home));
        Assert.DoesNotContain("reminder", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("expires", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("needs-update", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("GitHub", stored, StringComparison.Ordinal);
        Assert.DoesNotContain(_secret, stored, StringComparison.Ordinal);

        _session.Lock(VaultLockReason.Manual);
        Unlock();

        using var again = Shell();
        await again.Recommendations.Checking;
        Assert.True(again.Recommendations.Reminders.Single(row => row.Finding.Entry == _github).IsDismissed);
        Assert.Equal("1", again.SecurityPlace.Count);

        _session.Unlocked!.SetExpiryUnchecked(_github, AppClock.Start.AddDays(5));
        _session.Unlocked!.Save();
        again.Recommendations.Check();
        await again.Recommendations.Checking;

        Assert.True(again.Recommendations.Reminders.Single(row => row.Finding.Entry == _github).NeedsReview);
        Assert.Equal("2", again.SecurityPlace.Count);
    }

    [Fact]
    public async Task The_page_opens_in_the_browser_only_when_pressed_and_says_so_when_it_does_not()
    {
        using var shell = Shell();
        await shell.Recommendations.Checking;
        var expiring = shell.Recommendations.Reminders.Single(row => row.Finding.Entry == _github);

        Assert.Empty(_web.Opened);

        await expiring.OpenPageCommand.ExecuteAsync(null);

        Assert.Equal([new Uri("https://github.com/settings/tokens")], _web.Opened);
        Assert.Equal(string.Empty, shell.Recommendations.Message);

        _web.Opens = false;
        await expiring.OpenPageCommand.ExecuteAsync(null);

        Assert.Equal("Your system didn't open https://github.com/settings/tokens. Copy it and paste it into your browser.", shell.Recommendations.Message);
    }

    [Fact]
    public async Task Open_item_shows_the_item_a_reminder_names_in_items()
    {
        using var shell = Shell();
        await shell.Recommendations.Checking;
        shell.Current = Destinations.Of(DestinationKind.Settings);

        shell.Recommendations.Reminders.Single(row => row.Finding.Entry == _stripe).OpenCommand.Press();

        var entries = Assert.IsType<EntriesViewModel>(shell.Content);
        Assert.Equal(_stripe, entries.Selected?.Name);
    }

    private static void Add(Vault vault, EntryName name, string url, ItemKind kind)
    {
        vault.AddEntry(new VaultEntry { GroupPath = name.GroupPath, Title = name.Title, Password = _secret + name.Title, Url = url });
        vault.SetKind(name, kind);
    }

    private ShellViewModel Shell() =>
        new(_session, _fixture.Home, authority: null, clock: _clock, post: null, web: _web);

    private void Unlock()
    {
        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_fixture.Path_, master.Value));
    }

    /// <summary>Records each address Recommendations asks the platform to open.</summary>
    private sealed class RecordingLauncher : IWebLauncher
    {
        internal List<Uri> Opened { get; } = [];

        internal bool Opens { get; set; } = true;

        public Task<bool> OpenAsync(Uri address)
        {
            Opened.Add(address);
            return Task.FromResult(Opens);
        }
    }
}
