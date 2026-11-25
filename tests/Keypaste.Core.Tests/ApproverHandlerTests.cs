using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
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

    private const string _unfilled = "holds the KeePass placeholder {TOTP}, which keypaste does not fill in";

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
            Exposure = exposure.Length == 0 ? ["env/**"] : exposure,
            ClientName = "claude-code",
            ClientLabel = ApproverFixture.Agent,
        };

    private static NamesRequest Listing(params string[] exposure) => new(exposure) { ClientLabel = ApproverFixture.Agent };

    [Fact]
    public async Task AnApprovedRequest_ReleasesTheFieldAndRecordsWhy()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;

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

        // ...and the same source does read when the answer changes once the refusal's cooldown is
        // over, so this is not passing because reading is broken.
        fixture.Clock.Advance(TimeSpan.FromSeconds(ApprovalLimits.DefaultCooldownSeconds + 1));
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;
        await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

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
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;

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
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;

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

    [Fact]
    public async Task ARepeatRequestForADifferentField_AsksAgain()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;

        await fixture.Handler.RequestAsync(Request(field: "password"), "conn-1", Token);
        var other = await fixture.Handler.RequestAsync(Request(field: "notes"), "conn-1", Token);

        Assert.Equal(AuditMethod.Prompt, other.Method);
        Assert.Equal(2, fixture.Channel.Asked);
    }

    [Fact]
    public async Task AnotherConnection_GetsNothingFromSomebodyElsesGrant()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;

        await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        fixture.Channel.Answer = ApprovalAnswer.Denied;
        var other = await fixture.Handler.RequestAsync(Request(), "conn-2", Token);

        Assert.Equal(AuditDecision.Denied, other.Decision);
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

    [Fact]
    public async Task ThePromptCarriesTheBridgesLabel()
    {
        using var fixture = new ApproverFixture();

        await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal(ApproverFixture.Agent, fixture.Channel.LastPrompt!.Label, StringComparer.Ordinal);
    }

    [Fact]
    public async Task AFieldKeypasteDoesNotRelease_NeverReachesAPerson()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;

        var reply = await fixture.Handler.RequestAsync(Request(field: "otp"), "conn-1", Token);

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
    /// locked, in between. That is an error path, and law 7 says an error path denies.
    /// </summary>
    [Fact]
    public async Task AVaultThatFailsAfterTheApproval_StillDenies()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;
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

        var reply = await fixture.Handler.ListAsync(Listing("env/**"), "conn-1", Token);

        Assert.True(reply.VaultUnlocked);
        Assert.Equal([new ListedEntry(new EntryName("env/dev", "STRIPE_KEY"), ["password", "STRIPE_SECRET_KEY"], [])], reply.Names);
    }

    /// <summary>a tag lists its entry with only the variables it reaches, and an untagged entry not at all.</summary>
    [Fact]
    public async Task ListingThroughATag_YieldsTheTaggedEntryWithItsVariablesOnly()
    {
        using var fixture = new ApproverFixture();
        fixture.Source.Tagged[new EntryName("personal", "bank")] = ["env:billing:prod"];

        var reply = await fixture.Handler.ListAsync(Listing(EntryExposure.DefaultGlob), "conn-1", Token);

        Assert.Equal([new ListedEntry(new EntryName("personal", "bank"), ["STRIPE_SECRET_KEY"], ["env:billing:prod"])], reply.Names);
    }

    /// <summary>
    /// an entry a tag reaches releases a project variable after a person says yes, and its
    /// password is refused before anybody is asked.
    /// </summary>
    [Fact]
    public async Task ATaggedEntry_ReleasesAVariable_AndNeverItsPassword()
    {
        using var fixture = new ApproverFixture();
        fixture.Source.Tagged[new EntryName("personal", "bank")] = ["env:billing"];
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;

        var password = await fixture.Handler.RequestAsync(Request("personal/bank", "password", 900, EntryExposure.DefaultGlob), "conn-1", Token);

        Assert.Equal(AuditMethod.OutOfScope, password.Method);
        Assert.Null(password.Value);
        Assert.Equal(0, fixture.Channel.Asked);

        var variable = await fixture.Handler.RequestAsync(Request("personal/bank", "STRIPE_SECRET_KEY", 900, EntryExposure.DefaultGlob), "conn-1", Token);

        Assert.Equal(AuditDecision.Granted, variable.Decision);
        Assert.Equal(Sentinel, variable.Value, StringComparer.Ordinal);
        Assert.Equal(1, fixture.Channel.Asked);
    }

    /// <summary>An untagged entry under <c>env/</c> is an ordinary entry, so the default reaches none of it.</summary>
    [Fact]
    public async Task AnUntaggedEntryUnderEnv_IsOutsideTheDefault()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;

        var reply = await fixture.Handler.RequestAsync(Request("env/dev/STRIPE_KEY", "STRIPE_SECRET_KEY", 900, EntryExposure.DefaultGlob), "conn-1", Token);

        Assert.Equal(AuditMethod.OutOfScope, reply.Method);
        Assert.Equal(0, fixture.Channel.Asked);
    }

    [Fact]
    public async Task ListingALockedVaultSaysSoRatherThanReturningNothingQuietly()
    {
        using var fixture = new ApproverFixture();
        fixture.Source.Locked = true;

        var reply = await fixture.Handler.ListAsync(Listing("env/**"), "conn-1", Token);

        Assert.False(reply.VaultUnlocked);
        Assert.Empty(reply.Names);
        Assert.NotEmpty(reply.Reason);
    }

    // ------------------------------------------------------------------------ a search (find)

    private static FindRequest Search(string? text = null, ItemKind? kind = null, string? relatedTo = null, params string[] exposure) =>
        new(exposure.Length == 0 ? ["env/**"] : exposure, text, null, kind, relatedTo) { ClientLabel = ApproverFixture.Agent };

    /// <summary>The control: with both items exposed, each found item links to the other on stripe.com's site.</summary>
    [Fact]
    public async Task ASearchOfTheWholeVault_LinksItemsOnOneSite()
    {
        using var fixture = new ApproverFixture();

        var reply = await fixture.Handler.FindAsync(Search(kind: ItemKind.ApiKey, exposure: "**"), "conn-1", Token);

        var found = Assert.Single(reply.Found);
        Assert.True(reply.VaultUnlocked && reply.Complete);
        Assert.Equal(new EntryName("env/dev", "STRIPE_KEY"), found.Entry.Listing.Name);
        Assert.Equal("billing@acme.test", found.Entry.Username);
        var link = Assert.Single(found.Links);
        Assert.Equal((new EntryName("personal", "bank"), "stripe.com"), (link.Name, link.Site));
    }

    [Fact]
    public async Task AnItemOutsideTheExposure_IsNeverFoundOrLinked()
    {
        using var fixture = new ApproverFixture();

        var stripe = await fixture.Handler.FindAsync(Search("STRIPE"), "conn-1", Token);
        var bank = await fixture.Handler.FindAsync(Search("bank"), "conn-1", Token);
        var related = await fixture.Handler.FindAsync(Search(relatedTo: EntryHandle.For(new EntryName("personal", "bank"))), "conn-1", Token);

        var found = Assert.Single(stripe.Found);
        Assert.Equal(new EntryName("env/dev", "STRIPE_KEY"), found.Entry.Listing.Name);
        Assert.Empty(found.Links);
        Assert.True(bank.VaultUnlocked);
        Assert.Empty(bank.Found);
        Assert.Empty(related.Found);
    }

    [Fact]
    public async Task ASearch_HidesUsernames_WhenSettingsHideThem()
    {
        using var fixture = new ApproverFixture();
        fixture.Rules.Rules = AgentRules.Defaults with { ShowsUsername = false };

        var reply = await fixture.Handler.FindAsync(Search(kind: ItemKind.ApiKey), "conn-1", Token);

        var found = Assert.Single(reply.Found);
        Assert.Null(found.Entry.Username);
        Assert.Equal("https://dashboard.stripe.com:443", found.Entry.Origin?.ToString());
    }

    [Fact]
    public async Task ASearch_ReadsHostsAndReferences_OnlyWhileSettingsShowThem()
    {
        using var fixture = new ApproverFixture();

        await fixture.Handler.FindAsync(Search("STRIPE"), "conn-1", Token);
        Assert.False(fixture.Source.IndexedWithLinkedHosts);

        fixture.Rules.Rules = AgentRules.Defaults with { ShowsLinkedHosts = true };
        await fixture.Handler.FindAsync(Search("STRIPE"), "conn-1", Token);
        Assert.True(fixture.Source.IndexedWithLinkedHosts);
    }

    /// <summary>A project's environments and count come from the members the exposure reaches, never from one it hides.</summary>
    [Fact]
    public async Task ASearch_NamesTheProjectsOfWhatItFound_FromExposedMembersAlone()
    {
        using var fixture = new ApproverFixture();
        fixture.Source.Tagged[new EntryName("env/dev", "STRIPE_KEY")] = ["env:billing:prod"];
        fixture.Source.Tagged[new EntryName("personal", "bank")] = ["env:billing:dev"];

        var exposed = await fixture.Handler.FindAsync(Search(kind: ItemKind.ApiKey), "conn-1", Token);
        var whole = await fixture.Handler.FindAsync(Search(kind: ItemKind.ApiKey, exposure: "**"), "conn-1", Token);
        var byProject = await fixture.Handler.FindAsync(Search(exposure: "**") with { Project = "billing" }, "conn-1", Token);

        Assert.Equal(("billing", "prod", 1), Shown(Assert.Single(exposed.Projects)));
        Assert.Equal(("billing", "dev, prod", 2), Shown(Assert.Single(whole.Projects)));
        Assert.Equal(2, byProject.Found.Count);
    }

    private static (string, string, int) Shown(AccessProject project) => (project.Name, string.Join(", ", project.Environments), project.Items);

    [Fact]
    public async Task ASearch_IsRefused_WhenTheAgentRulesCannotBeRead()
    {
        using var fixture = new ApproverFixture();
        fixture.Rules.Unreadable = "agents.toml is damaged";

        var reply = await fixture.Handler.FindAsync(Search("STRIPE"), "conn-1", Token);

        Assert.False(reply.VaultUnlocked);
        Assert.Empty(reply.Found);
        Assert.Equal(AuditMethod.Failed, reply.Refusal);
        Assert.Equal("agent rules are not usable: agents.toml is damaged", reply.Reason);
    }

    [Fact]
    public async Task AnEmptySearch_IsRefusedWithAReason()
    {
        using var fixture = new ApproverFixture();

        var reply = await fixture.Handler.FindAsync(Search("   "), "conn-1", Token);

        Assert.False(reply.VaultUnlocked);
        Assert.Empty(reply.Found);
        Assert.Equal(AuditMethod.InvalidRequest, reply.Refusal);
        Assert.Equal("a search names text, a host, a kind, a related item or a project", reply.Reason);
    }

    [Fact]
    public async Task ASearchOfAVaultThatNoLongerMatchesItsFile_IsRefusedAsChanged()
    {
        using var fixture = new ApproverFixture();
        fixture.Source.IndexFailure = CredentialFailure.ChangedOnDisk;

        var reply = await fixture.Handler.FindAsync(Search("STRIPE"), "conn-1", Token);

        Assert.False(reply.VaultUnlocked);
        Assert.Empty(reply.Found);
        Assert.Equal(AuditMethod.VaultChanged, reply.Refusal);
    }

    [Fact]
    public async Task ASearchOfALockedVault_SaysSo()
    {
        using var fixture = new ApproverFixture();
        fixture.Source.Locked = true;

        var reply = await fixture.Handler.FindAsync(Search("STRIPE"), "conn-1", Token);

        Assert.False(reply.VaultUnlocked);
        Assert.Empty(reply.Found);
        Assert.Equal("no vault is unlocked", reply.Reason);
    }

    // ------------------------------------------------------- a release nobody could send

    /// <summary>
    /// A field too large for one frame is refused, and the refusal says the release was approved.
    /// </summary>
    /// <remarks>
    /// <c>notes</c> is releasable and a KDBX note has no length limit, so this is an ordinary entry
    /// somebody pasted a certificate into. A reply built and handed to the encoder anyway would make
    /// the write throw, take the connection down with its grants, and tell the agent the approver
    /// could not be asked — about a request a person had answered.
    /// </remarks>
    [Fact]
    public async Task AReleaseTooBigToDeliver_IsRefusedAndSaysAPersonApprovedIt()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;
        fixture.Source.Value = new string('n', 100_000);

        var reply = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Equal(AuditMethod.Undeliverable, reply.Method);
        Assert.Equal(ApproverProtocol.UndeliverableReason(AuditMethod.Prompt), reply.Reason, StringComparer.Ordinal);
        Assert.Null(reply.Value);
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
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;
        fixture.Source.Value = new string('n', 100_000);

        await fixture.Handler.RequestAsync(Request(), "conn-1", Token);
        var again = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal(1, fixture.Channel.Asked);
        Assert.Equal(AuditMethod.Cooldown, again.Method);
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
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;
        fixture.Source.Value = new string('n', 100_000);

        await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.DoesNotContain(fixture.Narration, line => line.StartsWith("released ", StringComparison.Ordinal));
        Assert.Contains(fixture.Narration, line => line.Contains("could not deliver", StringComparison.Ordinal));

        // ...and it does say "released" when the reply actually leaves, so this is not passing
        // because the narration stopped working.
        fixture.Source.Value = ApproverFixture.Sentinel;
        await fixture.Handler.RequestAsync(Request(field: "username"), "conn-2", Token);

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
        Assert.Equal("a person approved this one request", first.Reason);

        // Nothing was kept, so the identical request is a fresh question rather than a cache hit.
        Assert.Equal(AuditMethod.Prompt, second.Method);
        Assert.Equal(2, fixture.Channel.Asked);
        Assert.Contains(fixture.Narration, line => line.EndsWith(" once", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AProtectedEnvironment_IsAskedEveryTime_UnderAnAllowOrAnAlwaysAllow_AndOffersNoAlways()
    {
        using var fixture = new ApproverFixture();
        var stripe = new EntryName("env/dev", "STRIPE_KEY");
        fixture.Rules.Rules = AgentRules.Defaults
            .Allowing(FieldCategory.Password, confirmed: true)
            .Remembering(new RememberedAnswer(ApproverFixture.Agent, fixture.Source.ItemId(stripe)!, "password", CategoryRule.Allow), confirmed: true);
        fixture.Source.Protected.Add(stripe);
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;

        var first = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);
        var offered = fixture.Channel.LastPrompt!.OffersAlways;
        fixture.Channel.Answer = ApprovalAnswer.AlwaysAllowed;
        var always = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal((AuditDecision.Granted, AuditMethod.Prompt), (first.Decision, first.Method));
        Assert.False(offered);
        Assert.Equal((AuditDecision.Denied, null), (always.Decision, always.Value));
        Assert.Equal(2, fixture.Channel.Asked);
        Assert.Single(fixture.Rules.Rules.Answers);
    }

    [Fact]
    public async Task AProtectedEnvironment_UnderBlock_IsRefusedUnasked()
    {
        using var fixture = new ApproverFixture();
        fixture.Rules.Rules = AgentRules.Defaults.With(ApproverFixture.Agent, FieldCategory.Password, CategoryRule.Block);
        fixture.Source.Protected.Add(new EntryName("env/dev", "STRIPE_KEY"));

        var refused = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal((AuditDecision.Denied, AuditMethod.Blocked), (refused.Decision, refused.Method));
        Assert.Equal(0, fixture.Channel.Asked);
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

    /// <summary>
    /// A refusal holds back the same field of the same entry whichever connection asks next, so an
    /// agent that reconnects or starts another bridge is refused without a second prompt (T-11).
    /// </summary>
    [Fact]
    public async Task ADenial_CoolsTheSameFieldDownForAnotherConnection()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.Denied;

        await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;
        var reconnected = await fixture.Handler.RequestAsync(Request(), "conn-2", Token);
        var otherField = await fixture.Handler.RequestAsync(Request(field: "notes"), "conn-2", Token);

        Assert.Equal(AuditMethod.Cooldown, reconnected.Method);
        Assert.Equal(AuditDecision.Granted, otherField.Decision);
        Assert.Equal(2, fixture.Channel.Asked);
    }

    /// <summary>
    /// A value holding a placeholder keypaste cannot fill in is refused after the allow under a method of its
    /// own, told to the operator's terminal, and cools its field down as a refusal does, so a retry from any
    /// connection troubles nobody while another field is still asked about.
    /// </summary>
    [Fact]
    public async Task AnAllowedValueThatCannotBeFilledIn_IsRefusedAsUnfillable_AndCoolsItsFieldDown()
    {
        using var fixture = new ApproverFixture();
        fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;
        fixture.Source.Unfilled = _unfilled;

        var refused = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);
        var again = await fixture.Handler.RequestAsync(Request(), "conn-2", Token);

        Assert.Equal((AuditDecision.Denied, AuditMethod.Unfillable), (refused.Decision, refused.Method));
        Assert.Equal($"the item's password {_unfilled}", refused.Reason);
        Assert.Null(refused.Value);
        Assert.Equal(AuditMethod.Cooldown, again.Method);
        Assert.Equal(
            $"refused env/dev/STRIPE_KEY for claude-code: the item's password {_unfilled}; edit the item so keypaste can fill it in",
            fixture.Narration[0]);

        fixture.Source.Unfilled = null;
        var otherField = await fixture.Handler.RequestAsync(Request(field: "notes"), "conn-2", Token);

        Assert.Equal(AuditDecision.Granted, otherField.Decision);
        Assert.Equal(2, fixture.Channel.Asked);
    }

    /// <summary>An Automatic release of a value keypaste cannot fill in is refused, told and cooled down the same way, with nobody asked.</summary>
    [Fact]
    public async Task AnAutomaticValueThatCannotBeFilledIn_IsRefusedAsUnfillable_AndCoolsItsFieldDown()
    {
        using var fixture = new ApproverFixture();
        fixture.Rules.Rules = AgentRules.Defaults.Allowing(FieldCategory.Password, confirmed: true);
        fixture.Source.Unfilled = _unfilled;

        var refused = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);
        var again = await fixture.Handler.RequestAsync(Request(), "conn-1", Token);

        Assert.Equal((AuditDecision.Denied, AuditMethod.Unfillable), (refused.Decision, refused.Method));
        Assert.Null(refused.Value);
        Assert.Equal(AuditMethod.Cooldown, again.Method);
        Assert.Equal(0, fixture.Channel.Asked);
        Assert.Equal(
            $"refused env/dev/STRIPE_KEY for claude-code: the item's password {_unfilled}; edit the item so keypaste can fill it in",
            fixture.Narration[0]);
    }
}
