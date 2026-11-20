using Keypaste.Core.Approval;
using Keypaste.Core.Recommendations;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Items expiring within the days Settings reminds before, and items tagged needs-update, are found by item and never by value,
/// each with where to make a new credential, worked out from its URL alone.
/// </summary>
public sealed class ItemReminderCheckTests : IDisposable
{
    private const string _master = "item-reminder-master-pw";
    private const string _secret = "Vq7#tR2p!Lx9@mZ4wK8$bN3e";

    private static readonly DateTimeOffset _now = new(2030, 1, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTime _elsewhere = new(2030, 1, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly EntryName _github = new("keys", "GitHub");
    private static readonly EntryName _stripe = new("keys", "Stripe");
    private static readonly EntryName _cloud = new("keys", "Cloud");
    private static readonly EntryName _mail = new("logins", "Mail");
    private static readonly EntryName _later = new("logins", "Later");
    private static readonly EntryName _past = new("logins", "Past");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-item-reminder-tests-").FullName;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void An_item_is_listed_from_just_after_now_to_the_last_day_of_the_window_and_not_once_it_has_expired()
    {
        Seed(created =>
        {
            Add(created, _past, "https://mail.example.com", ItemKind.Login);
            created.SetExpiryUnchecked(_past, _now);
            Add(created, _mail, "https://mail.example.com", ItemKind.Login);
            created.SetExpiryUnchecked(_mail, _now.AddSeconds(1));
            Add(created, _github, "https://github.com", ItemKind.ApiKey);
            created.SetExpiryUnchecked(_github, _now.AddDays(14));
            Add(created, _later, "https://mail.example.com", ItemKind.Login);
            created.SetExpiryUnchecked(_later, _now.AddDays(14).AddSeconds(1));
        });

        using var vault = ClaimedVault.Open(VaultPath, _master);
        var reminders = ItemReminderCheck.Scan(vault, _now, 14);

        Assert.Equal([_mail, _github], reminders.Select(reminder => reminder.Entry));
        Assert.All(reminders, reminder => Assert.Equal(ReminderReason.ExpiresSoon, reminder.Reason));
        Assert.Equal([_now.AddSeconds(1), _now.AddDays(14)], reminders.Select(reminder => reminder.When));
        Assert.Equal("expires@2030-01-15T09:00:00.0000000Z", reminders[1].Key);
        Assert.Equal(new NewCredentialPage(new Uri("https://mail.example.com/.well-known/change-password"), NewCredentialPageKind.ChangePassword), reminders[0].Page);
        Assert.All(reminders, reminder => Assert.Equal(vault.EntryUuid(reminder.Entry), reminder.EntryUuid));
        Assert.Equal(_later, Assert.Single(ItemReminderCheck.Scan(vault, _now, 30), reminder => reminder.Entry == _later).Entry);
    }

    [Fact]
    public void The_next_change_is_the_first_expiry_to_pass_or_come_within_the_window_of_an_item_reported_on()
    {
        Seed(created =>
        {
            Add(created, _past, "https://mail.example.com", ItemKind.Login);
            created.SetExpiryUnchecked(_past, _now);
            Add(created, _mail, "https://mail.example.com", ItemKind.Login);
            created.SetExpiryUnchecked(_mail, _now.AddDays(3));
            Add(created, _later, "https://mail.example.com", ItemKind.Login);
            created.SetExpiryUnchecked(_later, _now.AddDays(14).AddHours(2));
            Add(created, _cloud, "https://console.aws.amazon.com", ItemKind.ApiKey);
            created.SetExpiryUnchecked(_cloud, _now.AddHours(1));
        });

        ForeignEdit.Apply(VaultPath, _master, _cloud.Title, _elsewhere, entry => entry.QualityCheck = false);

        using var vault = ClaimedVault.Open(VaultPath, _master);

        Assert.Equal(_now.AddHours(2), ItemReminderCheck.NextChange(vault, _now, 14));
        Assert.Equal(_now.AddDays(3), ItemReminderCheck.NextChange(vault, _now, 0));
        Assert.Equal(_now.AddDays(14).AddHours(2), ItemReminderCheck.NextChange(vault, _now.AddDays(3), 0));
        Assert.Null(ItemReminderCheck.NextChange(vault, _now.AddDays(15), 14));
    }

    [Fact]
    public void Off_lists_no_expiry_and_still_lists_an_item_marked_as_needing_an_update()
    {
        Seed(created =>
        {
            Add(created, _github, "https://github.com", ItemKind.ApiKey);
            created.SetExpiryUnchecked(_github, _now.AddDays(1));
            Add(created, _stripe, "https://dashboard.stripe.com", ItemKind.ApiKey);
            Assert.True(created.AddTag(_stripe, ItemChangeRules.NeedsUpdateTag));
        });

        using var vault = ClaimedVault.Open(VaultPath, _master);
        var marked = Assert.Single(ItemReminderCheck.Scan(vault, _now, 0));

        Assert.Equal((_stripe, ReminderReason.NeedsUpdate), (marked.Entry, marked.Reason));
        Assert.NotNull(marked.When);
        Assert.InRange(marked.When.Value, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.StartsWith("needs-update@", marked.Key, StringComparison.Ordinal);
        Assert.Equal(new NewCredentialPage(new Uri("https://dashboard.stripe.com/apikeys"), NewCredentialPageKind.Provider), marked.Page);
    }

    [Fact]
    public void A_changed_expiry_is_a_new_key_so_a_dismissal_of_the_old_one_does_not_hide_it()
    {
        Seed(created =>
        {
            Add(created, _github, "github.com", ItemKind.ApiKey);
            created.SetExpiryUnchecked(_github, _now.AddDays(3));
        });

        string before;

        using (var vault = ClaimedVault.Open(VaultPath, _master))
        {
            before = Assert.Single(ItemReminderCheck.Scan(vault, _now, 14)).Key;
            vault.SetExpiryUnchecked(_github, _now.AddDays(5));
            vault.Save();
        }

        using var reopened = ClaimedVault.Open(VaultPath, _master);
        Assert.NotEqual(before, Assert.Single(ItemReminderCheck.Scan(reopened, _now, 14)).Key);
    }

    [Fact]
    public void An_item_left_out_of_reports_is_not_reminded_of_its_expiry()
    {
        Seed(created =>
        {
            Add(created, _cloud, "https://console.aws.amazon.com", ItemKind.ApiKey);
            created.SetExpiryUnchecked(_cloud, _now.AddDays(2));
        });

        ForeignEdit.Apply(VaultPath, _master, _cloud.Title, _elsewhere, entry => entry.QualityCheck = false);

        using var vault = ClaimedVault.Open(VaultPath, _master);
        Assert.Empty(ItemReminderCheck.Scan(vault, _now, 14));
    }

    [Fact]
    public void A_reminder_holds_no_value()
    {
        Seed(created =>
        {
            Add(created, _github, "https://github.com", ItemKind.ApiKey);
            created.SetExpiryUnchecked(_github, _now.AddDays(2));
        });

        using var vault = ClaimedVault.Open(VaultPath, _master);
        var reminder = Assert.Single(ItemReminderCheck.Scan(vault, _now, 14));

        Assert.DoesNotContain(_secret, reminder.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://github.com", ItemKind.ApiKey, "https://github.com/settings/tokens", NewCredentialPageKind.Provider)]
    [InlineData("api.github.com/user", ItemKind.None, "https://github.com/settings/tokens", NewCredentialPageKind.Provider)]
    [InlineData("https://gitlab.com/group/project", ItemKind.ApiKey, "https://gitlab.com/-/user_settings/personal_access_tokens", NewCredentialPageKind.Provider)]
    [InlineData("https://us-east-1.console.aws.amazon.com/iam", ItemKind.ApiKey, "https://console.aws.amazon.com/iam/home#/security_credentials", NewCredentialPageKind.Provider)]
    [InlineData("https://www.amazon.com/orders", ItemKind.ApiKey, "https://www.amazon.com/orders", NewCredentialPageKind.ItemUrl)]
    [InlineData("https://api.openai.com/v1", ItemKind.ApiKey, "https://platform.openai.com/api-keys", NewCredentialPageKind.Provider)]
    [InlineData("https://chatgpt.openai.com", ItemKind.ApiKey, "https://chatgpt.openai.com/", NewCredentialPageKind.ItemUrl)]
    [InlineData("https://console.anthropic.com", ItemKind.ApiKey, "https://console.anthropic.com/settings/keys", NewCredentialPageKind.Provider)]
    [InlineData("https://api.stripe.com", ItemKind.ApiKey, "https://dashboard.stripe.com/apikeys", NewCredentialPageKind.Provider)]
    [InlineData("https://dash.cloudflare.com", ItemKind.Server, "https://dash.cloudflare.com/profile/api-tokens", NewCredentialPageKind.Provider)]
    [InlineData("https://github.com.example.net", ItemKind.ApiKey, "https://github.com.example.net/", NewCredentialPageKind.ItemUrl)]
    [InlineData("https://notgithub.com", ItemKind.ApiKey, "https://notgithub.com/", NewCredentialPageKind.ItemUrl)]
    [InlineData("https://github.com/login", ItemKind.Login, "https://github.com/.well-known/change-password", NewCredentialPageKind.ChangePassword)]
    [InlineData("shop.example.invalid:8443/account", ItemKind.Login, "https://shop.example.invalid:8443/.well-known/change-password", NewCredentialPageKind.ChangePassword)]
    [InlineData("https://db.example.invalid/admin", ItemKind.Database, "https://db.example.invalid/admin", NewCredentialPageKind.ItemUrl)]
    public void The_page_is_worked_out_from_the_url_and_kind_alone(string url, ItemKind kind, string page, NewCredentialPageKind pageKind) =>
        Assert.Equal(new NewCredentialPage(new Uri(page), pageKind), NewCredentialPages.For(url, kind));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("javascript:alert(1)")]
    [InlineData("cmd://open-terminal")]
    [InlineData("file:///etc/passwd")]
    public void A_url_keypaste_does_not_open_gives_no_page(string? url) =>
        Assert.Null(NewCredentialPages.For(url, ItemKind.Login));

    private void Seed(Action<Vault> fill)
    {
        using var vault = ClaimedVault.Create(VaultPath, _master);
        fill(vault);
        vault.Save();
    }

    private static void Add(Vault vault, EntryName name, string url, ItemKind kind)
    {
        vault.AddEntry(new VaultEntry { GroupPath = name.GroupPath, Title = name.Title, Password = _secret, Url = url });
        vault.SetKind(name, kind);
    }
}
