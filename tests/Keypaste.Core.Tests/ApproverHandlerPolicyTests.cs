using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Policy;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// What a pre-authorization does, and — mostly — where in the order it does it.
/// </summary>
/// <remarks>
/// This is the first feature in keypaste that releases a credential with nobody watching, so the
/// claims worth asserting are all about what still has to happen first. <c>Channel.Asked == 0</c>
/// proves nobody was shown anything; <c>Source.Reads == 0</c> proves nothing was decrypted. Between
/// them they say what a policy grant is: a decision taken in advance, not a shortcut past the rest
/// of the procedure.
/// </remarks>
public sealed class ApproverHandlerPolicyTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CredentialRequest Request(
        string entry = "env/dev/STRIPE_KEY",
        string field = "password",
        int ttl = 900,
        string? label = "billing-bot",
        params string[] exposure) => new()
        {
            Entry = entry,
            Field = field,
            Reason = "deploy billing to staging",
            TtlSeconds = ttl,
            Exposure = exposure.Length == 0 ? ["env/**"] : exposure,
            ClientName = "claude-code",
            ClientLabel = label,
        };

    // ---------------------------------------------------------------- the four the spec asks for

    [Fact]
    public async Task AMatchingRule_ReleasesTheFieldWithoutAskingAnybody()
    {
        using var fixture = new ApproverFixture(Policy());

        var reply = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal(AuditDecision.Granted, reply.Decision);
        Assert.Equal(AuditMethod.Policy, reply.Method);
        Assert.Equal(ApproverFixture.Sentinel, reply.Value);
        Assert.Equal(0, fixture.Channel.Asked);
        Assert.Contains("allow#1", reply.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("env/dev/STRIPE_KEY", "username", "billing-bot", "a field outside the rule")]
    [InlineData("env/dev/STRIPE_KEY", "password", "other-bot", "a label outside the rule")]
    [InlineData("env/dev/STRIPE_KEY", "password", null, "no label at all")]
    public async Task ANonMatchingRequest_StillAsksAPerson(string entry, string field, string? label, string what)
    {
        using var fixture = new ApproverFixture(Policy());
        fixture.Channel.Answer = ApprovalAnswer.Approved;

        var reply = await fixture.Handler.RequestAsync(Request(entry, field, label: label), "conn-1", Token);

        Assert.True(reply.Method == AuditMethod.Prompt, what);
        Assert.Equal(1, fixture.Channel.Asked);
    }

    /// <summary>
    /// A policy that is partly wrong says nothing, so the request a valid file would have granted
    /// silently reaches a person instead. Asserted through the loader rather than by handing the
    /// handler an empty document, so the fallback that actually ships is the one under test.
    /// </summary>
    [Fact]
    public async Task AMalformedPolicy_AsksAboutEverything()
    {
        var home = Path.Combine(Path.GetTempPath(), "keypaste-policy-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(home);

        try
        {
            var path = Path.Combine(home, "policy.toml");
            File.WriteAllText(path, Rules + "\n\n[[allow]]\nclient = \"x\"\n");

            var load = PolicyLoader.Load(path);
            Assert.Equal(PolicyStatus.Rejected, load.Status);

            using var fixture = new ApproverFixture(load.Rules);
            fixture.Channel.Answer = ApprovalAnswer.Approved;

            var reply = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

            Assert.Equal(AuditMethod.Prompt, reply.Method);
            Assert.Equal(1, fixture.Channel.Asked);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    /// <summary>
    /// The claim the specification asks for in so many words, and the reason the policy check sits
    /// <em>after</em> the exposure re-check rather than beside it. A rule is a narrowing of what a
    /// person would be asked about; the bridge's own <c>--expose</c> is the ceiling, and a file in
    /// the user's home directory does not get to raise it.
    /// </summary>
    [Theory]
    [InlineData("personal/bank", "by path")]
    [InlineData("handle", "by handle")]
    public async Task APolicyRule_CannotReachOutsideTheExposure(string how, string what)
    {
        using var fixture = new ApproverFixture(Policy(entries: "[\"**\"]"));

        var entry = string.Equals(how, "handle", StringComparison.Ordinal)
            ? EntryHandle.For(new EntryName("personal", "bank"))
            : how;

        var reply = await fixture.Handler.RequestAsync(Request(entry), "conn-1", Token);

        Assert.True(reply.Decision == AuditDecision.Denied, what);
        Assert.Equal(AuditMethod.OutOfScope, reply.Method);
        Assert.Null(reply.Value);
        Assert.Equal(0, fixture.Channel.Asked);
        Assert.Equal(0, fixture.Source.Reads);
    }

    // ------------------------------------------------------------------ the ceilings and the log

    /// <summary>
    /// Both ceilings apply and the rule may only lower. A rule saying an hour, under an approver
    /// started with a one-minute maximum, grants a minute — otherwise the one control SECURITY.md
    /// tells users is real would be defeated by a file they wrote last month.
    /// </summary>
    [Fact]
    public async Task APolicyRule_CannotRaiseTheCeilingTheOperatorSetOnTheCommandLine()
    {
        using var fixture = new ApproverFixture(
            Policy(ttl: "3600"),
            ApprovalLimits.Default with { MaximumTtlSeconds = 60 });

        var reply = await fixture.Handler.RequestAsync(Request(ttl: 3600), "conn-1", Token);

        Assert.Equal(AuditMethod.Policy, reply.Method);
        Assert.Equal(60, reply.TtlSeconds);
    }

    /// <summary>
    /// A request over the rule's own ceiling is clamped, not bounced to a person. Bouncing it would
    /// hand an agent a one-integer lever for manufacturing prompts on demand.
    /// </summary>
    [Fact]
    public async Task ARequestOverTheRulesTtl_IsClampedNotPrompted()
    {
        using var fixture = new ApproverFixture(Policy(ttl: "120"));

        var reply = await fixture.Handler.RequestAsync(Request(ttl: 3600), "conn-1", Token);

        Assert.Equal(AuditMethod.Policy, reply.Method);
        Assert.Equal(120, reply.TtlSeconds);
        Assert.Equal(0, fixture.Channel.Asked);
    }

    [Fact]
    public async Task APolicyGrant_IsLoggedAsPolicyAndNeverAsPrompt()
    {
        using var fixture = new ApproverFixture(Policy());

        var reply = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal(AuditMethod.Policy, reply.Method);
        Assert.NotEqual(AuditMethod.Prompt, reply.Method);
        Assert.NotEqual(AuditMethod.GrantCache, reply.Method);

        // The rule is named on the line, because "which standing grant did this" is the first
        // question anyone reading a silent release asks.
        Assert.Contains("allow#1", reply.Reason, StringComparison.Ordinal);
        Assert.Contains("env/dev/**", reply.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Caching a policy grant would blow a hole in <c>max_per_hour</c> — every later request inside
    /// the TTL served free and off the count — and would hide those releases behind
    /// <c>grant-cache</c> lines that name no rule. So each one is evaluated, counted and logged
    /// afresh.
    /// </summary>
    [Fact]
    public async Task APolicyGrant_LeavesNoGrantInTheCache()
    {
        using var fixture = new ApproverFixture(Policy());

        await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal(0, fixture.Grants.Count);

        var second = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal(AuditMethod.Policy, second.Method);
        Assert.Equal(2, fixture.Source.Reads);
    }

    // ------------------------------------------------------------------------- who may satisfy it

    /// <summary>
    /// The name an agent asserts about itself is unauthenticated (THREATS.md T-3), so it may reach
    /// the audit line and never a rule. Here the client calls itself exactly what the rule names
    /// and is still shown to a person.
    /// </summary>
    [Fact]
    public async Task TheClientsAssertedName_CanNeverSatisfyARule()
    {
        using var fixture = new ApproverFixture(Policy(client: "\"claude-code\""));
        fixture.Channel.Answer = ApprovalAnswer.Approved;

        var reply = await fixture.Handler.RequestAsync(
            Request() with { ClientName = "claude-code", ClientLabel = null },
            "conn-1",
            Token);

        Assert.Equal(AuditMethod.Prompt, reply.Method);
        Assert.Equal(1, fixture.Channel.Asked);
    }

    // ---------------------------------------------------------------------- the rest of the order

    /// <summary>
    /// A refusal a person just gave outranks a rule that would otherwise have released the same
    /// thing silently.
    /// </summary>
    /// <remarks>
    /// <b>No shipped path can reach this state today, and the check is kept anyway.</b> A rule that
    /// matches never prompts, so it never arms a cooldown for its own request; a rule that has spent
    /// its allowance denies rather than escalating; and the rule set is read once and never widens
    /// mid-session. So the probe in front of the policy evaluation is defence in depth, for the
    /// paths Stage 2.4 and 4.3 add — a per-client pause, a revoke switch — each of which produces a
    /// human "no" about a request some rule also covers.
    /// <para>
    /// Which is why this test arms the state directly, through the gate, rather than pretending to
    /// reach it: a test that drove it through <c>RequestAsync</c> would be asserting something the
    /// handler cannot currently do, and would pass whether or not the probe existed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ARefusalAPersonJustGave_OutranksAMatchingRule()
    {
        using var fixture = new ApproverFixture(Policy());
        fixture.Channel.Answer = ApprovalAnswer.Denied;

        // The key the handler computes for this request: connection, entry handle, field.
        var handle = EntryHandle.For(new EntryName("env/dev", "STRIPE_KEY"));
        var key = $"conn-1|{handle}|password";
        var prompt = ApprovalPrompt.For("claude-code", new EntryName("env/dev", "STRIPE_KEY"), "password", "why", 300);

        Assert.Equal(ApprovalAnswer.Denied, await fixture.Gate.AskAsync(key, prompt, Token));
        Assert.True(fixture.Gate.IsInCooldown(key));

        // The rule covers this request exactly, and it must still be refused.
        var reply = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Equal(AuditMethod.Cooldown, reply.Method);
        Assert.Null(reply.Value);
        Assert.Equal(0, fixture.Source.Reads);
    }

    [Fact]
    public async Task AGrantAPersonGave_IsStillServedAsAGrantAndNotReattributedToARule()
    {
        using var fixture = new ApproverFixture(Policy(field: "\"username\""));
        fixture.Channel.Answer = ApprovalAnswer.Approved;

        Assert.Equal(AuditMethod.Prompt, (await fixture.Handler.RequestAsync(Request(), "conn-1", Token)).Method);

        var second = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal(AuditMethod.GrantCache, second.Method);
        Assert.Equal(1, fixture.Channel.Asked);
    }

    [Fact]
    public async Task WhenTheAllowanceIsSpent_TheRequestIsRefusedAndNothingIsRead()
    {
        using var fixture = new ApproverFixture(Policy(perHour: "1"));

        await fixture.Handler.RequestAsync(Request(), "conn-1", Token);
        var spent = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal(AuditDecision.Denied, spent.Decision);
        Assert.Equal(AuditMethod.PolicyLimit, spent.Method);
        Assert.Null(spent.Value);
        Assert.Equal(0, fixture.Channel.Asked);
        Assert.Equal(1, fixture.Source.Reads);
        Assert.Contains("allow#1", spent.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The allowance is a property of the rule and of the clock, so it comes back on its own and
    /// not because anything reconnected.
    /// </summary>
    [Fact]
    public async Task TheAllowanceComesBackWhenTheHourRolls_AndNotBecauseAClientReconnected()
    {
        using var fixture = new ApproverFixture(Policy(perHour: "1"));

        await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal(
            AuditMethod.PolicyLimit,
            (await fixture.Handler.RequestAsync(Request(), "conn-2", Token)).Method);

        fixture.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(
            AuditMethod.Policy,
            (await fixture.Handler.RequestAsync(Request(), "conn-3", Token)).Method);
    }

    /// <summary>
    /// A rule can neither make an entry listable that the exposure excludes nor hide one. This is
    /// structural rather than behavioural — the listing path is never handed the policy at all —
    /// and asserted here so that changing it would be caught.
    /// </summary>
    [Fact]
    public async Task TheListingPath_NeverConsultsThePolicy()
    {
        using var fixture = new ApproverFixture(Policy(entries: "[\"personal/**\"]"));

        var reply = await fixture.Handler.ListAsync(new NamesRequest(["env/**"]), "conn-1", Token);

        Assert.True(reply.VaultUnlocked);
        Assert.Equal("STRIPE_KEY", Assert.Single(reply.Names).Title);
    }

    [Fact]
    public async Task NothingIsReadFromTheVault_UntilARuleOrAPersonSaysYes()
    {
        using var fixture = new ApproverFixture(Policy(perHour: "1"));

        await fixture.Handler.RequestAsync(Request("personal/bank"), "conn-1", Token);
        await fixture.Handler.RequestAsync(Request(field: "username"), "conn-1", Token);
        await fixture.Handler.RequestAsync(Request(label: null), "conn-1", Token);

        Assert.Equal(0, fixture.Source.Reads);
    }

    [Fact]
    public async Task AReadThatFailsAfterARuleMatched_IsADenialAndSpendsTheAllowanceAnyway()
    {
        using var fixture = new ApproverFixture(Policy(perHour: "1"));
        fixture.Source.FailReads = true;

        var reply = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Equal(AuditMethod.Failed, reply.Method);
        Assert.Null(reply.Value);

        // Not refunded. Over-counting a cap is the direction that fails closed, and a refund path
        // is one more thing that can itself go wrong while holding an unlocked vault.
        fixture.Source.FailReads = false;
        Assert.Equal(
            AuditMethod.PolicyLimit,
            (await fixture.Handler.RequestAsync(Request(), "conn-1", Token)).Method);
    }

    [Fact]
    public async Task AVaultThatIsLocked_IsStillRefusedBeforeAnyRuleIsConsulted()
    {
        using var fixture = new ApproverFixture(Policy());
        fixture.Source.Locked = true;

        var reply = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal(AuditMethod.VaultLocked, reply.Method);
        Assert.Equal(0, fixture.Source.Reads);
    }

    internal const string Rules = """
        [[allow]]
        client          = "billing-bot"
        entries         = ["env/dev/**"]
        fields          = ["password"]
        max_ttl_seconds = 300
        """;

    private static PolicyDocument Policy(
        string client = "\"billing-bot\"",
        string entries = "[\"env/dev/**\"]",
        string field = "\"password\"",
        string ttl = "300",
        string? perHour = null)
    {
        var text = $"""
            [[allow]]
            client          = {client}
            entries         = {entries}
            fields          = [{field}]
            max_ttl_seconds = {ttl}
            """;

        if (perHour is not null)
        {
            text += $"\nmax_per_hour    = {perHour}";
        }

        Assert.True(Toml.TryParse(text, out var syntax, out var syntaxError), syntaxError);
        Assert.True(PolicyDocument.TryCreate(syntax, out var document, out var error), error);

        return document;
    }
}
