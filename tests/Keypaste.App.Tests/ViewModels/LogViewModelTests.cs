using Keypaste.App.ViewModels;
using Keypaste.Core.Audit;
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
    private readonly TempAuditHome _home = new();

    public void Dispose() => _home.Dispose();

    /// <summary>
    /// The rows are <see cref="AuditReader"/>'s records, newest first, and the footer counts the file's records, so
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
        Assert.All(model.Rows, row => Assert.True(row.Verified));
        Assert.Equal("2 records", model.Summary);
        Assert.Equal(_home.LogPath, model.LogPath);
        Assert.False(model.HasNotes);
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
        Assert.Equal("1 record", model.Summary);
    }

    /// <summary>A record keypaste-mcp appends while the window is open shows up on a refresh.</summary>
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
        model.RefreshCommand.Execute(null);

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

        model.VerifyCommand.Execute(null);

        var report = AuditChainVerifier.Verify(_home.LogPath);
        Assert.True(model.VerdictShown);
        Assert.Equal("Chain verified · 1 record · latest seq 1", model.Verdict!.Headline);
        Assert.Equal(report.LatestHash, model.Verdict.Hash);
        Assert.Equal(LogTone.Ok, model.Verdict.Tone);
        Assert.Equal(_home.LogPath, model.Verdict.Path);
        Assert.DoesNotContain(model.Verdict.Paragraphs, paragraph => paragraph.Contains('\n', StringComparison.Ordinal));

        // It described the file as it was read a moment ago, so a fresh read folds it away.
        model.Refresh();
        Assert.False(model.VerdictShown);
    }

    /// <summary>
    /// An edited file says so on load, still shows its records, and marks the ones the chain cannot vouch for.
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

        Assert.True(model.HasMessage);
        Assert.True(model.IsBroken);
        Assert.Contains("edited", model.Message, StringComparison.Ordinal);
        Assert.Equal(2, model.Rows.Count);
        Assert.True(model.Rows[^1].Unverified);
        Assert.True(model.HasUnverifiedNote);
        Assert.Equal(LogTone.Danger, model.Verdict!.Tone);
        Assert.Equal("Chain broken at seq 1", model.Verdict.Headline);
        Assert.Contains(AuditText.Describe(AuditChainFault.Altered), Assert.Single(model.Verdict.Breaks), StringComparison.Ordinal);
    }

    /// <summary>
    /// Agents keeps what agents and tokens asked for, approvals included, You keeps only what the person did
    /// themselves, so the two never share a row; Denied keeps every refusal, and the footer says which, with its counts.
    /// </summary>
    [Fact]
    public void Each_filter_keeps_its_records_and_says_so()
    {
        _home.Write(
            Bridge(AuditDecision.Granted, AuditMethod.Prompt, "env/acme-api/dev/DATABASE_URL"),
            Bridge(AuditDecision.Granted, AuditMethod.Policy, "env/acme-api/dev/REDIS_URL"),
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
        Assert.Equal("2 of 5 records · refused calls only", model.Summary);
        Assert.False(model.FilterEmpty);
    }

    /// <summary>A filter that keeps nothing says so in the table rather than drawing an empty one.</summary>
    [Fact]
    public void A_filter_that_keeps_nothing_says_so()
    {
        _home.Append("env/dev/STRIPE_KEY");

        var model = new LogViewModel(_home.Home);
        model.Filter = model.Filters.Single(option => option.Filter == LogFilter.You);

        Assert.True(model.FilterEmpty);
        Assert.Equal("0 of 1 record · your own actions only", model.Summary);
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
        Args = AuditArgs.ForCredentialRequest(entry, "password", ttlSeconds: 60, "run the migration"),
        Decision = decision,
        Method = method,
        Reason = TempAuditHome.Reason,
    };
}
