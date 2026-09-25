using Keypaste.App.ViewModels;
using Keypaste.Core.Audit;
using Keypaste.Core.Sharing;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>How one audit record reads in the Activity table: who, what, which secrets, where, and what came of it.</summary>
public sealed class LogRowTests
{
    private static readonly DateTimeOffset _now = new(2026, 7, 28, 14, 40, 0, TimeSpan.Zero);

    private static LogRow Row(AuditEntry entry, bool verified = true) =>
        LogRow.From(entry, verified, new UtcClock(_now));

    private static AuditEntry Entry(string tool, string decision, string method) => new()
    {
        Line = 1,
        At = _now.AddMinutes(-8),
        Timestamp = "2026-07-28T14:32:08Z",
        Client = "claude-code",
        Name = "claude-code",
        Tool = tool,
        Decision = decision,
        Method = method,
        Reason = "approved by the person at the keyboard",
    };

    [Theory]
    [InlineData(3600, "Approved 1h")]
    [InlineData(900, "Approved 15m")]
    [InlineData(0, "Approved once")]
    public void A_person_s_approval_says_for_how_long(int seconds, string result)
    {
        var row = Row(Entry("request_credential", "granted", "prompt") with { Entry = "env/acme-api/dev/DATABASE_URL", Field = "password", GrantedSeconds = seconds });

        Assert.Equal(result, row.Result);
        Assert.Equal(LogTone.Ok, row.Tone);
        Assert.Equal("Requested", row.Action);
        Assert.Equal("DATABASE_URL", row.Secrets);
        Assert.Equal("acme-api · dev", row.Where);
        Assert.Equal("claude-code", row.Actor);
        Assert.False(row.ByYou);
    }

    [Fact]
    public void A_refusal_is_denied_in_danger()
    {
        var row = Row(Entry("request_credential", "denied", "prompt") with { Entry = "Work/github", Field = "username" });

        Assert.Equal("Denied", row.Result);
        Assert.Equal(LogTone.Danger, row.Tone);
        Assert.Equal("github · username", row.Secrets);
        Assert.Equal("Work", row.Where);
        Assert.True(row.Denied);
    }

    /// <summary>Nobody refused a prompt nobody answered, so it is not drawn as a person's refusal, though it was one.</summary>
    [Fact]
    public void A_prompt_nobody_answered_is_a_muted_refusal()
    {
        var row = Row(Entry("request_credential", "denied", "timed-out") with { Entry = "Work/github", Field = "password" });

        Assert.Equal("No answer", row.Result);
        Assert.Equal(LogTone.Muted, row.Tone);
        Assert.True(row.Denied);
    }

    [Fact]
    public void A_token_run_is_named_by_its_token_and_lists_what_it_injected()
    {
        var row = Row(Entry("run", "granted", "token") with
        {
            Client = "keypaste run --token",
            Entry = "env/acme-api/staging",
            Reason = "token t1 'ci-github-actions': 4 variable(s)",
            Entries = ["env/acme-api/staging/A", "env/acme-api/staging/B", "env/acme-api/staging/C", "env/acme-api/staging/D"],
        });

        Assert.Equal("ci-github-actions", row.Actor);
        Assert.Equal("Injected", row.Action);
        Assert.Equal("4 secrets", row.Secrets);
        Assert.Equal("acme-api · staging", row.Where);
        Assert.Equal("Token", row.Result);
        Assert.Equal(LogTone.Info, row.Tone);
        Assert.False(row.ByYou);
    }

    [Fact]
    public void A_share_is_yours_and_a_link()
    {
        var row = Row(Entry("share", "granted", "share-created") with { Client = "keypaste share", Entry = "env/acme-api/dev/STRIPE_SECRET_KEY", Field = "password" });

        Assert.Equal(LogRow.You, row.Actor);
        Assert.Equal("Shared", row.Action);
        Assert.Equal("STRIPE_SECRET_KEY", row.Secrets);
        Assert.Equal("Link", row.Result);
        Assert.Equal(LogTone.Muted, row.Tone);
        Assert.True(row.ByYou);
    }

    /// <summary>A share names who it went to and for how long, read back through the core's own reason format.</summary>
    [Fact]
    public void A_share_names_its_recipient_and_lifetime()
    {
        var at = _now.AddMinutes(-8);
        var info = new ShareInfo("3a11", "env/acme-api/dev/STRIPE_SECRET_KEY", "password", "maya@acme.dev", at, at.AddHours(24), 1, Passphrase: true, "https://keypaste.com");
        var row = Row(Entry("share", "granted", "share-created") with { Entry = info.What, Field = "password", Reason = ShareAuditReason.Created(info) });

        Assert.Equal("maya@acme.dev", row.Where);
        Assert.Equal("Link · 24h", row.Result);
    }

    [Fact]
    public void A_listing_names_no_secret()
    {
        var row = Row(Entry("list_entry_names", "granted", "exposure"));

        Assert.Equal("Listed names", row.Action);
        Assert.Equal("names only", row.Secrets);
        Assert.Equal("Granted", row.Result);
    }

    /// <summary>The clock keeps its seconds on every day, so two records a minute apart still read in order.</summary>
    [Fact]
    public void The_time_is_a_clock_to_the_second_on_any_day()
    {
        var today = Row(Entry("request_credential", "granted", "prompt") with { At = _now.AddSeconds(-7) });
        var earlier = Row(Entry("request_credential", "granted", "prompt") with { At = _now.AddDays(-3) });

        Assert.Equal("14:39:53", today.Time);
        Assert.Equal("14:40:00", earlier.Time);
        Assert.Equal(new DateTime(2026, 7, 25), earlier.Date);
        Assert.Equal("2026-07-28 14:39:53", today.When);
    }

    [Fact]
    public void A_row_the_chain_does_not_vouch_for_is_marked_and_says_so()
    {
        var row = Row(Entry("request_credential", "granted", "prompt"), verified: false);

        Assert.True(row.Unverified);
        Assert.StartsWith(LogRow.UnverifiedWords, row.Detail, StringComparison.Ordinal);
    }

    private sealed class UtcClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
