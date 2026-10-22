using Keypaste.App.Navigation;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Recommendations;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// V.9 in the app: weak, reused and expired passwords are listed under Settings by item and never by
/// value, counted quietly, checked against the session's clock, and a dismissal is remembered.
/// </summary>
public sealed class PasswordHealthRecommendationsTests : IDisposable
{
    private const string _weak = "hunter2";
    private const string _shared = "k8Fq!2vZ#pL9xR4m@Tw7";
    private const string _strong = "Vq7#tR2p!Lx9@mZ4wK8$bN3e";
    private const string _alsoStrong = "Jd4%nW8s^Hc2*Ty6&Qb9";
    private const string _replacement = "Rt5&kM9w!Zp2#Lc7^Hy4";
    private const string _otherReplacement = "Gb8@xQ3v%Nf6!Wk1*Ps9";

    private static readonly EntryName _mail = new("services", "Mail");
    private static readonly EntryName _forum = new("services", "Forum");
    private static readonly EntryName _shop = new("services", "Shop");
    private static readonly EntryName _cloud = new("services", "Cloud");
    private static readonly EntryName _bank = new("services", "Bank");

    private readonly TempVault _fixture = new();
    private readonly ManualClock _clock = new(AppClock.Start);
    private readonly AppVaultSession _session;

    public PasswordHealthRecommendationsTests()
    {
        using (var vault = Vault.Open(_fixture.Path_, TempVault.Password))
        {
            Add(vault, _mail, _weak);
            Add(vault, _forum, _shared);
            Add(vault, _shop, _shared);
            Add(vault, _cloud, _strong);
            vault.SetExpiryUnchecked(_cloud, AppClock.Start.AddDays(-1));

            // Past by the computer's clock and not by the session's, so a check that read the wrong clock lists it.
            Add(vault, _bank, _alsoStrong);
            vault.SetExpiryUnchecked(_bank, AppClock.Start.AddDays(30));

            Add(vault, new EntryName("services", "Deleted"), _shared);
            vault.RemoveEntry(new EntryName("services", "Deleted"), out _);
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
    public async Task Unlocking_lists_each_weak_reused_and_expired_item_and_counts_them_quietly_on_settings()
    {
        using var shell = Shell();
        await shell.Recommendations.Checking;
        var rows = shell.Recommendations.Passwords;

        Assert.Equal(
            [("services/Cloud", "Expired item"), ("services/Forum", "Reused password"), ("services/Mail", "Weak password"), ("services/Shop", "Reused password")],
            rows.Select(row => (row.Entry, row.Problem)));
        Assert.All(rows, row => Assert.Equal("Needs review", row.StateLabel));
        Assert.StartsWith("on services/Cloud, whose expiry date ", rows[0].Where, StringComparison.Ordinal);
        Assert.EndsWith(" has passed", rows[0].Where, StringComparison.Ordinal);
        Assert.Equal("on services/Forum, also on services/Shop", rows[1].Where);
        Assert.Equal("on services/Mail, short or predictable enough to be guessed", rows[2].Where);
        Assert.Equal("on services/Shop, also on services/Forum", rows[3].Where);
        Assert.Equal("Weak password on services/Mail, short or predictable enough to be guessed, needs review", rows[2].Description);

        Assert.Equal("4", Settings(shell).Count);
        Assert.Null(shell.Notice);
        Assert.Null(shell.Toast);
    }

    [Fact]
    public async Task No_row_and_no_stored_dismissal_holds_a_password_or_a_title()
    {
        using (var shell = Shell())
        {
            await shell.Recommendations.Checking;

            var surfaced = shell.Recommendations.Passwords
                .SelectMany(row => new[] { row.Entry, row.Problem, row.Where, row.Description, row.StateLabel, row.Finding.ToString(), row.Finding.Key })
                .ToList();

            Assert.All(surfaced, text => Assert.DoesNotContain(new[] { _weak, _shared, _strong, _alsoStrong }, value => text.Contains(value, StringComparison.Ordinal)));

            foreach (var row in shell.Recommendations.Passwords.ToList())
            {
                row.DismissCommand.Execute(null);
            }

            Assert.Equal(string.Empty, Settings(shell).Count);
        }

        var stored = File.ReadAllText(KeypasteHome.RecommendationsPath(_fixture.Home));
        Assert.Contains("\"password\"", stored, StringComparison.Ordinal);

        foreach (var text in new[] { _weak, _shared, _strong, _alsoStrong, "Mail", "Forum", "Shop", "Cloud" })
        {
            Assert.DoesNotContain(text, stored, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_dismissal_survives_a_lock_and_unlock_and_review_again_lists_it_again()
    {
        using (var shell = Shell())
        {
            await shell.Recommendations.Checking;

            shell.Recommendations.Passwords.Single(row => row.Finding.Entry == _mail).DismissCommand.Execute(null);

            Assert.Equal("3", Settings(shell).Count);
            Assert.Equal((_mail, "Dismissed"), (shell.Recommendations.Passwords[^1].Finding.Entry, shell.Recommendations.Passwords[^1].StateLabel));
        }

        _session.Lock(VaultLockReason.Manual);
        Unlock();

        using var again = Shell();
        await again.Recommendations.Checking;
        var dismissed = again.Recommendations.Passwords.Single(row => row.IsDismissed);
        Assert.Equal((_mail, PasswordProblem.Weak), (dismissed.Finding.Entry, dismissed.Finding.Problem));
        Assert.Equal("3", Settings(again).Count);

        dismissed.ReviewAgainCommand.Execute(null);

        Assert.Equal("4", Settings(again).Count);
        Assert.DoesNotContain(again.Recommendations.Passwords, row => row.IsDismissed);
    }

    [Fact]
    public async Task A_dismissed_expiry_is_listed_again_once_a_new_expiry_passes()
    {
        using var shell = Shell();
        await shell.Recommendations.Checking;
        shell.Recommendations.Passwords.Single(row => row.Finding.Problem == PasswordProblem.Expired).DismissCommand.Execute(null);
        Assert.Equal("3", Settings(shell).Count);

        var vault = _session.Unlocked!;
        vault.SetExpiryUnchecked(_cloud, AppClock.Start.AddHours(-1));
        vault.Save();
        shell.Recommendations.Check();
        await shell.Recommendations.Checking;

        var expired = shell.Recommendations.Passwords.Single(row => row.Finding.Problem == PasswordProblem.Expired);
        Assert.True(expired.NeedsReview);
        Assert.Equal("4", Settings(shell).Count);
    }

    [Fact]
    public async Task Replacing_a_weak_or_reused_password_takes_its_rows_off_the_list()
    {
        using var shell = Shell();
        await shell.Recommendations.Checking;
        var vault = _session.Unlocked!;

        vault.UpdateEntry(vault.Find(_mail)! with { Password = _replacement });
        vault.UpdateEntry(vault.Find(_shop)! with { Password = _otherReplacement });
        vault.Save();
        shell.Recommendations.Check();
        await shell.Recommendations.Checking;

        Assert.Equal([("services/Cloud", "Expired item")], shell.Recommendations.Passwords.Select(row => (row.Entry, row.Problem)));
        Assert.Equal("1", Settings(shell).Count);
    }

    [Fact]
    public async Task Open_item_shows_the_item_selected_in_its_group_found_by_its_uuid_after_a_rename()
    {
        using var shell = Shell();
        await shell.Recommendations.Checking;
        shell.Current = Destinations.Of(DestinationKind.Settings);
        var weak = shell.Recommendations.Passwords.Single(row => row.Finding.Entry == _mail);

        Assert.Equal("Open services/Mail in Items", weak.OpenLabel);
        Assert.Equal(OrganizeOutcome.Renamed, _session.Unlocked!.RenameEntry(_mail, "Mailbox", out var renamed));

        weak.OpenCommand.Execute(null);

        var entries = Assert.IsType<EntriesViewModel>(shell.Content);
        Assert.Equal(DestinationKind.Entries, shell.Current.Kind);
        Assert.Equal("services", entries.SelectedGroup?.Path);
        Assert.Equal(renamed, entries.Selected?.Name);
        Assert.Equal("weak", weak.Finding.Key);
    }

    [Fact]
    public async Task Open_item_says_so_when_the_item_has_left_the_vault()
    {
        using var shell = Shell();
        await shell.Recommendations.Checking;
        shell.Current = Destinations.Of(DestinationKind.Settings);
        var weak = shell.Recommendations.Passwords.Single(row => row.Finding.Entry == _mail);

        _session.Unlocked!.RemoveEntry(_mail, out _);
        weak.OpenCommand.Execute(null);

        Assert.Equal(DestinationKind.Settings, shell.Current.Kind);
        Assert.Equal("services/Mail is no longer in this vault.", shell.Recommendations.Message);
    }

    private static void Add(Vault vault, EntryName name, string password) =>
        vault.AddEntry(new VaultEntry { GroupPath = name.GroupPath, Title = name.Title, Password = password });

    private static NavItem Settings(ShellViewModel shell) =>
        shell.FooterNav.Single(item => item.Destination.Kind == DestinationKind.Settings);

    private ShellViewModel Shell() =>
        new(_session, _fixture.Home, authority: null, clock: _clock, post: null);

    private void Unlock()
    {
        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_fixture.Path_, master.Value));
    }
}
