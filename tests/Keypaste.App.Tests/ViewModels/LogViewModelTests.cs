using Keypaste.App.Resources;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Browser;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// What the activity view promises: its rows are the records the core's reader parsed, the sentences about the file
/// are the core's, it needs no vault, and a file the chain cannot vouch for says so.
/// </summary>
/// <remarks>
/// Not one of these unlocks a vault: the audit log is machine state, and every record below was written by a
/// fixture through the real writer with no vault anywhere near it.
/// </remarks>
public sealed class LogViewModelTests : IDisposable
{
    private const string _vault = "0123456789abcdef";
    private static readonly byte[] _vaultKey = [.. Enumerable.Range(1, 32).Select(i => (byte)i)];
    private readonly TempAuditHome _home = new();

    public void Dispose() => _home.Dispose();

    /// <summary>
    /// The rows are <see cref="AuditReader"/>'s records, newest first, and the status counts the file's records, so
    /// the table cannot drift from what <c>keypaste log</c> reads.
    /// </summary>
    [Fact]
    public void The_rows_are_the_core_readers_records_newest_first()
    {
        _home.Append("env/dev/STRIPE_KEY", "env/prod/GITHUB_TOKEN");

        var model = new LogViewModel(_home.Home);

        Assert.True(AuditReader.TryRead(_home.LogPath, out var entries, out _, out var error), error);

        Assert.Equal(entries.Reverse(), model.Rows.Select(row => row.Source));
        Assert.Equal("GITHUB_TOKEN", model.Rows[0].Secrets);
        Assert.Equal("prod", model.Rows[0].Where);
        Assert.Equal("env/prod", model.Rows[0].WhereInFull);
        Assert.All(model.Rows, row => Assert.True(row.Verified));
        Assert.Equal("Log intact · 2 records", model.Status);
        Assert.Equal(_home.LogPath, model.LogPath);
        Assert.False(model.HasUnreadableNote);
    }

    /// <summary>Rows run newest first by their full time, whatever order the file has them in, so one day's section holds that day alone.</summary>
    [Fact]
    public void Rows_run_newest_first_by_time_and_each_day_is_one_section()
    {
        _home.AppendAt(AppClock.Start.AddDays(-1).AddHours(5), "env/dev/YESTERDAY_LATE");
        _home.AppendAt(AppClock.Start.AddHours(-5), "env/dev/TODAY_EARLY");
        _home.AppendAt(AppClock.Start.AddDays(-1), "env/dev/YESTERDAY_EARLY");
        _home.AppendAt(AppClock.Start, "env/dev/TODAY_LATE");

        var model = new LogViewModel(_home.Home, new FixedClock(AppClock.Start.AddHours(1)));

        Assert.Equal(["TODAY_LATE", "TODAY_EARLY", "YESTERDAY_LATE", "YESTERDAY_EARLY"], model.Rows.Select(row => row.Secrets));
        Assert.Equal(["Today", null, "Yesterday", null], model.Rows.Select(row => row.Day));
    }

    /// <summary>A record whose time does not parse goes below every dated one, a later line first, wherever the file has it.</summary>
    [Fact]
    public void Undated_rows_run_last_and_newest_line_first()
    {
        _home.AppendAt(AppClock.Start.AddHours(-2), "env/dev/FIRST");
        _home.AppendAt(AppClock.Start.AddHours(-1), "env/dev/SECOND");
        _home.AppendAt(AppClock.Start, "env/dev/THIRD");
        _home.AppendAt(AppClock.Start.AddHours(1), "env/dev/FOURTH");
        Undate(1, 3);

        var model = new LogViewModel(_home.Home, new FixedClock(AppClock.Start.AddHours(2)));

        Assert.Equal(["FOURTH", "SECOND", "THIRD", "FIRST"], model.Rows.Select(row => row.Secrets));
        Assert.Equal([false, false, true, true], model.Rows.Select(row => row.Source.At is null));
    }

    /// <summary>A table that runs over several days carries each day once, above its first row.</summary>
    [Fact]
    public void Each_day_is_divided_once()
    {
        _home.Append("env/dev/A", "env/dev/B");

        var model = new LogViewModel(_home.Home, new FixedClock(new DateTimeOffset(2026, 7, 29, 10, 0, 0, TimeSpan.Zero)));

        Assert.Equal("Yesterday", model.Rows[0].Day);
        Assert.Null(model.Rows[1].Day);

        var same = new LogViewModel(_home.Home, new FixedClock(new DateTimeOffset(2026, 7, 28, 23, 0, 0, TimeSpan.Zero)));

        Assert.All(same.Rows, row => Assert.False(row.StartsDay));
    }

    /// <summary>A machine no agent has asked anything of is normal, and is told so in one sentence.</summary>
    [Fact]
    public void An_absent_log_is_a_sentence_rather_than_an_error()
    {
        var model = new LogViewModel(_home.Home);

        Assert.False(File.Exists(_home.LogPath));
        Assert.Empty(model.Rows);
        Assert.False(model.HasTable);
        Assert.True(model.IsEmpty);
        Assert.False(model.HasNotice);
        Assert.Equal(LogViewModel.NothingYet, model.Message);
        Assert.Null(model.Verdict);
        Assert.False(model.VerifyCommand.CanExecute(null));
    }

    [Fact]
    public void An_empty_log_offers_to_connect_an_AI_tool_where_something_can_take_it_there()
    {
        var opened = false;
        var model = new LogViewModel(_home.Home, connect: () => opened = true);
        var alone = new LogViewModel(_home.Home);

        Assert.True(model.OffersConnect);
        Assert.False(alone.OffersConnect);

        model.ConnectCommand.Press();

        Assert.True(opened);
    }

    /// <summary>A log with records produces the table, and says nothing alarming about an intact one.</summary>
    [Fact]
    public void A_log_with_records_produces_rows()
    {
        _home.Append("env/dev/STRIPE_KEY");

        var model = new LogViewModel(_home.Home);

        Assert.True(model.HasTable);
        Assert.True(model.HasRows);
        Assert.False(model.HasMessage);
        Assert.Equal("STRIPE_KEY", Assert.Single(model.Rows).Secrets);
        Assert.Equal("Log intact · 1 record", model.Status);
    }

    /// <summary>A record the bridge appends while the window is open shows up on a refresh.</summary>
    [Fact]
    public void Refresh_picks_up_an_appended_record()
    {
        _home.Append("env/dev/STRIPE_KEY");

        var model = new LogViewModel(_home.Home);

        _home.Append("env/prod/GITHUB_TOKEN");
        Assert.Single(model.Rows);

        model.Refresh();

        Assert.Equal(2, model.Rows.Count);
        Assert.Equal("GITHUB_TOKEN", model.Rows[0].Secrets);
    }

    /// <summary>The same, when a person presses the button rather than the code calling the method.</summary>
    [Fact]
    public void The_refresh_command_re_reads_the_file()
    {
        _home.Append("env/dev/STRIPE_KEY");

        var model = new LogViewModel(_home.Home);
        _home.Append("env/prod/GITHUB_TOKEN");

        Assert.True(model.RefreshCommand.CanExecute(null));
        model.RefreshCommand.Press();

        Assert.Equal(2, model.Rows.Count);
    }

    /// <summary>The verdict is on hand from the load, and on screen only when it was asked for.</summary>
    [Fact]
    public void The_verdict_waits_to_be_asked_for()
    {
        _home.Append("env/dev/STRIPE_KEY");

        var model = new LogViewModel(_home.Home);

        Assert.False(model.VerdictShown);
        Assert.NotNull(model.Verdict);
        Assert.True(model.VerifyCommand.CanExecute(null));

        model.VerifyCommand.Press();

        var report = AuditChainVerifier.Verify(_home.LogPath);
        Assert.True(model.VerdictShown);
        Assert.Equal("Activity checked: no edits found in 1 record", model.Verdict!.Headline);
        Assert.False(model.Verdict.HasSentence);
        Assert.Equal(report.LatestHash, model.Verdict.Hash);
        Assert.Equal(LogTone.Ok, model.Verdict.Tone);
        Assert.Equal(_home.LogPath, model.Verdict.Path);
        Assert.DoesNotContain(model.Verdict.Paragraphs, paragraph => paragraph.Contains('\n', StringComparison.Ordinal));

        // The limits wait behind Learn more, and Hide folds the verdict away.
        Assert.False(model.LimitsShown);
        model.LearnMoreCommand.Press();
        Assert.True(model.LimitsShown);
        model.HideVerdictCommand.Press();
        Assert.False(model.VerdictShown);
        Assert.False(model.LimitsShown);
        model.VerifyCommand.Press();

        // It described the file as it was read a moment ago, so a fresh read folds it away.
        model.Refresh();
        Assert.False(model.VerdictShown);
    }

    [Fact]
    public void Verify_checks_the_checkpoints_this_vaults_key_signed_and_counts_the_unsigned_tail()
    {
        _home.Append("env/dev/STRIPE_KEY");
        _home.Checkpoint(Key(_vaultKey, _vault));
        _home.Append("env/prod/GITHUB_TOKEN");

        var model = new LogViewModel(_home.Home, checkpointKey: () => Key(_vaultKey, _vault));

        Assert.Equal(LogTone.Ok, model.Verdict!.Tone);
        Assert.Contains("1 checkpoint found, 1 signed by this vault's key. Unsigned tail: 1 record after the last signed checkpoint.", model.Verdict.Paragraphs);
        Assert.Contains(model.Verdict.Paragraphs, paragraph => paragraph.StartsWith("A rewrite that recomputed the chain is caught only before the last checkpoint", StringComparison.Ordinal));
    }

    [Fact]
    public void A_checkpoint_naming_another_vault_is_counted_and_not_checked()
    {
        _home.Append("env/dev/STRIPE_KEY");
        _home.Checkpoint(Key(_vaultKey, "fedcba9876543210"));

        var model = new LogViewModel(_home.Home, checkpointKey: () => Key(_vaultKey, _vault));

        Assert.Equal(LogTone.Ok, model.Verdict!.Tone);
        Assert.Contains(
            "1 checkpoint found, 0 signed by this vault's key. Not checked: 1 checkpoint naming another vault. No checkpoint is signed by this vault's key, so no record is signed.",
            model.Verdict.Paragraphs);
    }

    [Fact]
    public void A_checkpoint_this_vaults_key_did_not_sign_breaks_the_chain()
    {
        _home.Append("env/dev/STRIPE_KEY");
        _home.Checkpoint(Key(new byte[32], _vault));

        var model = new LogViewModel(_home.Home, checkpointKey: () => Key(_vaultKey, _vault));

        Assert.True(model.IsBroken);
        Assert.Equal(LogTone.Danger, model.Verdict!.Tone);
        Assert.Equal("Activity checked: one record was edited", model.Verdict.Headline);
        Assert.Contains(AuditText.Describe(AuditChainFault.Forged), Assert.Single(model.Verdict.Breaks), StringComparison.Ordinal);
        Assert.Contains("1 checkpoint found, 0 signed by this vault's key. No checkpoint is signed by this vault's key, so no record is signed.", model.Verdict.Paragraphs);
    }

    [Fact]
    public void Without_a_checkpoint_key_no_checkpoint_is_checked_and_Verify_says_so()
    {
        _home.Append("env/dev/STRIPE_KEY");
        _home.Checkpoint(Key(_vaultKey, _vault));

        var model = new LogViewModel(_home.Home, checkpointKey: () => null);

        Assert.Equal(LogTone.Ok, model.Verdict!.Tone);
        Assert.Contains("1 checkpoint found, not checked: this vault holds no checkpoint key yet, so the log is not compacted until a save gives it one.", model.Verdict.Paragraphs);
        Assert.Contains(model.Verdict.Paragraphs, paragraph => paragraph.Contains("because no checkpoint was checked", StringComparison.Ordinal));
    }

    /// <summary>
    /// An edited file says so on load in the status, still shows its records, and marks the one where the chain breaks.
    /// </summary>
    /// <remarks>
    /// Saying nothing until somebody pressed a button would let a tampered log look like an untouched one, and
    /// hiding the records would let an attacker make the log unreadable by editing one byte of it.
    /// </remarks>
    [Fact]
    public void A_broken_chain_is_said_without_hiding_the_records()
    {
        _home.Append("env/dev/STRIPE_KEY", "env/prod/GITHUB_TOKEN");
        _home.Alter();

        var model = new LogViewModel(_home.Home);

        Assert.False(model.HasNotice);
        Assert.True(model.IsBroken);
        Assert.True(model.StatusIsEdited);
        Assert.StartsWith("Log edited at ", model.Status, StringComparison.Ordinal);
        Assert.EndsWith(" · 2 records", model.Status, StringComparison.Ordinal);
        Assert.Equal(2, model.Rows.Count);
        Assert.True(model.Rows[^1].Edited);
        Assert.False(model.Rows[^1].Unchecked);
        Assert.Contains("Edited", model.Rows[^1].RowName, StringComparison.Ordinal);
        Assert.False(model.Rows[0].Edited);
        Assert.Equal(LogTone.Danger, model.Verdict!.Tone);
        Assert.Equal("Activity checked: one record was edited", model.Verdict.Headline);
        Assert.EndsWith("doesn't match what keypaste wrote. The records before it are intact.", model.Verdict.Sentence, StringComparison.Ordinal);
        Assert.Contains(AuditText.Describe(AuditChainFault.Altered), Assert.Single(model.Verdict.Breaks), StringComparison.Ordinal);
        Assert.Contains(model.Verdict.Paragraphs, paragraph => paragraph.Contains("keypaste log verify --expect", StringComparison.Ordinal));
        Assert.True(model.VerifyCommand.CanExecute(null));
    }

    /// <summary>
    /// Agents keeps what agents and tokens asked for, approvals included, You keeps only what the person did
    /// themselves, so the two never share a row; Denied keeps every refusal, and the status says which, with its counts.
    /// </summary>
    [Fact]
    public void Each_filter_keeps_its_records_and_says_so()
    {
        _home.Write(
            Bridge(AuditDecision.Granted, AuditMethod.Prompt, "env/acme-api/dev/DATABASE_URL"),
            Bridge(AuditDecision.Granted, AuditMethod.Automatic, "env/acme-api/dev/REDIS_URL"),
            Bridge(AuditDecision.Denied, AuditMethod.TimedOut, "env/infra/prod/AWS_SECRET_ACCESS_KEY"),
            Bridge(AuditDecision.Denied, AuditMethod.Prompt, "Work/github"),
            new AuditRecord
            {
                Tool = "share",
                Client = new AuditClient("keypaste share", "1.0.0", null),
                Args = new AuditArgs { Entry = "Work/github", Field = "password" },
                Decision = AuditDecision.Granted,
                Method = AuditMethod.ShareCreated,
                Reason = "share abc: 1 view, expires 2026-07-29T09:12:44Z, to maya@acme.dev",
            });

        var model = new LogViewModel(_home.Home);

        Assert.Equal(5, model.Rows.Count);

        model.Filter = model.Filters.Single(option => option.Filter == LogFilter.Agents);
        Assert.Equal(["github", "AWS_SECRET_ACCESS_KEY", "REDIS_URL", "DATABASE_URL"], model.Rows.Select(row => row.Secrets));
        Assert.DoesNotContain(model.Rows, row => row.Actor == LogRow.You);

        model.Filter = model.Filters.Single(option => option.Filter == LogFilter.You);
        Assert.Equal("github", Assert.Single(model.Rows).Secrets);
        Assert.Equal(LogRow.You, model.Rows[0].Actor);
        Assert.Equal("maya@acme.dev", model.Rows[0].Where);

        model.Filter = model.Filters.Single(option => option.Filter == LogFilter.Denied);
        Assert.All(model.Rows, row => Assert.True(row.Denied));
        Assert.Equal(["Denied", "No answer"], model.Rows.Select(row => row.Result));
        Assert.Equal("Log intact · 2 of 5 records · refused calls only", model.Status);
        Assert.False(model.FilterEmpty);
    }

    /// <summary>
    /// The browser's requests are the person's, in their browser: Your browser keeps them alone, You keeps them with the
    /// person's shares, and Agents never holds one, so an agent's requests are never mixed with the person's autofill.
    /// </summary>
    [Fact]
    public void Your_browser_keeps_the_browsers_requests_and_agents_never_holds_one()
    {
        _home.Write(
            Bridge(AuditDecision.Granted, AuditMethod.Prompt, "env/acme-api/dev/DATABASE_URL"),
            BrowserFill(AuditMethod.Prompt),
            BrowserFill(AuditMethod.Picked),
            new AuditRecord
            {
                Tool = "share",
                Client = new AuditClient("keypaste share", "1.0.0", null),
                Args = new AuditArgs { Entry = "Work/github", Field = "password" },
                Decision = AuditDecision.Granted,
                Method = AuditMethod.ShareCreated,
                Reason = "share abc: 1 view, expires 2026-07-29T09:12:44Z, to maya@acme.dev",
            });

        var model = new LogViewModel(_home.Home);

        model.ShowBrowser = true;
        Assert.Equal(["you, in Chrome on studio", "you, in Chrome on studio"], model.Rows.Select(row => row.Actor));
        Assert.Equal(["You picked it in the page", Strings.AllowedOnce], model.Rows.Select(row => row.Result));
        Assert.Equal("Log intact · 2 of 4 records · your browser only", model.Status);

        model.ShowAgents = true;
        Assert.Equal("DATABASE_URL", Assert.Single(model.Rows).Secrets);

        model.ShowYou = true;
        Assert.Equal(3, model.Rows.Count);
        Assert.DoesNotContain(model.Rows, row => row.Actor == "claude-code");
    }

    /// <summary>
    /// A page's lookup is hidden when the screen opens, and the status counts it; Show browser lookups lists it again under
    /// every filter that holds it, is offered only where one is, and a fill is never hidden.
    /// </summary>
    [Fact]
    public void The_browsers_lookups_are_hidden_until_asked_for_and_the_status_says_so()
    {
        _home.Write(
            BrowserLookup("listed 2 login(s) by name, no password, on https://github.com"),
            BrowserFill(AuditMethod.Prompt),
            BrowserLookup("listed 1 login(s) by name, no password, on https://linear.app"),
            Bridge(AuditDecision.Granted, AuditMethod.Prompt, "env/acme-api/dev/DATABASE_URL"));

        var model = new LogViewModel(_home.Home);

        Assert.True(model.OffersLookups);
        Assert.False(model.ShowLookups);
        Assert.DoesNotContain(model.Rows, row => row.IsLookup);
        Assert.Equal(["DATABASE_URL", "GitHub · fill"], model.Rows.Select(row => row.Secrets));
        Assert.Equal("Log intact · 2 of 4 records · browser lookups hidden", model.Status);

        model.ShowBrowser = true;
        Assert.Equal("Filled a login", Assert.Single(model.Rows).Action);
        Assert.Equal("Log intact · 1 of 4 records · your browser only · browser lookups hidden", model.Status);

        model.ShowLookups = true;
        Assert.Equal(["Listed logins by name", "Filled a login", "Listed logins by name"], model.Rows.Select(row => row.Action));
        Assert.Equal("Log intact · 3 of 4 records · your browser only", model.Status);

        model.ShowAll = true;
        Assert.Equal(4, model.Rows.Count);
        Assert.Equal("Log intact · 4 records", model.Status);

        model.ShowAgents = true;
        model.ShowLookups = false;
        Assert.Equal("Log intact · 1 of 4 records · agent and token requests only", model.Status);
        Assert.False(model.OffersLookups);
    }

    /// <summary>A day of the browser's lookups folded into one line is the browser's and a lookup: under Your browser, and hidden until asked for.</summary>
    [Fact]
    public void A_folded_browser_day_is_under_your_browser_and_hidden_with_the_lookups()
    {
        _home.Write(
            new AuditRecord
            {
                Tool = AuditTools.Summary,
                Client = new AuditClient(BrowserIdentity.ChromeWebStore.Id, null, "Chrome on studio"),
                Decision = AuditDecision.Granted,
                Method = AuditMethod.Folded,
                Reason = "the vault's owner folded 14 lookups of one day into this line",
                Vault = _vault,
                Summary = new AuditSummary("2026-06-01", "2026-06-01T09:00:00.000Z", "2026-06-01T17:00:00.000Z", [new AuditFoldCount(AuditTools.Browser, AuditDecision.Granted, 14)], new string('a', 64)),
            },
            BrowserFill(AuditMethod.Picked));

        var model = new LogViewModel(_home.Home);
        model.ShowBrowser = true;

        Assert.Equal("Filled a login", Assert.Single(model.Rows).Action);

        model.ShowLookups = true;
        var folded = model.Rows.Single(row => row.Source.Summary is not null);
        Assert.Equal(("you, in Chrome on studio", "14 lookups on 2026-06-01"), (folded.Actor, folded.Subject));

        model.ShowAgents = true;
        Assert.Empty(model.Rows);
    }

    /// <summary>A log of nothing but lookups offers Show all, which lists every line, the lookups included.</summary>
    [Fact]
    public void A_log_of_lookups_alone_offers_show_all_and_show_all_lists_them()
    {
        _home.Write(BrowserLookup("listed 2 login(s) by name, no password, on https://github.com"));

        var model = new LogViewModel(_home.Home);

        Assert.True(model.FilterEmpty);
        Assert.True(model.OffersShowAll);

        model.ShowWholeLogCommand.Execute(null);

        Assert.True(model.ShowLookups);
        Assert.True(Assert.Single(model.Rows).IsLookup);
        Assert.False(model.OffersShowAll);
    }

    /// <summary>A lookup the chain cannot vouch for is never hidden, or a line somebody wrote into the log could pass as one unseen.</summary>
    [Fact]
    public void An_edited_lookup_is_listed_while_lookups_are_hidden()
    {
        _home.Write(
            BrowserLookup(TempAuditHome.Reason),
            BrowserLookup("listed 1 login(s) by name, no password, on https://linear.app"));
        _home.Alter();

        var model = new LogViewModel(_home.Home);

        var shown = Assert.Single(model.Rows);
        Assert.True(shown.IsLookup);
        Assert.True(shown.Edited);
    }

    /// <summary>A filter that keeps nothing says so in the table rather than drawing an empty one.</summary>
    [Fact]
    public void A_filter_that_keeps_nothing_says_so()
    {
        _home.Append("env/dev/STRIPE_KEY");

        var model = new LogViewModel(_home.Home);
        model.Filter = model.Filters.Single(option => option.Filter == LogFilter.You);

        Assert.True(model.FilterEmpty);
        Assert.Equal("Log intact · 0 of 1 record · your own actions only", model.Status);
    }

    /// <summary>Gives the records on the 1-based <paramref name="lines"/> a time that does not parse, as a hand edit could.</summary>
    private void Undate(params int[] lines)
    {
        var records = File.ReadAllText(_home.LogPath).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            const string Key = "\"ts\":\"";
            var written = records[line - 1];
            var start = written.IndexOf(Key, StringComparison.Ordinal);

            Assert.True(start >= 0, written);
            start += Key.Length;
            records[line - 1] = string.Concat(written.AsSpan(0, start), "sometime", written.AsSpan(written.IndexOf('"', start)));
        }

        File.WriteAllText(_home.LogPath, string.Join('\n', records) + '\n');
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static AuditRecord Bridge(AuditDecision decision, AuditMethod method, string entry) => new()
    {
        Tool = "request_credential",
        Client = new AuditClient("claude-code", "1.2.3", null),
        Args = AuditArgs.ForCredentialRequest(entry, "password", "run the migration"),
        Decision = decision,
        Method = method,
        Reason = TempAuditHome.Reason,
    };

    private static AuditRecord BrowserFill(AuditMethod method) =>
        RequestRecords.BrowserFill(BrowserIdentity.ChromeWebStore, "Chrome on studio", new EntryName("Work", "GitHub"), "https://github.com", AuditDecision.Granted, method, "filled", "session-one", _vault);

    private static AuditRecord BrowserLookup(string reason) =>
        RequestRecords.Browser(BrowserIdentity.ChromeWebStore, "Chrome on studio", BrowserAct.List, AuditDecision.Granted, AuditMethod.Exposure, reason, "session-one", _vault);

    /// <summary>A fresh copy each time, since the screen zeroes the key it was handed once it has checked.</summary>
    private static AuditCheckpointKey Key(byte[] key, string vault) => new([.. key], vault);
}
