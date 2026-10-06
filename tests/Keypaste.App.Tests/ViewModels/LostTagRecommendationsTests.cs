using KeePassLib;
using KeePassLib.Security;
using Keypaste.App.Navigation;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// V.11 in the app: a project tag another app dropped while changing the entry is listed under
/// Settings and counted quietly, goes back only after the person has seen what it reaches, and a
/// dismissal is remembered; a tag removed in keypaste is not listed.
/// </summary>
public sealed class LostTagRecommendationsTests : IDisposable
{
    private const string _oldValue = "sk_live_APP_LOST_TAG_old";
    private const string _newValue = "sk_live_APP_LOST_TAG_new";

    private static readonly EntryName _billing = new("services", "Billing");
    private static readonly EntryName _web = new("services", "Web");
    private static readonly DateTime _dropped = new(2030, 1, 1, 9, 0, 0, DateTimeKind.Utc);

    private readonly TempVault _fixture = new();
    private readonly ManualClock _clock = new(AppClock.Start);
    private readonly AppVaultSession _session;

    public LostTagRecommendationsTests()
    {
        using (var vault = Vault.Open(_fixture.Path_, TempVault.Password))
        {
            vault.AddEntry(new VaultEntry { GroupPath = _billing.GroupPath, Title = _billing.Title, Password = _oldValue });
            Assert.True(vault.SetFields(_billing, [new FieldWrite("STRIPE_SECRET_KEY", _oldValue)]));
            Assert.True(vault.AddTag(_billing, "env:billing:prod"));
            vault.AddEntry(new VaultEntry { GroupPath = _web.GroupPath, Title = _web.Title, Password = "p" });
            Assert.True(vault.AddTag(_web, "env:web"));
            Assert.True(vault.RemoveTags(_web, ["env:web"]));
            vault.Save();
        }

        ForeignEdit.Apply(_fixture.Path_, TempVault.Password, _billing.Title, _dropped, entry =>
        {
            entry.Tags = [];
            entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, _newValue));
        });

        _session = new AppVaultSession(_clock);
        Unlock();
    }

    public void Dispose()
    {
        _session.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public void Unlocking_lists_the_tag_another_app_dropped_and_counts_it_quietly_on_settings()
    {
        using var shell = Shell();

        var lost = Assert.Single(shell.Recommendations.LostTags);
        Assert.Equal("env:billing:prod", lost.Tag);
        Assert.Equal("Needs review", lost.StateLabel);
        Assert.StartsWith("dropped from services/Billing in a change saved ", lost.Where, StringComparison.Ordinal);
        Assert.EndsWith(", so agents are no longer asked live for it", lost.Where, StringComparison.Ordinal);
        Assert.Equal("1", Settings(shell).Count);
        Assert.Null(shell.Notice);
    }

    [Fact]
    public void Restore_says_what_the_tag_reaches_first_and_then_puts_it_back_in_one_revision()
    {
        using var shell = Shell();
        var lost = Assert.Single(shell.Recommendations.LostTags);
        var bytes = File.ReadAllBytes(_fixture.Path_);
        var history = _session.Unlocked!.ReadHistory(_billing)!.Count;

        lost.RestoreCommand.Execute(null);

        Assert.True(lost.IsConfirming);
        Assert.Equal(
            ["env:billing:prod puts services/Billing in billing/prod, a protected environment whose every release is asked live.", "Joining billing/prod: STRIPE_SECRET_KEY."],
            lost.Confirmation);
        Assert.Equal(bytes, File.ReadAllBytes(_fixture.Path_));

        lost.ConfirmRestoreCommand.Execute(null);

        var vault = _session.Unlocked!;
        Assert.Equal(["env:billing:prod"], vault.Tags(_billing));
        Assert.Equal(history + 1, vault.ReadHistory(_billing)!.Count);
        Assert.StartsWith("Put env:billing:prod back on services/Billing.", shell.Recommendations.Message, StringComparison.Ordinal);
        Assert.Empty(shell.Recommendations.LostTags);
        Assert.Equal(string.Empty, Settings(shell).Count);
    }

    [Fact]
    public void A_dismissal_survives_a_lock_and_unlock_and_review_again_lists_it_again()
    {
        using (var shell = Shell())
        {
            Assert.Single(shell.Recommendations.LostTags).DismissCommand.Execute(null);

            Assert.Equal(string.Empty, Settings(shell).Count);
            Assert.Equal("Dismissed", Assert.Single(shell.Recommendations.LostTags).StateLabel);
        }

        var stored = File.ReadAllText(KeypasteHome.RecommendationsPath(_fixture.Home));
        Assert.Contains("lost-project-tag", stored, StringComparison.Ordinal);
        Assert.DoesNotContain(_oldValue, stored, StringComparison.Ordinal);
        Assert.DoesNotContain(_newValue, stored, StringComparison.Ordinal);
        Assert.DoesNotContain("Billing", stored, StringComparison.Ordinal);

        _session.Lock(VaultLockReason.Manual);
        Unlock();

        using var again = Shell();
        var dismissed = Assert.Single(again.Recommendations.LostTags);
        Assert.True(dismissed.IsDismissed);

        dismissed.ReviewAgainCommand.Execute(null);

        Assert.Equal("1", Settings(again).Count);
    }

    [Fact]
    public void A_tag_removed_in_keypaste_is_not_listed()
    {
        using var shell = Shell();

        Assert.DoesNotContain(shell.Recommendations.LostTags, row => row.Finding.Entry == _web);

        var vault = _session.Unlocked!;
        Assert.True(vault.AddTag(_web, "env:web:staging"));
        Assert.True(vault.RemoveTags(_web, ["env:web:staging"]));
        vault.Save();
        shell.Recommendations.Check();

        Assert.Single(shell.Recommendations.LostTags);
        Assert.DoesNotContain(shell.Recommendations.LostTags, row => row.Finding.Entry == _web);
    }

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
