using System.Collections.Concurrent;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// V-C.2 in the app: keys left in notes are listed only under Security, one finding for each item's keys, counted
/// quietly, moved together in one revision, remembered when dismissed, and never shown by value.
/// </summary>
public sealed class RecommendationsTests : IDisposable
{
    // Strong and used once each, so the keys in notes are all the list holds (V.9).
    private const string _stripeLogin = "stripe-login-Q7v2Lm9X";
    private const string _plainLogin = "plain-login-W4k8Rz3N";

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
    private readonly ManualClock _clock = new(AppClock.Start);
    private readonly AppVaultSession _session;

    public RecommendationsTests()
    {
        using (var vault = Vault.Open(_fixture.Path_, TempVault.Password))
        {
            vault.AddEntry(new VaultEntry { GroupPath = _stripe.GroupPath, Title = _stripe.Title, Password = _stripeLogin, Notes = _notes });
            vault.AddEntry(new VaultEntry { GroupPath = _plain.GroupPath, Title = _plain.Title, Password = _plainLogin, Notes = "Recovery codes are in the safe." });
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
    public async Task Unlocking_lists_one_items_three_keys_as_one_finding_and_counts_it_quietly()
    {
        using var shell = Shell();
        await shell.Recommendations.Checking;

        var row = Assert.Single(shell.Recommendations.Rows);
        Assert.Equal(["STRIPE_SECRET_KEY", "OPENAI_API_KEY", "GITHUB_TOKEN"], Keys(shell.Recommendations));
        Assert.True(row.NeedsReview);
        Assert.Equal("Stripe", row.Entry);
        Assert.Equal("services/Stripe", row.EntryPath);
        Assert.Equal("3 keys in the notes of Stripe", row.Heading);
        Assert.Equal("GitHub token", row.Keys[2].TokenKind);
        Assert.False(shell.Recommendations.OffersBulkMove);

        var settings = Settings(shell);
        Assert.Equal("1", settings.Count);
        Assert.Null(shell.Notice);
        Assert.Empty(shell.Toasts.Shown);
    }

    [Fact]
    public async Task Moving_every_key_writes_protected_fields_in_one_revision_and_empties_the_list()
    {
        using var shell = Shell();
        await shell.Recommendations.Checking;
        var list = shell.Recommendations;

        Assert.Single(list.Rows).MoveCommand.Press();
        await list.Checking;

        Assert.StartsWith("Moved 3 keys from the notes of Stripe", list.Message, StringComparison.Ordinal);
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
    public async Task A_dismissal_survives_a_lock_and_unlock_and_review_again_restores_it()
    {
        using (var shell = Shell())
        {
            await shell.Recommendations.Checking;

            Assert.Single(shell.Recommendations.Rows).DismissCommand.Press();

            Assert.Equal(string.Empty, Settings(shell).Count);
            Assert.True(Assert.Single(shell.Recommendations.Rows).IsDismissed);
        }

        var stored = File.ReadAllText(KeypasteHome.RecommendationsPath(_fixture.Home));
        Assert.Contains("OPENAI_API_KEY", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-proj-2", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("Stripe", stored, StringComparison.Ordinal);

        _session.Lock(VaultLockReason.Manual);
        Unlock();

        using var again = Shell();
        await again.Recommendations.Checking;
        Assert.Equal(string.Empty, Settings(again).Count);
        var dismissed = again.Recommendations.Rows.Single(row => row.IsDismissed);
        Assert.Equal(3, dismissed.Keys.Count);

        dismissed.ReviewAgainCommand.Press();

        Assert.Equal("1", Settings(again).Count);
        Assert.DoesNotContain(again.Recommendations.Rows, row => row.IsDismissed);
    }

    [Fact]
    public async Task Notes_edited_between_the_check_and_the_move_refuse_the_move_and_write_nothing()
    {
        var posted = new ConcurrentQueue<Action>();
        using var shell = Shell(post: posted.Enqueue);
        await shell.Recommendations.Checking;
        Run(posted);

        // The save's own check stays posted and unrun, so the list keeps the findings from before the edit.
        var vault = _session.Unlocked!;
        vault.UpdateEntry(vault.Find(_stripe)! with { Notes = _notes + "\nedited" });
        vault.Save();
        var bytes = File.ReadAllBytes(_fixture.Path_);
        var history = vault.ReadHistory(_stripe)!.Count;

        shell.Recommendations.Rows[0].MoveCommand.Press();

        Assert.StartsWith("The notes of Stripe changed after they were checked, so nothing was moved.", shell.Recommendations.Message, StringComparison.Ordinal);
        Assert.Equal(bytes, File.ReadAllBytes(_fixture.Path_));
        Assert.Equal(history, vault.ReadHistory(_stripe)!.Count);
        Assert.Empty(vault.Fields(_stripe)!);
    }

    [Fact]
    public async Task An_unreadable_dismissals_file_still_lists_every_key_and_is_never_replaced()
    {
        var path = KeypasteHome.RecommendationsPath(_fixture.Home);
        File.WriteAllText(path, "not json");

        using var shell = Shell();
        await shell.Recommendations.Checking;
        Assert.Equal(1, shell.Recommendations.NeedsReview);

        shell.Recommendations.Rows[0].DismissCommand.Press();

        Assert.Contains("could not be read", shell.Recommendations.Message, StringComparison.Ordinal);
        Assert.Equal("not json", File.ReadAllText(path));
    }

    [Fact]
    public async Task A_save_after_an_access_change_still_checks_the_notes_again()
    {
        using var shell = Shell();
        await shell.Recommendations.Checking;

        using (var current = TempVault.Secret(TempVault.Password))
        {
            var result = _session.ChangeAccess(current.Value, new VaultAccessChange(true, AccessKeyfileChange.Keep), "a-new-master-password", "a-new-master-password");
            Assert.Equal(AccessChangeOutcome.Changed, result.Outcome);
        }

        var vault = _session.Unlocked!;
        vault.UpdateEntry(vault.Find(_plain)! with { Notes = "NEW_KEY=fresh" });
        vault.Save();
        await shell.Recommendations.Checking;

        Assert.Contains("NEW_KEY", Keys(shell.Recommendations));
    }

    /// <summary>
    /// With no UI thread to post to, a check shows its findings on the thread that asked for it before returning, so
    /// nothing else touches the list while its owner goes on.
    /// </summary>
    [Fact]
    public async Task With_no_UI_thread_a_check_shows_its_findings_on_the_thread_that_asked()
    {
        var (asked, shown, rows) = await Task.Run(() =>
        {
            using var list = new RecommendationsViewModel(_session, _fixture.Home);
            var shownOn = 0;
            list.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(RecommendationsViewModel.Rows))
                {
                    shownOn = Environment.CurrentManagedThreadId;
                }
            };

            list.Check();
            return (Environment.CurrentManagedThreadId, shownOn, Keys(list).Count);
        });

        Assert.Equal(asked, shown);
        Assert.Equal(3, rows);
    }

    /// <summary>
    /// A check reads the vault off the UI thread and shows its findings through the shell's post; of two checks in a
    /// row only the later one's are shown, and a check a lock overtook shows nothing.
    /// </summary>
    [Fact]
    public async Task Of_two_checks_in_a_row_only_the_later_findings_are_shown()
    {
        var posted = new ConcurrentQueue<Action>();
        using var list = new RecommendationsViewModel(_session, _fixture.Home, post: posted.Enqueue);
        await list.Checking;
        Run(posted);
        Assert.Equal(3, Keys(list).Count);

        var vault = _session.Unlocked!;
        vault.UpdateEntry(vault.Find(_plain)! with { Notes = "FIRST_KEY=one" });
        list.Check();
        await list.Checking;
        vault.UpdateEntry(vault.Find(_plain)! with { Notes = "SECOND_KEY=two" });
        list.Check();
        await list.Checking;

        Assert.True(posted.TryDequeue(out var superseded));
        superseded();
        Assert.Equal(3, Keys(list).Count);

        Assert.True(posted.TryDequeue(out var latest));
        latest();
        Assert.Contains("SECOND_KEY", Keys(list));
        Assert.DoesNotContain("FIRST_KEY", Keys(list));

        vault.UpdateEntry(vault.Find(_plain)! with { Notes = "THIRD_KEY=three" });
        list.Check();
        await list.Checking;
        _session.Lock(VaultLockReason.Manual);

        Assert.True(posted.TryDequeue(out var overtaken));
        overtaken();
        Assert.DoesNotContain("THIRD_KEY", Keys(list));
        Assert.True(posted.IsEmpty);
    }

    [Fact]
    public async Task Unticked_keys_stay_in_the_notes_while_the_rest_move_together()
    {
        using var shell = Shell();
        await shell.Recommendations.Checking;
        var row = Assert.Single(shell.Recommendations.Rows);

        row.Keys.Single(key => key.Key == "OPENAI_API_KEY").IsIncluded = false;
        row.MoveCommand.Press();
        await shell.Recommendations.Checking;

        Assert.StartsWith("Moved 2 keys from the notes of Stripe", shell.Recommendations.Message, StringComparison.Ordinal);
        Assert.Equal(["OPENAI_API_KEY"], Keys(shell.Recommendations));
        var vault = _session.Unlocked!;
        Assert.Equal(["GITHUB_TOKEN", "STRIPE_SECRET_KEY"], vault.Fields(_stripe)!.Select(field => field.Name).Order(StringComparer.Ordinal));
        Assert.Single(vault.ReadHistory(_stripe)!);
    }

    [Fact]
    public async Task Moving_into_a_stage_shows_what_it_brings_then_tags_the_item_in_the_same_revision()
    {
        var vault = _session.Unlocked!;
        vault.AddEntry(new VaultEntry { GroupPath = "env/billing", Title = ".env.staging", Password = _plainLogin + "-billing" });
        vault.AddTag(new EntryName("env/billing", ".env.staging"), "env:billing:staging");
        vault.Save();
        using var shell = Shell();
        await shell.Recommendations.Checking;
        var row = Assert.Single(shell.Recommendations.Rows);

        row.AskMoveToStageCommand.Press();
        Assert.True(row.IsChoosingStage);
        row.ChosenStage = row.Stages.Single(stage => stage.Environment == "staging");
        row.Keys.Single(key => key.Key == "GITHUB_TOKEN").IsIncluded = false;

        Assert.Equal("Stripe joins billing/staging with OPENAI_API_KEY, STRIPE_SECRET_KEY.", row.StagePreview);

        row.ConfirmMoveToStageCommand.Press();
        await shell.Recommendations.Checking;

        Assert.StartsWith("Moved 2 keys from the notes of Stripe into billing/staging.", shell.Recommendations.Message, StringComparison.Ordinal);
        Assert.Equal(["env:billing:staging"], vault.Tags(_stripe));
        Assert.Equal("sk_test_1", vault.ReadField(_stripe, "STRIPE_SECRET_KEY"));
        Assert.Single(vault.ReadHistory(_stripe)!);
        Assert.Equal(["GITHUB_TOKEN"], Keys(shell.Recommendations));
    }

    private static List<string> Keys(RecommendationsViewModel list) =>
        [.. list.Rows.SelectMany(row => row.Keys).Select(key => key.Key)];

    private static void Run(ConcurrentQueue<Action> posted)
    {
        while (posted.TryDequeue(out var action))
        {
            action();
        }
    }

    private static NavItem Settings(ShellViewModel shell) =>
        shell.SecurityPlace;

    private ShellViewModel Shell(Action<Action>? post = null) =>
        new(_session, _fixture.Home, authority: null, clock: _clock, post: post);

    private void Unlock()
    {
        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_fixture.Path_, master.Value));
    }
}
