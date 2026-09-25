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

    /// <summary>
    /// The number an agent and the audit log are told. Remaining lifetime is asked of the clock
    /// rather than counted down, so a wall clock moved backwards used to report more time left
    /// than the person had approved — a lifetime longer than the ceiling SECURITY.md names.
    /// </summary>
    [Fact]
    public async Task AReusedGrant_NeverReportsMoreTimeThanWasApproved()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Approved;

        var approved = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        fixture.Clock.RewindWallOnly(TimeSpan.FromHours(1));

        var reused = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal(AuditMethod.GrantCache, reused.Method);
        Assert.InRange(reused.TtlSeconds, 0, approved.TtlSeconds);
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
    /// The person chooses the duration on screen, so the prompt offers the approver's ceiling however
    /// short or long a lifetime the agent asked for.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    [InlineData(3600)]
    public async Task ThePromptOffersTheCeiling_WhateverTheAgentAsked(int requested)
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Approved;

        var reply = await fixture.Handler.RequestAsync(Request(ttl: requested), "conn-1", Token);

        Assert.Equal(ApprovalLimits.DefaultMaximumTtlSeconds, fixture.Channel.LastPrompt!.TtlSeconds);
        Assert.Equal(ApprovalLimits.DefaultMaximumTtlSeconds, reply.TtlSeconds);
    }

    /// <summary>The person is shown which configured connection is asking, as the bridge was labelled.</summary>
    [Fact]
    public async Task AnApprovedGrant_IsListedWithWhatThePersonApproved_AndNoValue()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Approved;

        await fixture.Handler.RequestAsync(Request() with { ClientLabel = "deploy-bot" }, "conn-1", Token);

        var activity = fixture.Handler.Activity();
        var grant = Assert.Single(activity.Grants);

        Assert.Empty(activity.Waiting);
        Assert.Equal(new GrantKey("conn-1", EntryHandle.For(new EntryName("env/dev", "STRIPE_KEY")), "password"), grant.Key);
        Assert.Equal(fixture.Channel.LastPrompt, grant.Approved);
        Assert.Equal("deploy-bot", grant.Approved.Label, StringComparer.Ordinal);
        Assert.Equal(TimeSpan.FromSeconds(ApprovalLimits.Default.MaximumTtlSeconds), grant.Remaining);
        Assert.DoesNotContain(Sentinel, grant.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRequestInFrontOfAPerson_IsListedAsWaiting()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Hold = true;

        var asking = fixture.Handler.RequestAsync(Request(), "conn-1", Token).AsTask();
        await fixture.Channel.Waiting.WaitAsync(Token);

        var waiting = Assert.Single(fixture.Handler.Activity().Waiting);
        Assert.Equal("env/dev/STRIPE_KEY", waiting.Prompt.Entry, StringComparer.Ordinal);

        fixture.Clock.Advance(TimeSpan.FromSeconds(ApprovalLimits.DefaultWindowSeconds));
        await asking;

        Assert.Empty(fixture.Handler.Activity().Waiting);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AfterARevoke_TheSameRequestIsAskedAgain_RatherThanRefused(bool all)
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Approved;

        await fixture.Handler.RequestAsync(Request(), "conn-1", Token);
        var grant = Assert.Single(fixture.Handler.Activity().Grants);

        if (all)
        {
            fixture.Handler.RevokeAll();
        }
        else
        {
            fixture.Handler.Revoke(grant.Key);
        }

        Assert.Empty(fixture.Handler.Activity().Grants);

        var again = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal(AuditMethod.Prompt, again.Method);
        Assert.Equal(AuditDecision.Granted, again.Decision);
        Assert.Equal(2, fixture.Channel.Asked);
    }

    [Fact]
    public async Task ThePromptCarriesTheBridgesLabel()
    {
        using var fixture = new ApproverFixture();

        await fixture.Handler.RequestAsync(Request() with { ClientLabel = "deploy-bot" }, "conn-1", Token);

        Assert.Equal("deploy-bot", fixture.Channel.LastPrompt!.Label, StringComparer.Ordinal);
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

    // ------------------------------------------------------- a release nobody could send (F.3d)

    /// <summary>
    /// A field too large for one frame is refused, and the refusal says the release was approved.
    /// </summary>
    /// <remarks>
    /// <c>notes</c> is releasable and a KDBX note has no length limit, so this is an ordinary entry
    /// somebody pasted a certificate into. Before F.3d the reply was built and handed to the
    /// encoder anyway; the write threw, the connection went down and took its grants with it, and
    /// the agent was told the approver could not be asked — about a request a person had answered.
    /// </remarks>
    [Fact]
    public async Task AReleaseTooBigToDeliver_IsRefusedAndSaysAPersonApprovedIt()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Approved;
        fixture.Source.Value = new string('n', 100_000);

        var reply = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Equal(AuditMethod.Undeliverable, reply.Method);
        Assert.Equal(ApproverProtocol.UndeliverableReason(AuditMethod.Prompt), reply.Reason, StringComparer.Ordinal);
        Assert.Null(reply.Value);
        Assert.Equal(0, reply.TtlSeconds);
        Assert.Equal("env/dev/STRIPE_KEY", reply.Entry, StringComparer.Ordinal);

        // A person did answer, and the field was read. The record says denied because nothing
        // reached the agent, not because the procedure stopped early.
        Assert.Equal(1, fixture.Channel.Asked);
        Assert.Equal(1, fixture.Source.Reads);
    }

    /// <summary>
    /// The same request twice costs one prompt, even though neither answer can be delivered.
    /// </summary>
    /// <remarks>
    /// The size is a property of the entry, not of the request, so every retry would produce the
    /// same refusal — and re-prompting for each one is THREATS.md T-11 with a lever attached. The
    /// grant is stored exactly as a deliverable release stores one, so the second request is
    /// answered from the cache without troubling anybody, and the reason says so.
    /// </remarks>
    [Fact]
    public async Task AReleaseTooBigToDeliver_AsksAPersonOnce()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Approved;
        fixture.Source.Value = new string('n', 100_000);

        await fixture.Handler.RequestAsync(Request(), "conn-1", Token);
        var again = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal(1, fixture.Channel.Asked);
        Assert.Equal(AuditMethod.Undeliverable, again.Method);
        Assert.Equal(
            ApproverProtocol.UndeliverableReason(AuditMethod.GrantCache), again.Reason, StringComparer.Ordinal);
        Assert.Null(again.Value);
    }

    /// <summary>The operator's terminal never says a credential was released when none was.</summary>
    /// <remarks>
    /// The approver writes no audit line, so its narration is the only record it produces itself
    /// (THREATS.md T-14). It printed <c>released …</c> before the reply had left the process, which
    /// on this path is a claim nothing else would ever contradict.
    /// </remarks>
    [Fact]
    public async Task AnUndeliverableRelease_NeverTellsTheOperatorItWasReleased()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Approved;
        fixture.Source.Value = new string('n', 100_000);

        await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.DoesNotContain(fixture.Narration, line => line.StartsWith("released ", StringComparison.Ordinal));
        Assert.Contains(fixture.Narration, line => line.Contains("could not deliver", StringComparison.Ordinal));

        // ...and it does say "released" when the reply actually leaves, so this is not passing
        // because the narration stopped working.
        fixture.Source.Value = ApproverFixture.Sentinel;
        await fixture.Handler.RequestAsync(Request("env/dev/STRIPE_KEY"), "conn-2", Token);

        Assert.Contains(fixture.Narration, line => line.StartsWith("released ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AllowOnce_ReleasesTheField_AndStoresNoGrant()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;

        var first = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);
        var second = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal(AuditDecision.Granted, first.Decision);
        Assert.Equal(AuditMethod.Prompt, first.Method);
        Assert.Equal(Sentinel, first.Value, StringComparer.Ordinal);
        Assert.Equal(0, first.TtlSeconds);
        Assert.Equal("a person approved this one request", first.Reason);

        // Nothing was kept, so the identical request is a fresh question rather than a cache hit.
        Assert.Equal(AuditMethod.Prompt, second.Method);
        Assert.Equal(2, fixture.Channel.Asked);
        Assert.Empty(fixture.Handler.Activity().Grants);
        Assert.Contains(fixture.Narration, line => line.EndsWith(" once", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AllowForTheHour_StoresAGrantForTheCeiling_NotTheAgentsTtl()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Approved;

        var first = await fixture.Handler.RequestAsync(Request(ttl: 60), "conn-1", Token);

        fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        var second = await fixture.Handler.RequestAsync(Request(ttl: 60), "conn-1", Token);

        Assert.Equal(3600, first.TtlSeconds);
        Assert.Equal("a person approved this request for 1 hour", first.Reason);
        Assert.Contains(fixture.Narration, line => line.EndsWith(" for 3600s", StringComparison.Ordinal));
        Assert.Equal(AuditMethod.GrantCache, second.Method);
        Assert.Equal(3600 - 61, second.TtlSeconds);
        Assert.Equal(1, fixture.Channel.Asked);
    }

    [Fact]
    public async Task TheTimedGrant_CoversOnlyThatConnectionEntryAndField()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Approved;

        await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        fixture.Channel.Answer = ApprovalAnswer.Denied;
        var otherConnection = await fixture.Handler.RequestAsync(Request(), "conn-2", Token);
        var otherField = await fixture.Handler.RequestAsync(Request(field: "username"), "conn-1", Token);
        var same = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal(AuditDecision.Denied, otherConnection.Decision);
        Assert.Equal(AuditDecision.Denied, otherField.Decision);
        Assert.Equal(AuditMethod.GrantCache, same.Method);
        Assert.Equal(3, fixture.Channel.Asked);
    }

    [Fact]
    public async Task MaxTtl_BoundsTheTimedGrant()
    {
        using var fixture = new ApproverFixture(limits: ApprovalLimits.Default with { MaximumTtlSeconds = 300 });
        fixture.Channel.Answer = ApprovalAnswer.Approved;

        var reply = await fixture.Handler.RequestAsync(Request(ttl: 3600), "conn-1", Token);

        Assert.Equal(300, fixture.Channel.LastPrompt!.TtlSeconds);
        Assert.Equal(300, reply.TtlSeconds);
        Assert.Equal("a person approved this request for 5 minutes", reply.Reason);
    }

    /// <summary>A channel that says "timed" to a prompt that offered none releases once and keeps nothing.</summary>
    [Fact]
    public async Task AnHourAnswer_ToAPromptOfferingNone_ReleasesOnceOnly()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Approved;
        var handler = LiveOnly(fixture);

        var reply = await handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal(0, fixture.Channel.LastPrompt!.TtlSeconds);
        Assert.Equal(AuditDecision.Granted, reply.Decision);
        Assert.Equal(0, reply.TtlSeconds);
        Assert.Equal(0, fixture.Grants.Count);
    }

    [Fact]
    public async Task ALiveOnlyEntry_IsNeverServedFromAGrant()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Approved;

        // A grant under exactly the same connection, entry and field, given while it was not live-only.
        await fixture.Handler.RequestAsync(Request(), "conn-1", Token);
        Assert.Equal(1, fixture.Grants.Count);

        fixture.Channel.Answer = ApprovalAnswer.Denied;
        var reply = await LiveOnly(fixture).RequestAsync(Request(), "conn-1", Token);

        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Equal(AuditMethod.Prompt, reply.Method);
        Assert.Equal(2, fixture.Channel.Asked);
    }

    [Fact]
    public async Task ALiveOnlyEntry_IsNeverReleasedByPolicy()
    {
        using var fixture = new ApproverFixture(ApproverHandlerPolicyTests.Policy());
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;

        var reply = await LiveOnly(fixture).RequestAsync(Request() with { ClientLabel = "billing-bot" }, "conn-1", Token);

        Assert.Equal(1, fixture.Channel.Asked);
        Assert.Equal(0, fixture.Channel.LastPrompt!.TtlSeconds);
        Assert.Equal(AuditMethod.Prompt, reply.Method);

        // The same rule does release without asking once the entry is not live-only.
        var ruled = await fixture.Handler.RequestAsync(Request() with { ClientLabel = "billing-bot" }, "conn-1", Token);
        Assert.Equal(AuditMethod.Policy, ruled.Method);
    }

    [Fact]
    public async Task WithoutAPredicate_AProdEntryIsStillLiveOnly()
    {
        using var fixture = new ApproverFixture(ApproverHandlerPolicyTests.Policy(entries: "[\"env/**\"]"));
        fixture.Channel.Answer = ApprovalAnswer.Approved;
        var prod = new ProdSource();
        var handler = new ApproverHandler(prod, prod, fixture.Gate, fixture.Grants, fixture.Policy);
        var request = Request(ProdSource.Address) with { ClientLabel = "billing-bot" };

        var first = await handler.RequestAsync(request, "conn-1", Token);
        var second = await handler.RequestAsync(request, "conn-1", Token);

        Assert.Equal(AuditMethod.Prompt, first.Method);
        Assert.Equal(AuditMethod.Prompt, second.Method);
        Assert.Equal(0, first.TtlSeconds);
        Assert.Equal(0, fixture.Channel.LastPrompt!.TtlSeconds);
        Assert.Equal(2, fixture.Channel.Asked);
        Assert.Equal(0, fixture.Grants.Count);
    }

    [Fact]
    public async Task ADenial_StillStartsTheCooldown()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Denied;

        await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;
        var again = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal(AuditMethod.Cooldown, again.Method);
        Assert.Equal(1, fixture.Channel.Asked);
    }

    private static ApproverHandler LiveOnly(ApproverFixture fixture) =>
        new(fixture.Source, fixture.Source, fixture.Gate, fixture.Grants, fixture.Policy, fixture.Narration.Add, _ => true);

    /// <summary>A vault holding one entry in a protected profile.</summary>
    private sealed class ProdSource : ICredentialSource, IEntryNameLister
    {
        internal const string Address = "env/acme/prod/API_KEY";

        private static readonly EntryName _entry = new("env/acme/prod", "API_KEY");

        public bool TryResolve(string entryArgument, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out EntryName? name, out CredentialFailure failure)
        {
            var found = entryArgument == Address || entryArgument == EntryHandle.For(_entry);
            name = found ? _entry : null;
            failure = found ? CredentialFailure.None : CredentialFailure.NotFound;
            return found;
        }

        public bool TryRead(EntryName name, string field, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ReleasedField? value, out CredentialFailure failure)
        {
            value = new ReleasedField(field, Sentinel);
            failure = CredentialFailure.None;
            return true;
        }

        public bool TryList(EntryExposure exposure, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IReadOnlyList<EntryName>? names, out CredentialFailure failure)
        {
            names = [_entry];
            failure = CredentialFailure.None;
            return true;
        }
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
