using Keypaste.App.Navigation;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// V-C.2 in the app: keys left in notes are listed only under Settings, counted quietly, moved in
/// one revision, remembered when dismissed, and never shown by value.
/// </summary>
public sealed class RecommendationsTests : IDisposable
{
    private static readonly string _gitHubToken = "ghp_" + string.Concat(Enumerable.Repeat("kp2c", 9));
    private static readonly EntryName _stripe = new("services", "Stripe");
    private static readonly EntryName _plain = new("services", "Plain");

    private static readonly string _notes = string.Join(
        "\n",
        "STRIPE_SECRET_KEY=sk_test_1",
        "export OPENAI_API_KEY=sk-proj-2",
        _gitHubToken,
        "-----BEGIN PRIVATE KEY-----",
        "MIIEvQIBADANBgkqhkiG9w0BAQEFAASC",
        "-----END PRIVATE KEY-----",
        "Recovery codes are in the safe.");

    private readonly TempVault _fixture = new();
    private readonly ManualClock _clock = new();
    private readonly AppVaultSession _session;

    public RecommendationsTests()
    {
        using (var vault = Vault.Open(_fixture.Path_, TempVault.Password))
        {
            vault.AddEntry(new VaultEntry { GroupPath = _stripe.GroupPath, Title = _stripe.Title, Password = "p", Notes = _notes });
            vault.AddEntry(new VaultEntry { GroupPath = _plain.GroupPath, Title = _plain.Title, Password = "p", Notes = "Recovery codes are in the safe." });
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
    public void Unlocking_lists_three_keys_needing_review_and_counts_them_quietly_on_settings()
    {
        using var shell = Shell();

        Assert.Equal(["STRIPE_SECRET_KEY", "OPENAI_API_KEY", "GITHUB_TOKEN"], shell.Recommendations.Rows.Select(row => row.Key));
        Assert.All(shell.Recommendations.Rows, row => Assert.Equal("Needs review", row.StateLabel));
        Assert.All(shell.Recommendations.Rows, row => Assert.Equal("services/Stripe", row.Entry));
        Assert.Equal("GitHub token", shell.Recommendations.Rows[2].TokenKind);

        var settings = Settings(shell);
        Assert.Equal("3", settings.Count);
        Assert.False(settings.IsLive);
        Assert.Null(shell.Notice);
        Assert.Null(shell.Toast);
    }

    [Fact]
    public void Moving_every_key_writes_protected_fields_in_one_revision_and_empties_the_list()
    {
        using var shell = Shell();
        var list = shell.Recommendations;

        list.SelectAllCommand.Execute(null);
        list.MoveSelectedCommand.Execute(null);

        Assert.StartsWith("Moved 3 keys from the notes of services/Stripe", list.Message, StringComparison.Ordinal);
        Assert.Empty(list.Rows);
        Assert.Equal(string.Empty, Settings(shell).Count);

        var vault = _session.Unlocked!;
        Assert.Equal(SavedRead.Current, vault.ReadSaved(out _));
        Assert.All(vault.Fields(_stripe)!, field => Assert.True(field.IsProtected));
        Assert.Equal("sk_test_1", vault.ReadField(_stripe, "STRIPE_SECRET_KEY"));
        Assert.Equal(_gitHubToken, vault.ReadField(_stripe, "GITHUB_TOKEN"));
        Assert.Equal(
            "-----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBgkqhkiG9w0BAQEFAASC\n-----END PRIVATE KEY-----\nRecovery codes are in the safe.",
            vault.Find(_stripe)!.Notes);
        Assert.Equal(_notes, Assert.Single(vault.ReadHistory(_stripe)!).Fields.Notes);
    }

    [Fact]
    public void A_dismissal_survives_a_lock_and_unlock_and_review_again_restores_it()
    {
        using (var shell = Shell())
        {
            var openAi = shell.Recommendations.Rows.Single(row => row.Key == "OPENAI_API_KEY");
            openAi.DismissCommand.Execute(null);

            Assert.Equal("2", Settings(shell).Count);
            Assert.Equal("Dismissed", shell.Recommendations.Rows[^1].StateLabel);
        }

        var stored = File.ReadAllText(KeypasteHome.RecommendationsPath(_fixture.Home));
        Assert.Contains("OPENAI_API_KEY", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-proj-2", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("Stripe", stored, StringComparison.Ordinal);

        _session.Lock(VaultLockReason.Manual);
        Unlock();

        using var again = Shell();
        Assert.Equal("2", Settings(again).Count);
        var dismissed = again.Recommendations.Rows.Single(row => row.IsDismissed);
        Assert.Equal("OPENAI_API_KEY", dismissed.Key);

        dismissed.ReviewAgainCommand.Execute(null);

        Assert.Equal("3", Settings(again).Count);
        Assert.DoesNotContain(again.Recommendations.Rows, row => row.IsDismissed);
    }

    [Fact]
    public void Notes_edited_between_the_check_and_the_move_refuse_the_move_and_write_nothing()
    {
        List<Action> posted = [];
        using var shell = Shell(post: posted.Add);
        var vault = _session.Unlocked!;
        vault.UpdateEntry(vault.Find(_stripe)! with { Notes = _notes + "\nedited" });
        vault.Save();
        var bytes = File.ReadAllBytes(_fixture.Path_);
        var history = vault.ReadHistory(_stripe)!.Count;

        shell.Recommendations.SelectAllCommand.Execute(null);
        shell.Recommendations.MoveSelectedCommand.Execute(null);

        Assert.StartsWith("The notes of services/Stripe changed after they were checked, so nothing was moved.", shell.Recommendations.Message, StringComparison.Ordinal);
        Assert.Equal(bytes, File.ReadAllBytes(_fixture.Path_));
        Assert.Equal(history, vault.ReadHistory(_stripe)!.Count);
        Assert.Empty(vault.Fields(_stripe)!);
    }

    [Fact]
    public void A_field_holding_another_value_refuses_the_move_and_writes_nothing()
    {
        var vault = _session.Unlocked!;
        vault.SetFields(_stripe, [new FieldWrite("STRIPE_SECRET_KEY", "sk_test_other")]);
        vault.Save();
        using var shell = Shell();
        var bytes = File.ReadAllBytes(_fixture.Path_);

        shell.Recommendations.Rows.Single(row => row.Key == "STRIPE_SECRET_KEY").MoveCommand.Execute(null);

        Assert.StartsWith("services/Stripe already has a STRIPE_SECRET_KEY field with a different value", shell.Recommendations.Message, StringComparison.Ordinal);
        Assert.Equal(bytes, File.ReadAllBytes(_fixture.Path_));
        Assert.Equal("sk_test_other", vault.ReadField(_stripe, "STRIPE_SECRET_KEY"));
    }

    [Fact]
    public void An_unreadable_dismissals_file_still_lists_every_key_and_is_never_replaced()
    {
        var path = KeypasteHome.RecommendationsPath(_fixture.Home);
        File.WriteAllText(path, "not json");

        using var shell = Shell();
        Assert.Equal(3, shell.Recommendations.NeedsReview);

        shell.Recommendations.Rows[0].DismissCommand.Execute(null);

        Assert.Contains("could not be read", shell.Recommendations.Message, StringComparison.Ordinal);
        Assert.Equal("not json", File.ReadAllText(path));
    }

    [Fact]
    public void Searching_for_a_value_left_in_notes_finds_nothing()
    {
        using var shell = Shell();
        var entries = Assert.IsType<EntriesViewModel>(shell.Content);

        entries.Search = "sk_test_1";

        Assert.Empty(entries.Rows);
    }

    [Fact]
    public void A_save_after_an_access_change_still_checks_the_notes_again()
    {
        using var shell = Shell();

        using (var current = TempVault.Secret(TempVault.Password))
        {
            var result = _session.ChangeAccess(current.Value, new VaultAccessChange(true, AccessKeyfileChange.Keep), "a-new-master-password", "a-new-master-password");
            Assert.Equal(AccessChangeOutcome.Changed, result.Outcome);
        }

        var vault = _session.Unlocked!;
        vault.UpdateEntry(vault.Find(_plain)! with { Notes = "NEW_KEY=fresh" });
        vault.Save();

        Assert.Contains(shell.Recommendations.Rows, row => row.Key == "NEW_KEY");
    }

    private static NavItem Settings(ShellViewModel shell) =>
        shell.FooterNav.Single(item => item.Destination.Kind == DestinationKind.Settings);

    private ShellViewModel Shell(Action<Action>? post = null) =>
        new(_session, _fixture.Home, authority: null, clock: _clock, post: post);

    private void Unlock()
    {
        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_fixture.Path_, master.Value));
    }
}
