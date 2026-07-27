using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Policy;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The approver's decision procedure, with fakes for the vault and the human.
/// </summary>
/// <remarks>
/// The claims worth making here are about <em>order</em>, not about outcomes: that nothing decrypts
/// a field before a person has said yes, that an entry outside the exposure never reaches a person
/// at all, and that "no such entry" and "not yours" are indistinguishable to an agent.
/// </remarks>
public sealed class ApproverHandlerTests
{
    internal const string Sentinel = ApproverFixture.Sentinel;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CredentialRequest Request(
        string entry = "env/dev/STRIPE_KEY",
        string field = "password",
        int ttl = 900,
        params string[] exposure) => new()
        {
            Entry = entry,
            Field = field,
            Reason = "deploy billing to staging",
            TtlSeconds = ttl,
            Exposure = exposure.Length == 0 ? ["env/**"] : exposure,
            ClientName = "claude-code",
        };

    [Fact]
    public async Task AnApprovedRequest_ReleasesTheFieldAndRecordsWhy()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Approved;

        var reply = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal(AuditDecision.Granted, reply.Decision);
        Assert.Equal(AuditMethod.Prompt, reply.Method);
        Assert.Equal(Sentinel, reply.Value, StringComparer.Ordinal);
        Assert.Equal("env/dev/STRIPE_KEY", reply.Entry, StringComparer.Ordinal);
    }

    /// <summary>
    /// The single most important ordering claim in the approval flow. If the field were read before
    /// the prompt, every denied and every timed-out request would still have decrypted a credential
    /// into this process's memory.
    /// </summary>
    [Fact]
    public async Task NothingIsReadFromTheVault_UntilAPersonHasSaidYes()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Denied;

        var reply = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Equal(0, fixture.Source.Reads);
        Assert.Null(reply.Value);

        // ...and the same source does read when the answer changes, so this is not passing because
        // reading is broken.
        fixture.Channel.Answer = ApprovalAnswer.Approved;
        await fixture.Handler.RequestAsync(Request(), "conn-2", Token);

        Assert.Equal(1, fixture.Source.Reads);
    }

    /// <summary>
    /// An entry outside the exposure never reaches a human at all. Prompting for it and then
    /// refusing would still have let an agent put an arbitrary entry name in front of the user,
    /// which is most of what a phishing attempt through this channel would need.
    /// </summary>
    [Fact]
    public async Task AnEntryOutsideTheExposure_IsRefusedWithoutAskingAnybody()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Approved;

        var reply = await fixture.Handler.RequestAsync(Request("personal/bank"), "conn-1", Token);

        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Equal(AuditMethod.OutOfScope, reply.Method);
        Assert.Equal(0, fixture.Channel.Asked);
        Assert.Equal(0, fixture.Source.Reads);
    }

    /// <summary>
    /// The handle equivalent, and the reason the approver re-checks the exposure after resolving
    /// rather than trusting the bridge's check. The bridge cannot test a handle against its globs
    /// without the vault, so a handle would otherwise be the way around the exposure rule.
    /// </summary>
    [Fact]
    public async Task AHandleOutsideTheExposure_IsRefusedNotPrompted()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Approved;

        var handle = EntryHandle.For(new EntryName("personal", "bank"));

        var reply = await fixture.Handler.RequestAsync(Request(handle), "conn-1", Token);

        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Equal(AuditMethod.OutOfScope, reply.Method);
        Assert.Equal(0, fixture.Channel.Asked);

        // The handle does resolve — the refusal is the exposure rule, not a broken lookup.
        Assert.True(fixture.Source.TryResolve(handle, out var resolved, out _));
        Assert.Equal("personal", resolved.GroupPath, StringComparer.Ordinal);
    }

    /// <summary>
    /// "There is no such entry" and "that entry is not yours" have to be the same answer. A
    /// difference between them is an oracle: an agent could enumerate what exists in parts of the
    /// vault it was never allowed to see, which is the exposure rule undone by an error message.
    /// </summary>
    [Fact]
    public async Task AMissingEntryAndAForbiddenOne_AreIndistinguishableToTheAgent()
    {
        using var fixture = new ApproverFixture();

        var missing = await fixture.Handler.RequestAsync(Request("env/dev/NOT_THERE"), "conn-1", Token);
        var forbidden = await fixture.Handler.RequestAsync(Request("personal/bank"), "conn-1", Token);

        Assert.Equal(missing.Method, forbidden.Method);
        Assert.Equal(AuditMethod.OutOfScope, missing.Method);
        Assert.Equal(missing.Decision, forbidden.Decision);
        Assert.Equal(missing.Entry, forbidden.Entry);
        Assert.Equal(missing.TtlSeconds, forbidden.TtlSeconds);
    }

    [Theory]
    [InlineData(ApprovalAnswer.Denied, AuditMethod.Prompt)]
    [InlineData(ApprovalAnswer.TimedOut, AuditMethod.TimedOut)]
    [InlineData(ApprovalAnswer.Busy, AuditMethod.Busy)]
    [InlineData(ApprovalAnswer.Cooldown, AuditMethod.Cooldown)]
    [InlineData(ApprovalAnswer.NoChannel, AuditMethod.NoApprover)]
    [InlineData(ApprovalAnswer.Failed, AuditMethod.Failed)]
    public async Task EveryAnswerThatIsNotYes_IsADenialThatSaysWhy(ApprovalAnswer answer, AuditMethod expected)
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = answer;

        var reply = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Equal(expected, reply.Method);
        Assert.Null(reply.Value);
        Assert.NotEmpty(reply.Reason);
    }

    /// <summary>The point of the grant cache: a person is not asked the same question twice.</summary>
    [Fact]
    public async Task ARepeatRequestInsideTheTtl_IsServedWithoutAskingAgain()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Approved;

        var first = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);
        var second = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal(AuditMethod.Prompt, first.Method);
        Assert.Equal(AuditMethod.GrantCache, second.Method);
        Assert.Equal(Sentinel, second.Value, StringComparer.Ordinal);

        // One prompt, and one read of the vault. A cache that re-read would be keeping a
        // capability alive rather than the datum a person actually approved.
        Assert.Equal(1, fixture.Channel.Asked);
        Assert.Equal(1, fixture.Source.Reads);
    }

    [Fact]
    public async Task ARepeatRequestForADifferentField_AsksAgain()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Approved;

        await fixture.Handler.RequestAsync(Request(field: "password"), "conn-1", Token);
        var other = await fixture.Handler.RequestAsync(Request(field: "username"), "conn-1", Token);

        Assert.Equal(AuditMethod.Prompt, other.Method);
        Assert.Equal(2, fixture.Channel.Asked);
    }

    [Fact]
    public async Task AnotherConnection_GetsNothingFromSomebodyElsesGrant()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Approved;

        await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        fixture.Channel.Answer = ApprovalAnswer.Denied;
        var other = await fixture.Handler.RequestAsync(Request(), "conn-2", Token);

        Assert.Equal(AuditDecision.Denied, other.Decision);
    }

    [Fact]
    public async Task WhenAConnectionGoesAway_ItsGrantsGoWithIt()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Approved;

        await fixture.Handler.RequestAsync(Request(), "conn-1", Token);
        fixture.Handler.Disconnected("conn-1");

        fixture.Channel.Answer = ApprovalAnswer.Denied;

        Assert.Equal(AuditDecision.Denied, (await fixture.Handler.RequestAsync(Request(), "conn-1", Token)).Decision);
    }

    /// <summary>
    /// A path and the handle for the same entry share one grant, so an agent cannot force a second
    /// prompt for something it has already been given by spelling the entry differently.
    /// </summary>
    [Fact]
    public async Task AHandleAndItsPath_ShareOneGrant()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Approved;

        await fixture.Handler.RequestAsync(Request("env/dev/STRIPE_KEY"), "conn-1", Token);

        var handle = EntryHandle.For(new EntryName("env/dev", "STRIPE_KEY"));
        var again = await fixture.Handler.RequestAsync(Request(handle), "conn-1", Token);

        Assert.Equal(AuditMethod.GrantCache, again.Method);
        Assert.Equal(1, fixture.Channel.Asked);
    }

    /// <summary>
    /// The human is shown, and the grant lives for, the TTL that will actually apply — not the hour
    /// the agent asked for.
    /// </summary>
    [Fact]
    public async Task TheGrantedTtl_IsTheCappedOneNotTheRequestedOne()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Approved;

        var reply = await fixture.Handler.RequestAsync(Request(ttl: 3600), "conn-1", Token);

        Assert.Equal(ApprovalLimits.DefaultMaximumTtlSeconds, reply.TtlSeconds);
        Assert.Equal(ApprovalLimits.DefaultMaximumTtlSeconds, fixture.Channel.LastPrompt!.TtlSeconds);
    }

    [Fact]
    public async Task AFieldKeypasteDoesNotRelease_NeverReachesAPerson()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Approved;

        var reply = await fixture.Handler.RequestAsync(Request(field: "totp"), "conn-1", Token);

        Assert.Equal(AuditMethod.InvalidRequest, reply.Method);
        Assert.Equal(0, fixture.Channel.Asked);
    }

    [Fact]
    public async Task ALockedVault_IsSaidToBeLockedRatherThanOutOfScope()
    {
        using var fixture = new ApproverFixture();
        fixture.Source.Locked = true;

        var reply = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal(AuditMethod.VaultLocked, reply.Method);
        Assert.Equal(0, fixture.Channel.Asked);
    }

    /// <summary>
    /// The gap between saying yes and reading: the entry can be deleted, or the vault can be
    /// locked, in between. That is an error path, and law 3.7 says an error path denies.
    /// </summary>
    [Fact]
    public async Task AVaultThatFailsAfterTheApproval_StillDenies()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Approved;
        fixture.Source.FailReads = true;

        var reply = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Equal(AuditMethod.Failed, reply.Method);
        Assert.Null(reply.Value);
    }

    [Fact]
    public async Task ListingYieldsOnlyWhatTheExposureAllows()
    {
        using var fixture = new ApproverFixture();

        var reply = await fixture.Handler.ListAsync(new NamesRequest(["env/**"]), "conn-1", Token);

        Assert.True(reply.VaultUnlocked);
        Assert.Equal([new EntryName("env/dev", "STRIPE_KEY")], reply.Names);
    }

    [Fact]
    public async Task ListingALockedVaultSaysSoRatherThanReturningNothingQuietly()
    {
        using var fixture = new ApproverFixture();
        fixture.Source.Locked = true;

        var reply = await fixture.Handler.ListAsync(new NamesRequest(["env/**"]), "conn-1", Token);

        Assert.False(reply.VaultUnlocked);
        Assert.Empty(reply.Names);
        Assert.NotEmpty(reply.Reason);
    }

    [Fact]
    public void TheHandlerRejectsNulls()
    {
        using var fixture = new ApproverFixture();

        var s = fixture.Source;
        var g = fixture.Gate;
        var c = fixture.Grants;
        var p = PolicyGate.None;

        Assert.Throws<ArgumentNullException>(() => new ApproverHandler(null!, s, g, c, p));
        Assert.Throws<ArgumentNullException>(() => new ApproverHandler(s, null!, g, c, p));
        Assert.Throws<ArgumentNullException>(() => new ApproverHandler(s, s, null!, c, p));
        Assert.Throws<ArgumentNullException>(() => new ApproverHandler(s, s, g, null!, p));
        Assert.Throws<ArgumentNullException>(() => new ApproverHandler(s, s, g, c, null!));
    }
}
