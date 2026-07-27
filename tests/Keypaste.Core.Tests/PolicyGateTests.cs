using Keypaste.Core.Policy;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The gate and its hourly allowance, on a clock the test moves by hand.
/// </summary>
public sealed class PolicyGateTests
{
    private static readonly EntryName _covered = new("env/dev", "STRIPE_KEY");
    private static readonly EntryName _uncovered = new("personal", "bank");

    [Fact]
    public void ARuleThatCoversTheRequest_Grants()
    {
        var outcome = Gate(cap: "20").Evaluate("claude-code", _covered, "password");

        Assert.Equal(PolicyOutcomeKind.Granted, outcome.Kind);
        Assert.Equal("allow#1", outcome.Rule!.Id);
    }

    [Theory]
    [InlineData("other-client", "the wrong label")]
    [InlineData(null, "no label at all")]
    public void ARequestFromAnotherClient_MatchesNoRule(string? label, string what)
    {
        var outcome = Gate(cap: "20").Evaluate(label, _covered, "password");

        Assert.True(outcome.Kind == PolicyOutcomeKind.NoRule, what);
    }

    [Fact]
    public void ARequestOutsideEveryPattern_MatchesNoRule()
    {
        Assert.Equal(PolicyOutcomeKind.NoRule, Gate(cap: "20").Evaluate("claude-code", _uncovered, "password").Kind);
        Assert.Equal(PolicyOutcomeKind.NoRule, Gate(cap: "20").Evaluate("claude-code", _covered, "username").Kind);
    }

    [Fact]
    public void AGateOverNoRules_PreAuthorizesNothing()
    {
        Assert.True(PolicyGate.None.IsEmpty);
        Assert.Equal(PolicyOutcomeKind.NoRule, PolicyGate.None.Evaluate("claude-code", _covered, "password").Kind);
    }

    [Fact]
    public void ARuleWithNoAllowance_NeverRunsOut()
    {
        var gate = Gate(cap: null);

        for (var i = 0; i < 200; i++)
        {
            Assert.Equal(PolicyOutcomeKind.Granted, gate.Evaluate("claude-code", _covered, "password").Kind);
        }
    }

    [Fact]
    public void WhenTheAllowanceIsSpent_TheRequestIsRefusedRatherThanEscalated()
    {
        var gate = Gate(cap: "3");

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(PolicyOutcomeKind.Granted, gate.Evaluate("claude-code", _covered, "password").Kind);
        }

        var spent = gate.Evaluate("claude-code", _covered, "password");

        Assert.Equal(PolicyOutcomeKind.RateLimited, spent.Kind);
        Assert.Equal("allow#1", spent.Rule!.Id);
    }

    /// <summary>
    /// A true sliding window, not a bucket that resets on the hour: the allowance comes back one
    /// release at a time, exactly an hour after each was spent.
    /// </summary>
    [Fact]
    public void TheAllowanceComesBackOneReleaseAtATime_AnHourAfterEachWasSpent()
    {
        var clock = new ManualClock();
        var gate = Gate(cap: "2", clock);

        Assert.Equal(PolicyOutcomeKind.Granted, gate.Evaluate("claude-code", _covered, "password").Kind);
        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(PolicyOutcomeKind.Granted, gate.Evaluate("claude-code", _covered, "password").Kind);
        Assert.Equal(PolicyOutcomeKind.RateLimited, gate.Evaluate("claude-code", _covered, "password").Kind);

        // One second short of an hour after the first release, and it is still spent.
        clock.Advance(TimeSpan.FromMinutes(30) - TimeSpan.FromSeconds(1));
        Assert.Equal(PolicyOutcomeKind.RateLimited, gate.Evaluate("claude-code", _covered, "password").Kind);

        // An hour exactly, and the first one comes back — but only the first.
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(PolicyOutcomeKind.Granted, gate.Evaluate("claude-code", _covered, "password").Kind);
        Assert.Equal(PolicyOutcomeKind.RateLimited, gate.Evaluate("claude-code", _covered, "password").Kind);
    }

    /// <summary>
    /// The allowance belongs to the rule, not to whoever is asking. Counting per connection would
    /// mean a client could reset its own quota by spawning a fresh bridge, and a quota the
    /// constrained party can reset is not a quota (THREATS.md T-14).
    /// </summary>
    [Fact]
    public void TheAllowanceBelongsToTheRule_NotToTheCaller()
    {
        var gate = Gate(cap: "1", entries: "[\"env/**\"]");

        Assert.Equal(PolicyOutcomeKind.Granted, gate.Evaluate("claude-code", _covered, "password").Kind);
        Assert.Equal(
            PolicyOutcomeKind.RateLimited,
            gate.Evaluate("claude-code", new EntryName("env/test", "OTHER_KEY"), "password").Kind);
    }

    /// <summary>
    /// A spent rule does not fall through to a later one, or the cap is defeated by writing the
    /// same rule twice.
    /// </summary>
    [Fact]
    public void ASpentRule_DoesNotFallThroughToTheNextOne()
    {
        const string Text = """
            [[allow]]
            client          = "claude-code"
            entries         = ["env/dev/**"]
            fields          = ["password"]
            max_ttl_seconds = 300
            max_per_hour    = 1

            [[allow]]
            client          = "claude-code"
            entries         = ["env/dev/**"]
            fields          = ["password"]
            max_ttl_seconds = 300
            """;

        var gate = new PolicyGate(Document(Text), new ManualClock());

        Assert.Equal(PolicyOutcomeKind.Granted, gate.Evaluate("claude-code", _covered, "password").Kind);

        var second = gate.Evaluate("claude-code", _covered, "password");

        Assert.Equal(PolicyOutcomeKind.RateLimited, second.Kind);
        Assert.Equal(1, second.Rule!.Ordinal);
    }

    [Fact]
    public void OnlyAGrantSpendsTheAllowance()
    {
        var gate = Gate(cap: "5");
        var rule = Assert.Single(gate.Document.Rules);

        Assert.Equal(0, gate.Spent(rule));

        gate.Evaluate("claude-code", _uncovered, "password");
        gate.Evaluate("other-client", _covered, "password");
        gate.Evaluate("claude-code", _covered, "username");

        Assert.Equal(0, gate.Spent(rule));

        gate.Evaluate("claude-code", _covered, "password");

        Assert.Equal(1, gate.Spent(rule));
    }

    [Fact]
    public void ItRejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new PolicyGate(null!, new ManualClock()));
        Assert.Throws<ArgumentNullException>(() => new PolicyGate(PolicyDocument.None, null!));
        Assert.Throws<ArgumentNullException>(() => new PolicyRateLimiter(null!));
        Assert.Throws<ArgumentNullException>(() => PolicyGate.None.Evaluate("c", null!, "password"));
        Assert.Throws<ArgumentNullException>(() => PolicyGate.None.Evaluate("c", _covered, null!));
        Assert.Throws<ArgumentNullException>(() => PolicyGate.None.Spent(null!));
    }

    private static PolicyGate Gate(
        string? cap,
        ManualClock? clock = null,
        string entries = "[\"env/dev/**\"]")
    {
        var text = $"""
            [[allow]]
            client          = "claude-code"
            entries         = {entries}
            fields          = ["password"]
            max_ttl_seconds = 300
            """;

        if (cap is not null)
        {
            text += $"\nmax_per_hour    = {cap}";
        }

        return new PolicyGate(Document(text), clock ?? new ManualClock());
    }

    private static PolicyDocument Document(string text)
    {
        Assert.True(Toml.TryParse(text, out var syntax, out var syntaxError), syntaxError);
        Assert.True(PolicyDocument.TryCreate(syntax, out var document, out var error), error);
        return document;
    }
}
