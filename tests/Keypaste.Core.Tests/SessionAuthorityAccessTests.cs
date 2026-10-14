using System.Security.Cryptography;
using System.Text;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Xunit;
using static Keypaste.Core.Tests.AgentAccessBuilders;

namespace Keypaste.Core.Tests;

/// <summary>
/// The owner applying each agent's settings, over a real pipe: Automatic releases unasked and on its
/// own line, an agent given no values is refused before anything resolves, Always for asks before it
/// widens anything, <c>*</c> never gives more than the defaults, a spent limit asks, a widening waits
/// for the person while a narrowing applies at once, <c>clients.toml</c> only ever narrows, and an
/// agent's own request is about itself and gives its reason; a request for more from an agent or a
/// terminal, once refused or unanswered, holds back the next from any of them; a change to settings keypaste does not wholly understand is judged by what
/// the agent could do before and after it; a narrowing the person makes holds wherever it can be
/// kept, saying where it could not; Always for whose change does not take effect says why once the
/// release has left; and the person's own changes in the app neither wait on a cooldown nor start one.
/// </summary>
public sealed class SessionAuthorityAccessTests : IDisposable
{
    private const string _agent = ApproverFixture.Agent;
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);
    private static readonly EntryName _stripe = new("env/dev", "STRIPE_KEY");
    private static readonly EntryName _record = new(ReservedGroups.Agents, _agent);

    /// <summary>Settings that say Automatic, seeing everything, beside a member keypaste does not know, so they apply only beneath the defaults.</summary>
    private const string _unknownMember = """{"acaf":1,"see":["**"],"mode":"automatic","later":true}""";

    /// <summary>Settings that say Automatic, seeing everything, with a limit keypaste cannot read, so the agent is asked every time.</summary>
    private const string _unreadableLimit = """{"acaf":1,"see":["**"],"mode":"automatic","limit":{"perHour":"lots"}}""";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-authority-access-").FullName;
    private readonly ApproverFixture _fixture = new();
    private readonly OwnerLog _log;
    private readonly SessionLifetime? _lifetime = new("session-one");

    public SessionAuthorityAccessTests() => _log = new OwnerLog(_directory);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    private string ClientsPath => Path.Combine(_directory, "clients.toml");

    public void Dispose()
    {
        _lifetime?.Dispose();
        _log.Dispose();
        _fixture.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private CredentialRequest Credential(string field = "password", string? label = _agent) => new()
    {
        Entry = "env/dev/STRIPE_KEY",
        Field = field,
        Reason = "deploy billing to staging",
        TtlSeconds = 60,
        Exposure = ["**"],
        ClientName = "claude-code",
        ClientVersion = "2.1.0",
        ClientLabel = label,
        Vault = VaultPath,
        Session = "session-one",
    };

    private AccessChangeRequest Change(string agent = _agent) => new(agent, string.Empty) { Vault = VaultPath, Session = "session-one" };

    private AccessChangeRequest Asked(string agent = _agent) => Change(agent) with { Origin = AccessOrigin.Agent, Reason = "the deploy needs it" };

    private List<string?> Lines(string member) => [.. _log.Lines().Select(line => line.GetProperty(member).GetString())];

    [Fact]
    public async Task Automatic_ReleasesUnasked_OnALineOfItsOwn()
    {
        _fixture.Settings.Records[_agent] = ApproverFixture.SeesAll with { Mode = AgentMode.Automatic };
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        var reply = await client.RequestAsync(Credential(), Token);

        Assert.Equal((AuditDecision.Granted, AuditMethod.Automatic), (reply!.Decision, reply.Method));
        Assert.Equal(ApproverFixture.Sentinel, reply.Value);
        Assert.Equal(0, _fixture.Channel.Asked);
        Assert.Equal(["automatic"], Lines("method"));
        Assert.Equal(0, _fixture.Grants.Count);
    }

    /// <summary>An agent given no values learns nothing of the vault from a credential request: nothing is even resolved.</summary>
    [Fact]
    public async Task AnAgentGivenNoValues_IsRefusedBeforeAnythingResolves()
    {
        _fixture.Settings.Records[_agent] = ApproverFixture.SeesAll with { Values = false, Mode = AgentMode.Automatic };
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        var reply = await client.RequestAsync(Credential(), Token);

        Assert.Equal(AuditMethod.InjectOnly, reply!.Method);
        Assert.Null(reply.Value);
        Assert.Equal((0, 0, 0), (_fixture.Source.Resolves, _fixture.Source.Reads, _fixture.Channel.Asked));
    }

    /// <summary><c>*</c>, an unnamed bridge and a name with no record get no more than the defaults, whatever a <c>*</c> record says.</summary>
    [Fact]
    public async Task TheStarSettings_NeverGiveMoreThanTheDefaults()
    {
        _fixture.Source.Tagged[_stripe] = ["env:billing"];
        _fixture.Settings.Records[AgentNames.Any] = Automatic with { See = See("**"), Run = true };
        _fixture.Channel.Answer = ApprovalAnswer.Denied;
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        var unnamed = await client.RequestAsync(Credential("STRIPE_SECRET_KEY", label: null), Token);
        var stranger = await client.RequestAsync(Credential("STRIPE_SECRET_KEY", label: "stranger") with { Reason = "another reason" }, Token);
        var password = await client.RequestAsync(Credential(label: "stranger"), Token);
        var widen = await client.ChangeAccessAsync(Change(AgentNames.Any) with { Mode = "automatic" }, Token);

        Assert.Equal((AuditMethod.Prompt, AuditMethod.Cooldown), (unnamed!.Method, stranger!.Method));
        Assert.Equal(AuditMethod.OutOfScope, password!.Method);
        Assert.Equal(1, _fixture.Channel.Asked);
        Assert.Equal((AuditDecision.Denied, AuditMethod.InvalidRequest), (widen!.Decision, widen.Method));
        Assert.Empty(_fixture.Settings.Written);
        Assert.Equal(0, _fixture.Channel.AccessAsked);
    }

    /// <summary>A limit that is spent asks, with the timed grant offered, rather than refusing.</summary>
    [Fact]
    public async Task ASpentHourlyLimit_AsksInstead()
    {
        _fixture.Settings.Records[_agent] = ApproverFixture.SeesAll with { Mode = AgentMode.Automatic, Limit = new AccessLimit { PerHour = 1 } };
        _fixture.Channel.Answer = ApprovalAnswer.Denied;
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        var first = await client.RequestAsync(Credential(), Token);
        var second = await client.RequestAsync(Credential(), Token);

        Assert.Equal((AuditMethod.Automatic, AuditMethod.Prompt), (first!.Method, second!.Method));
        Assert.Equal(1, _fixture.Channel.Asked);
        Assert.Equal(ApprovalLimits.Default.MaximumTtlSeconds, _fixture.Channel.LastPrompt!.TtlSeconds);

        _fixture.Clock.Advance(TimeSpan.FromHours(1) + ApprovalLimits.Default.DenialCooldown + TimeSpan.FromSeconds(1));
        Assert.Equal(AuditMethod.Automatic, (await client.RequestAsync(Credential(), Token))!.Method);
    }

    /// <summary>Always for allows this one request, then asks the person before the environment becomes Automatic, on a line of its own.</summary>
    [Fact]
    public async Task AlwaysFor_AllowsOnce_ThenAsksBeforeMakingTheEnvironmentAutomatic()
    {
        _fixture.Source.Tagged[_stripe] = ["env:billing"];
        _fixture.Channel.Answer = ApprovalAnswer.AlwaysForProject;
        _fixture.Channel.AccessAnswer = ApprovalAnswer.ApprovedOnce;
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        var reply = await client.RequestAsync(Credential("STRIPE_SECRET_KEY"), Token);

        Assert.Equal((AuditMethod.Prompt, 0), (reply!.Method, reply.TtlSeconds));
        Assert.Equal(ApproverFixture.Sentinel, reply.Value);
        Assert.Equal(Scope("billing:dev"), _fixture.Channel.LastPrompt!.AlwaysFor);
        Assert.Equal(AccessOrigin.Prompt, _fixture.Channel.LastAccessPrompt!.Origin);
        Assert.Contains(Row("billing:dev", AgentMode.Automatic), _fixture.Settings.Records[_agent].Projects);
        Assert.Equal(["access_change", "request_credential"], Lines("tool"));
        Assert.Equal(["prompt", "prompt"], Lines("method"));
        Assert.Equal(0, _fixture.Grants.Count);

        var next = await client.RequestAsync(Credential("STRIPE_SECRET_KEY"), Token);
        Assert.Equal(AuditMethod.Automatic, next!.Method);
        Assert.Equal(1, _fixture.Channel.Asked);
        Assert.Empty(_fixture.Channel.Told);
    }

    [Fact]
    public async Task AlwaysFor_Refused_KeepsItToTheOneRelease()
    {
        _fixture.Source.Tagged[_stripe] = ["env:billing"];
        _fixture.Channel.Answer = ApprovalAnswer.AlwaysForProject;
        _fixture.Channel.AccessAnswer = ApprovalAnswer.Denied;
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        var reply = await client.RequestAsync(Credential("STRIPE_SECRET_KEY"), Token);

        Assert.Equal(ApproverFixture.Sentinel, reply!.Value);
        Assert.Empty(_fixture.Settings.Written);
        Assert.Equal(["denied", "granted"], Lines("decision"));
        Assert.Empty(_fixture.Channel.Told);
    }

    /// <summary>
    /// When the change Always for asks for is confirmed and cannot be saved, the release stands once and
    /// the person is told, where they answered, what was not made Automatic and why, instead of the
    /// operator's terminal saying it on a line of its own.
    /// </summary>
    [Fact]
    public async Task AlwaysFor_WhoseChangeIsNotSaved_TellsThePersonWhy()
    {
        _fixture.Source.Tagged[_stripe] = ["env:billing"];
        _fixture.Channel.Answer = ApprovalAnswer.AlwaysForProject;
        _fixture.Channel.AccessAnswer = ApprovalAnswer.ApprovedOnce;
        _fixture.Settings.Saving = new AgentSave(AgentSaveOutcome.Failed, "the disk is full");
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        var reply = await client.RequestAsync(Credential("STRIPE_SECRET_KEY"), Token);

        Assert.Equal((AuditDecision.Granted, ApproverFixture.Sentinel), (reply!.Decision, reply.Value));
        var told = Assert.Single(_fixture.Channel.Told);
        Assert.Equal("billing:dev was not made Automatic for claude-code: the vault was not saved: the disk is full.", told.Unmade);
        Assert.Equal(["denied", "granted"], Lines("decision"));
        Assert.DoesNotContain(_fixture.Narration, line => line.StartsWith("did not change", StringComparison.Ordinal));

        var next = await client.RequestAsync(Credential("STRIPE_SECRET_KEY"), Token);
        Assert.Equal(AuditMethod.Prompt, next!.Method);
    }

    /// <summary>
    /// Always for keeps the prompt's one slot until its confirmation is answered, so another request
    /// arriving between the two is refused as busy rather than put in front of the person who was just
    /// told to confirm, and the slot is free again once the change is made.
    /// </summary>
    [Fact]
    public async Task AlwaysFor_KeepsThePersonForItsConfirmation()
    {
        _fixture.Source.Tagged[_stripe] = ["env:billing"];
        _fixture.Channel.Answer = ApprovalAnswer.AlwaysForProject;
        _fixture.Channel.AccessAnswer = ApprovalAnswer.ApprovedOnce;
        var other = ApprovalPrompt.For("codex", new EntryName("personal", "bank"), "password", "another agent's request", 0);
        Task<ApprovalAnswer>? between = null;
        _fixture.Source.During = () => between ??= _fixture.Gate.AskAsync("between", other, Token).AsTask();
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        var reply = await client.RequestAsync(Credential("STRIPE_SECRET_KEY"), Token);

        Assert.Equal(ApproverFixture.Sentinel, reply!.Value);
        Assert.Equal(ApprovalAnswer.Busy, await between!);
        Assert.Equal((1, 1), (_fixture.Channel.Asked, _fixture.Channel.AccessAsked));
        Assert.Equal(ApprovalAnswer.AlwaysForProject, await _fixture.Gate.AskAsync("after", other, Token));
    }

    /// <summary>
    /// Always for on an environment whose Automatic has passed its end asks to make it Automatic with no
    /// end, its hourly limit kept, rather than writing the row as it was and calling that applied, so the
    /// next request is released.
    /// </summary>
    [Fact]
    public async Task AlwaysFor_WhereAutomaticHasEnded_AsksToMakeItAutomaticWithNoEnd()
    {
        _fixture.Source.Tagged[_stripe] = ["env:billing"];
        var ended = new AccessLimit { Until = _fixture.Clock.GetUtcNow().AddHours(-1), PerHour = 5 };
        _fixture.Settings.Records[_agent] = ApproverFixture.SeesAll with { Projects = [Row("billing:dev", AgentMode.Automatic, ended)] };
        _fixture.Channel.Answer = ApprovalAnswer.AlwaysForProject;
        _fixture.Channel.AccessAnswer = ApprovalAnswer.ApprovedOnce;
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        var reply = await client.RequestAsync(Credential("STRIPE_SECRET_KEY"), Token);

        Assert.Equal(ApproverFixture.Sentinel, reply!.Value);
        Assert.Equal(Scope("billing:dev"), _fixture.Channel.LastPrompt!.AlwaysFor);
        Assert.Equal(1, _fixture.Channel.AccessAsked);
        Assert.Equal(Row("billing:dev", AgentMode.Automatic, new AccessLimit { PerHour = 5 }), Assert.Single(_fixture.Settings.Records[_agent].Projects));
        Assert.Empty(_fixture.Channel.Told);

        var next = await client.RequestAsync(Credential("STRIPE_SECRET_KEY"), Token);
        Assert.Equal(AuditMethod.Automatic, next!.Method);
        Assert.Equal(1, _fixture.Channel.Asked);
    }

    /// <summary>
    /// Always for whose change gives nothing, because the agent's saved settings already make the
    /// environment Automatic but keypaste does not wholly understand them, tells the person why it is still
    /// asked about rather than calling it made.
    /// </summary>
    [Fact]
    public async Task AlwaysFor_ThatLeavesTheEnvironmentAsked_TellsThePersonWhy()
    {
        _fixture.Source.Tagged[_stripe] = ["env:billing"];
        _fixture.Settings.Records[_agent] = ApproverFixture.SeesAll with { Projects = [Row("billing:dev", AgentMode.Automatic)] };
        _fixture.Settings.Unreadable.Add(_agent);
        _fixture.Channel.Answer = ApprovalAnswer.AlwaysForProject;
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        var reply = await client.RequestAsync(Credential("STRIPE_SECRET_KEY"), Token);

        Assert.Equal((AuditDecision.Granted, ApproverFixture.Sentinel), (reply!.Decision, reply.Value));
        Assert.Equal(0, _fixture.Channel.AccessAsked);
        var told = Assert.Single(_fixture.Channel.Told);
        Assert.Equal($"billing:dev was not made Automatic for claude-code: {AccessChangePrompt.BeneathDefaultsText}.", told.Unmade);
        Assert.Equal(["granted", "granted"], Lines("decision"));
    }

    /// <summary>
    /// A refusal the person gives their own change in the app holds nothing back, so they can try it again
    /// at once, and an agent's unanswered request for the same never holds back theirs; the app's own
    /// screen is not something any program can send a request as.
    /// </summary>
    [Fact]
    public async Task ThePersonsOwnChangesInTheApp_NeitherWaitOnACooldownNorStartOne()
    {
        var authority = new SessionAuthority(
            VaultIdentity.Of(_directory, VaultPath),
            () => _lifetime,
            _fixture.Handler,
            () => _log.Log,
            clock: _fixture.Clock);
        Assert.True(AccessEdit.TryCreate(null, "automatic", null, null, null, null, null, false, out var automatic, out var error), error);

        _fixture.Channel.AccessAnswer = ApprovalAnswer.Denied;
        var refused = await authority.ChangeAccessAsync(_agent, automatic, "keypaste app", Token);
        var retried = await authority.ChangeAccessAsync(_agent, automatic, "keypaste app", Token);

        Assert.Equal((AuditMethod.Prompt, AuditMethod.Prompt), (refused.Method, retried.Method));
        Assert.Equal(2, _fixture.Channel.AccessAsked);

        await using var owner = Owner.Start(this);
        await using var agent = await owner.AttachAsync();
        _fixture.Channel.AccessAnswer = ApprovalAnswer.TimedOut;
        var unanswered = await agent.ChangeAccessAsync(Asked() with { Mode = "automatic" }, Token);

        _fixture.Channel.AccessAnswer = ApprovalAnswer.ApprovedOnce;
        var confirmed = await authority.ChangeAccessAsync(_agent, automatic, "keypaste app", Token);

        Assert.Equal(AuditMethod.TimedOut, unanswered!.Method);
        Assert.Equal((AuditDecision.Granted, AuditMethod.Prompt), (confirmed.Decision, confirmed.Method));
        Assert.Equal(AgentMode.Automatic, _fixture.Settings.Records[_agent].Mode);
        Assert.Equal(4, _fixture.Channel.AccessAsked);
    }

    /// <summary>A confirmation of a change is listed while the person is asked, with where it says it came from, and not after.</summary>
    [Fact]
    public async Task Activity_ListsTheWaitingChange_WhileThePersonIsAsked()
    {
        await using var owner = Owner.Start(this);
        await using var agent = await owner.AttachAsync();
        IReadOnlyList<WaitingChange> listed = [];
        _fixture.Channel.WhileConfirming = () => listed = owner.Authority.Activity.WaitingChanges;

        await agent.ChangeAccessAsync(Asked() with { Mode = "automatic" }, Token);

        var waiting = Assert.Single(listed);
        Assert.Equal((_agent, AccessOrigin.Agent), (waiting.Prompt.Agent, waiting.Prompt.Origin));
        Assert.True(waiting.Remaining > TimeSpan.Zero);
        Assert.Empty(owner.Authority.Activity.WaitingChanges);
        Assert.Empty(owner.Authority.Activity.Waiting);
    }

    /// <summary>An Always for answer to a prompt that did not offer it is an answer nobody could have meant: nothing is read or changed.</summary>
    [Fact]
    public async Task AnAlwaysForAnswer_ToAPromptThatDidNotOfferIt_ReleasesNothing()
    {
        _fixture.Channel.Answer = ApprovalAnswer.AlwaysForProject;
        _fixture.Channel.AccessAnswer = ApprovalAnswer.ApprovedOnce;
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        var reply = await client.RequestAsync(Credential(), Token);

        Assert.Null(_fixture.Channel.LastPrompt!.AlwaysFor);
        Assert.Equal((AuditDecision.Denied, AuditMethod.Failed), (reply!.Decision, reply.Method));
        Assert.Null(reply.Value);
        Assert.Equal((0, 0), (_fixture.Source.Reads, _fixture.Channel.AccessAsked));
        Assert.Empty(_fixture.Settings.Written);
    }

    [Fact]
    public async Task ANarrowing_AppliesAtOnce_AndAWidening_WaitsForThePerson()
    {
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync(withClient: false);

        var narrowed = await client.ChangeAccessAsync(Change() with { Mode = "ask" }, Token);

        Assert.Equal((AuditDecision.Granted, AuditMethod.Narrowing), (narrowed!.Decision, narrowed.Method));
        Assert.Equal(0, _fixture.Channel.AccessAsked);
        Assert.Equal(AgentMode.Ask, _fixture.Settings.Records[_agent].Mode);
        Assert.Equal(AgentMode.Ask, Assert.Single(_fixture.Settings.Caps.Rows).Mode);

        _fixture.Channel.AccessAnswer = ApprovalAnswer.Denied;
        var refused = await client.ChangeAccessAsync(Change() with { Mode = "automatic" }, Token);

        Assert.Equal((AuditDecision.Denied, AuditMethod.Prompt), (refused!.Decision, refused.Method));
        Assert.Equal(AgentMode.Ask, _fixture.Settings.Records[_agent].Mode);
        Assert.Equal(AccessOrigin.Terminal, _fixture.Channel.LastAccessPrompt!.Origin);
        Assert.Contains(_fixture.Channel.LastAccessPrompt.Gains, gain => gain.StartsWith("by default: Automatic", StringComparison.Ordinal));
        Assert.False(_fixture.Channel.LastAccessPrompt.StaysBeneathDefaults);

        _fixture.Clock.Advance(ApprovalLimits.Default.DenialCooldown + TimeSpan.FromSeconds(1));
        _fixture.Channel.AccessAnswer = ApprovalAnswer.ApprovedOnce;
        var confirmed = await client.ChangeAccessAsync(Change() with { Mode = "automatic" }, Token);
        var released = await client.RequestAsync(Credential(), Token);

        Assert.Equal((AuditDecision.Granted, AuditMethod.Prompt), (confirmed!.Decision, confirmed.Method));
        Assert.Equal(AuditMethod.Automatic, released!.Method);
        Assert.Equal(["narrowing", "prompt", "prompt", "automatic"], Lines("method"));
        Assert.All(_log.Lines().Take(3), line =>
        {
            Assert.Equal("keypaste mcp policy", line.GetProperty("client").GetProperty("name").GetString());
            Assert.Equal(_agent, line.GetProperty("client").GetProperty("label").GetString());
            Assert.Contains("mode ", line.GetProperty("reason").GetString()!, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// A narrowing the person makes reaches the vault even when this computer's ceilings cannot keep it,
    /// because the file is full or cannot be written, and the reply says the ceiling was not kept.
    /// </summary>
    [Fact]
    public async Task APersonsNarrowing_ReachesTheVault_WhenThisComputersCeilingsCannotKeepIt()
    {
        _fixture.Settings.Caps = Full();
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync(withClient: false);

        var full = await client.ChangeAccessAsync(Change() with { Mode = "ask" }, Token);

        _fixture.Settings.Caps = AccessCaps.Empty;
        _fixture.Settings.CapsProblem = "the disk is full";
        var unwritable = await client.ChangeAccessAsync(Change() with { Values = "no" }, Token);

        Assert.Equal((AuditDecision.Granted, AuditMethod.Narrowing), (full!.Decision, full.Method));
        Assert.Contains(
            $"applied at once in the vault, since it gives the agent nothing more, but this computer's ceilings could not hold it: clients.toml holds at most {AccessCaps.MaximumRows} rows",
            full.Reason,
            StringComparison.Ordinal);
        Assert.Equal((AuditDecision.Granted, AuditMethod.Narrowing), (unwritable!.Decision, unwritable.Method));
        Assert.Contains("but this computer's ceilings could not be written: the disk is full", unwritable.Reason, StringComparison.Ordinal);
        Assert.Equal((AgentMode.Ask, false), (_fixture.Settings.Records[_agent].Mode, _fixture.Settings.Records[_agent].Values));
        Assert.Empty(_fixture.Settings.CapsWritten);
        Assert.Equal(0, _fixture.Channel.AccessAsked);
        Assert.Equal(["narrowing", "narrowing"], Lines("method"));
    }

    /// <summary>An agent's own narrowing is kept on this computer alone, so one the ceilings cannot keep is refused and the vault is left as it was.</summary>
    [Fact]
    public async Task AnAgentsNarrowing_ThatThisComputersCeilingsCannotKeep_IsRefused()
    {
        _fixture.Settings.Caps = Full();
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        var refused = await client.ChangeAccessAsync(Asked() with { Mode = "ask" }, Token);

        Assert.Equal((AuditDecision.Denied, AuditMethod.Failed), (refused!.Decision, refused.Method));
        Assert.Contains("this computer's ceilings could not hold it", refused.Reason, StringComparison.Ordinal);
        Assert.Empty(_fixture.Settings.Written);
        Assert.Empty(_fixture.Settings.CapsWritten);
    }

    /// <summary>
    /// A narrowing that would take the agent's record past the most project rows it keeps still holds on
    /// this computer, and the reply says the vault was not saved.
    /// </summary>
    [Fact]
    public async Task APersonsNarrowing_ThatTheRecordCannotTake_StillHoldsOnThisComputer()
    {
        using var vault = Vault.Create(VaultPath, EnvStoreTests.MasterPassword);
        var rows = string.Join(',', Enumerable.Range(0, AgentAccess.MaximumProjects).Select(i => $$"""{"project":"p{{i}}","mode":"automatic"}"""));
        Hold(vault, $$"""{"acaf":1,"see":["**"],"mode":"automatic","projects":[{{rows}}]}""");
        await using var owner = Owner.Start(this, handler: Saving(vault));
        await using var client = await owner.AttachAsync(withClient: false);

        var narrowed = await client.ChangeAccessAsync(Change() with { Project = "billing", Mode = "ask" }, Token);

        Assert.Equal((AuditDecision.Granted, AuditMethod.Narrowing), (narrowed!.Decision, narrowed.Method));
        Assert.Contains(
            $"and the vault was not saved: this agent already has {AgentAccess.MaximumProjects} project rows, the most its settings keep; remove one it no longer needs, then try again",
            narrowed.Reason,
            StringComparison.Ordinal);
        Assert.Equal(AgentAccess.MaximumProjects, Held(vault).Access.Projects.Count);
        Assert.True(new AccessCapsSource(ClientsPath).TryCurrent(out var caps, out var problem), problem);
        var ceiling = Assert.Single(caps.Rows);
        Assert.Equal((Scope("billing"), AgentMode.Ask), (ceiling.Project, ceiling.Mode));
    }

    /// <summary>
    /// A change to a project row of a record holding more rows than keypaste reads is not written to the
    /// vault, and the reply says how to fix that record rather than only naming the bound.
    /// </summary>
    [Fact]
    public async Task APersonsNarrowing_OfARecordHoldingMoreRowsThanKeypasteReads_SaysHowToFixIt()
    {
        using var vault = Vault.Create(VaultPath, EnvStoreTests.MasterPassword);
        var rows = string.Join(',', Enumerable.Range(0, AgentAccess.MaximumProjects + 1).Select(i => $$"""{"project":"p{{i}}","mode":"ask"}"""));
        Hold(vault, $$"""{"acaf":1,"mode":"ask","projects":[{{rows}}]}""");
        await using var owner = Owner.Start(this, handler: Saving(vault));
        await using var client = await owner.AttachAsync(withClient: false);

        var narrowed = await client.ChangeAccessAsync(Change() with { Project = "billing", Mode = "ask" }, Token);

        Assert.Equal((AuditDecision.Granted, AuditMethod.Narrowing), (narrowed!.Decision, narrowed.Method));
        Assert.Contains(
            $"and the vault was not saved: the vault holds more than {AgentAccess.MaximumProjects} project rows for this agent, which keypaste does not read: remove some in KeePassXC, or reset this agent's settings",
            narrowed.Reason,
            StringComparison.Ordinal);
        Assert.False(Held(vault).Understood);
        Assert.Equal(0, _fixture.Channel.AccessAsked);
    }

    /// <summary>
    /// A widening of settings keypaste does not wholly understand is saved as the person allows it, and
    /// both the confirmation and the reply say the agent gets no more than the defaults until they are
    /// fixed, which its next request shows.
    /// </summary>
    [Fact]
    public async Task AWidening_OfSettingsKeypasteDoesNotWhollyUnderstand_SaysItWaitsForThemToBeFixed()
    {
        _fixture.Source.Tagged[_stripe] = ["env:billing"];
        _fixture.Settings.Unreadable.Add(_agent);
        _fixture.Channel.AccessAnswer = ApprovalAnswer.ApprovedOnce;
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        var confirmed = await client.ChangeAccessAsync(Change() with { Mode = "automatic" }, Token);
        var asked = await client.RequestAsync(Credential("STRIPE_SECRET_KEY"), Token);

        Assert.True(_fixture.Channel.LastAccessPrompt!.StaysBeneathDefaults);
        Assert.Equal((AuditDecision.Granted, AuditMethod.Prompt), (confirmed!.Decision, confirmed.Method));
        Assert.Contains($"a person confirmed it, and the vault holds it, but {AccessChangePrompt.BeneathDefaultsText}", confirmed.Reason, StringComparison.Ordinal);
        Assert.Equal(AgentMode.Automatic, _fixture.Settings.Records[_agent].Mode);
        Assert.Equal(AuditMethod.Prompt, asked!.Method);
        Assert.Equal(1, _fixture.Channel.Asked);
    }

    /// <summary>
    /// A narrowing of settings keypaste does not wholly understand applies at once and asks nobody, though
    /// the record still says Automatic: while the record is not understood that stays inactive, and once a
    /// narrowing takes it away this computer keeps it taken away too.
    /// </summary>
    [Fact]
    public async Task ANarrowing_OfSettingsKeypasteDoesNotWhollyUnderstand_AppliesAtOnce_LeavingWhatTheySayInactive()
    {
        _fixture.Source.Tagged[_stripe] = ["env:billing"];
        using var vault = Vault.Create(VaultPath, EnvStoreTests.MasterPassword);
        Hold(vault, _unknownMember);
        await using var owner = Owner.Start(this, handler: Saving(vault));
        await using var client = await owner.AttachAsync();

        var asked = await client.RequestAsync(Credential("STRIPE_SECRET_KEY"), Token);
        var valuesOff = await client.ChangeAccessAsync(Change() with { Values = "no" }, Token);
        var held = Held(vault);

        Assert.Equal(AuditMethod.Prompt, asked!.Method);
        Assert.Equal((AuditDecision.Granted, AuditMethod.Narrowing), (valuesOff!.Decision, valuesOff.Method));
        Assert.Equal(0, _fixture.Channel.AccessAsked);
        Assert.False(held.Understood);
        Assert.Equal((false, AgentMode.Automatic), (held.Said.Values, held.Said.Mode));

        var lowered = await client.ChangeAccessAsync(Change() with { Mode = "allow-for-a-while" }, Token);
        var refused = await client.RequestAsync(Credential("STRIPE_SECRET_KEY"), Token);

        Assert.Equal((AuditDecision.Granted, AuditMethod.Narrowing), (lowered!.Decision, lowered.Method));
        Assert.Equal(AuditMethod.InjectOnly, refused!.Method);
        Assert.Equal((0, 1), (_fixture.Channel.AccessAsked, _fixture.Channel.Asked));
        Assert.Equal(AgentMode.AllowForAWhile, Held(vault).Said.Mode);
        Assert.True(new AccessCapsSource(ClientsPath).TryCurrent(out var caps, out var problem), problem);
        var ceiling = Assert.Single(caps.Rows);
        Assert.Equal((false, AgentMode.AllowForAWhile), (ceiling.Values, ceiling.Mode));
    }

    /// <summary>
    /// A change that leaves settings keypaste did not wholly understand understood asks for all they then
    /// give, what they said before included, and none of it applies until the person allows it.
    /// </summary>
    [Fact]
    public async Task AChangeThatMakesTheSettingsWhollyUnderstood_AsksForEverythingTheyThenGive()
    {
        _fixture.Source.Tagged[_stripe] = ["env:billing"];
        using var vault = Vault.Create(VaultPath, EnvStoreTests.MasterPassword);
        Hold(vault, _unreadableLimit);
        _fixture.Channel.AccessAnswer = ApprovalAnswer.Denied;
        await using var owner = Owner.Start(this, handler: Saving(vault));
        await using var client = await owner.AttachAsync();

        var refused = await client.ChangeAccessAsync(Change() with { PerHour = "10" }, Token);

        Assert.Equal((AuditDecision.Denied, AuditMethod.Prompt), (refused!.Decision, refused.Method));
        Assert.Contains(_fixture.Channel.LastAccessPrompt!.Gains, gain => gain.StartsWith("by default: Automatic", StringComparison.Ordinal));
        Assert.Contains("sees **", _fixture.Channel.LastAccessPrompt.Gains);
        Assert.False(_fixture.Channel.LastAccessPrompt.StaysBeneathDefaults);
        Assert.Equal(_unreadableLimit, vault.ReadField(_record, AgentAccessStore.Field));

        _fixture.Clock.Advance(ApprovalLimits.Default.DenialCooldown + TimeSpan.FromSeconds(1));
        _fixture.Channel.AccessAnswer = ApprovalAnswer.ApprovedOnce;
        var confirmed = await client.ChangeAccessAsync(Change() with { PerHour = "10" }, Token);
        var released = await client.RequestAsync(Credential("STRIPE_SECRET_KEY"), Token);

        Assert.Equal((AuditDecision.Granted, AuditMethod.Prompt), (confirmed!.Decision, confirmed.Method));
        Assert.True(Held(vault).Understood);
        Assert.Equal(AuditMethod.Automatic, released!.Method);
        Assert.Equal((2, 0), (_fixture.Channel.AccessAsked, _fixture.Channel.Asked));
    }

    /// <summary>
    /// A confirmed widening lifts this computer's ceilings from what <c>clients.toml</c> holds once the
    /// person has answered, so a ceiling written while they were asked, by hand or by another agent
    /// narrowing itself, stays.
    /// </summary>
    [Fact]
    public async Task AConfirmedWidening_KeepsTheCeilingsWrittenWhileThePersonWasAsked()
    {
        using var vault = Vault.Create(VaultPath, EnvStoreTests.MasterPassword);
        Hold(vault, """{"acaf":1,"see":["**"]}""");
        File.WriteAllText(ClientsPath, $"[[client]]\nlabel = \"{_agent}\"\nvalues = \"no\"\n");
        _fixture.Channel.AccessAnswer = ApprovalAnswer.ApprovedOnce;
        _fixture.Channel.WhileConfirming = () => File.AppendAllText(ClientsPath, "[[client]]\nlabel = \"codex\"\nrun = \"off\"\n");
        await using var owner = Owner.Start(this, handler: Saving(vault));
        await using var client = await owner.AttachAsync(withClient: false);

        var confirmed = await client.ChangeAccessAsync(Change() with { Values = "yes" }, Token);

        Assert.Equal((AuditDecision.Granted, AuditMethod.Prompt), (confirmed!.Decision, confirmed.Method));
        Assert.DoesNotContain("ceilings could not be lifted", confirmed.Reason, StringComparison.Ordinal);
        Assert.True(Held(vault).Access.Values);
        Assert.True(new AccessCapsSource(ClientsPath).TryCurrent(out var caps, out var problem), problem);
        var kept = Assert.Single(caps.Rows);
        Assert.Equal(("codex", (bool?)false), (kept.Label, kept.Run));
    }

    /// <summary>A setting in <c>clients.toml</c> can only take away: one that says more than the vault changes nothing, and one that says less holds.</summary>
    [Fact]
    public async Task AHandEditToClientsToml_OnlyNarrows()
    {
        _fixture.Settings.Records[_agent] = ApproverFixture.SeesAll with { Mode = AgentMode.Ask };
        _fixture.Settings.Caps = Parsed("[[client]]\nlabel = \"claude-code\"\nmode = \"automatic\"\nvalues = \"yes\"\nrun = \"on\"\nsee = [\"**\"]\n");
        _fixture.Channel.Answer = ApprovalAnswer.Denied;
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        var asked = await client.RequestAsync(Credential(), Token);

        Assert.Equal(AuditMethod.Prompt, asked!.Method);
        Assert.Equal(OnceOnly.AskEveryTime, _fixture.Channel.LastPrompt!.OnceOnly);

        _fixture.Settings.Caps = Parsed("[[client]]\nlabel = \"claude-code\"\nvalues = \"no\"\n");
        var held = await client.RequestAsync(Credential("username"), Token);

        Assert.Equal(AuditMethod.InjectOnly, held!.Method);
        Assert.Equal(1, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task AnUnusableClientsFile_RefusesEveryRequest_AndEveryChange()
    {
        _fixture.Settings.Problem = "clients.toml: line 3: 'policy' is not a setting";
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        var credential = await client.RequestAsync(Credential(), Token);
        var listing = await client.ListAsync(new NamesRequest(["**"]) { ClientLabel = _agent, Vault = VaultPath, Session = "session-one" }, Token);
        var change = await client.ChangeAccessAsync(Change() with { Mode = "ask" }, Token);

        Assert.Equal(AuditMethod.Failed, credential!.Method);
        Assert.Empty(listing!.Names);
        Assert.Equal((AuditDecision.Denied, AuditMethod.Failed), (change!.Decision, change.Method));
        Assert.Equal(0, _fixture.Source.Resolves);
        Assert.Empty(_fixture.Settings.Written);
    }

    /// <summary>A change whose line could not follow it is never made (T-6).</summary>
    [Fact]
    public async Task AChangeTheLogCannotRecord_IsNotMade()
    {
        await using var owner = Owner.Start(this, audit: () => null);
        await using var client = await owner.AttachAsync(withClient: false);

        var reply = await client.ChangeAccessAsync(Change() with { Mode = "ask" }, Token);

        Assert.Equal((AuditDecision.Denied, AuditMethod.Failed), (reply!.Decision, reply.Method));
        Assert.False(reply.Recorded);
        Assert.Empty(_fixture.Settings.Written);
        Assert.Empty(_fixture.Settings.CapsWritten);
    }

    [Fact]
    public async Task AListing_NamesOnlyWhatTheAgentCanSee()
    {
        _fixture.Settings.Records[_agent] = AgentAccess.Defaults with { See = See("personal/**") };
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        var named = await client.ListAsync(new NamesRequest(["**"]) { ClientLabel = _agent, Vault = VaultPath, Session = "session-one" }, Token);
        var unnamed = await client.ListAsync(new NamesRequest(["**"]) { Vault = VaultPath, Session = "session-one" }, Token);

        Assert.Equal([new EntryName("personal", "bank")], named!.Names.Select(entry => entry.Name));
        Assert.Empty(unnamed!.Names);
    }

    [Fact]
    public async Task TheVaultsSettings_AreListed_AndTheListingIsNotRecorded()
    {
        _fixture.Settings.Records[AgentNames.Any] = Asks;
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync(withClient: false);

        var reply = await client.ListAccessAsync(new AccessListRequest { Vault = VaultPath, Session = "session-one" }, Token);

        Assert.True(reply!.Answered);
        Assert.Equal([AgentNames.Any, _agent], reply.Agents.Select(row => row.Name));
        Assert.Equal(AgentMode.Ask, AcafRecord.Read(reply.Agents[0].Record).Access.Mode);
        Assert.Empty(_log.Lines());
    }

    /// <summary>A request that says an agent asked through its own tool is admitted only for the agent its connection attached as, and is recorded either way.</summary>
    [Fact]
    public async Task AnAgentsOwnRequest_IsAboutTheAgentItsConnectionAttachedAs()
    {
        _fixture.Channel.AccessAnswer = ApprovalAnswer.ApprovedOnce;
        await using var owner = Owner.Start(this);
        await using var named = await owner.AttachAsync();
        await using var unnamed = await owner.AttachAsync(withClient: false);

        var another = await named.ChangeAccessAsync(Asked("cursor") with { Run = "on" }, Token);
        var nobody = await unnamed.ChangeAccessAsync(Asked() with { Run = "on" }, Token);

        Assert.Equal((AuditMethod.InvalidRequest, AuditMethod.InvalidRequest), (another!.Method, nobody!.Method));
        Assert.Equal(0, _fixture.Channel.AccessAsked);
        Assert.Empty(_fixture.Settings.Written);
        Assert.Equal(["denied", "denied"], Lines("decision"));
        Assert.Equal("cursor", _log.Lines()[0].GetProperty("client").GetProperty("label").GetString());
        Assert.False(_log.Lines()[1].GetProperty("client").TryGetProperty("name", out _));
    }

    /// <summary>
    /// An agent's own request meets the bridge's rules again here: it gives its reason, a project row
    /// holds only a mode and its limits, and a limit is a bound, its end in UTC as the bridge sends it,
    /// never a limit's removal or a reset, which the tool never sends.
    /// </summary>
    [Fact]
    public async Task AnAgentsOwnRequest_MeetsTheBridgesRules()
    {
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        var unexplained = await client.ChangeAccessAsync(Asked() with { Reason = string.Empty, Mode = "ask" }, Token);
        var row = await client.ChangeAccessAsync(Asked() with { Project = "billing", Run = "on" }, Token);
        var uncapped = await client.ChangeAccessAsync(Asked() with { PerHour = AccessEdit.None }, Token);
        var unending = await client.ChangeAccessAsync(Asked() with { Until = AccessEdit.None }, Token);
        var local = await client.ChangeAccessAsync(Asked() with { Until = "2026-12-31" }, Token);
        var reset = await client.ChangeAccessAsync(Asked() with { Project = "billing", Reset = true }, Token);
        var resetAll = await client.ChangeAccessAsync(Asked() with { Mode = "automatic", Reset = true }, Token);

        Assert.All(new[] { unexplained, row, uncapped, unending, local, reset, resetAll }, reply => Assert.Equal(AuditMethod.InvalidRequest, reply!.Method));
        Assert.All(new[] { reset, resetAll }, reply => Assert.StartsWith("the reset argument ", reply!.Reason, StringComparison.Ordinal));
        Assert.StartsWith("the reason argument ", unexplained!.Reason, StringComparison.Ordinal);
        Assert.StartsWith("the project argument ", row!.Reason, StringComparison.Ordinal);
        Assert.StartsWith("the per_hour argument ", uncapped!.Reason, StringComparison.Ordinal);
        Assert.StartsWith("the until argument ", unending!.Reason, StringComparison.Ordinal);
        Assert.StartsWith("the until argument ", local!.Reason, StringComparison.Ordinal);
        Assert.Equal(0, _fixture.Channel.AccessAsked);
        Assert.Empty(_fixture.Settings.CapsWritten);
    }

    /// <summary>
    /// An agent's request for more is put to the person as its own, with its reason; once refused, the
    /// next request for more is refused unasked for the cooldown, whatever it asks for, whichever agent
    /// it names and whether an agent or a terminal sends it, and says when to try again, while an agent
    /// can still narrow itself, only on this computer, and the person's own change in the app is asked.
    /// </summary>
    [Fact]
    public async Task ARefusedRequestForMore_HoldsBackTheNextFromAnyAgentOrTerminal_ButNotTheApp()
    {
        _fixture.Channel.AccessAnswer = ApprovalAnswer.Denied;
        await using var owner = Owner.Start(this);
        await using var agent = await owner.AttachAsync();
        await using var renamed = await owner.AttachAsync(label: "cursor");
        await using var terminal = await owner.AttachAsync(withClient: false);

        var refused = await agent.ChangeAccessAsync(Asked() with { Mode = "automatic" }, Token);

        Assert.Equal((AuditDecision.Denied, AuditMethod.Prompt), (refused!.Decision, refused.Method));
        Assert.Equal((AccessOrigin.Agent, "the deploy needs it"), (_fixture.Channel.LastAccessPrompt!.Origin, _fixture.Channel.LastAccessPrompt.Reason));

        var reworded = await agent.ChangeAccessAsync(Asked() with { Reason = "it is urgent", Project = "billing", Mode = "automatic" }, Token);
        var another = await renamed.ChangeAccessAsync(Asked("cursor") with { Mode = "automatic" }, Token);
        var typed = await terminal.ChangeAccessAsync(Change("codex") with { Run = "on" }, Token);
        var narrowed = await agent.ChangeAccessAsync(Asked() with { Values = "no" }, Token);

        Assert.All(new[] { reworded, another, typed }, reply => Assert.Equal((AuditDecision.Denied, AuditMethod.Cooldown), (reply!.Decision, reply.Method)));
        Assert.StartsWith(
            "a request for more access from an agent or a terminal was refused or went unanswered a moment ago, so this one was not put to the person; try again in 1 minute, or make it in the app's own agent settings, which do not wait",
            typed!.Reason,
            StringComparison.Ordinal);
        Assert.Equal((AuditDecision.Granted, AuditMethod.Narrowing), (narrowed!.Decision, narrowed.Method));
        Assert.Empty(_fixture.Settings.Written);
        Assert.False(Assert.Single(_fixture.Settings.Caps.Rows).Values);
        Assert.Equal(1, _fixture.Channel.AccessAsked);

        _fixture.Channel.AccessAnswer = ApprovalAnswer.ApprovedOnce;
        var person = await App().ChangeAccessAsync("codex", RunOn(), "keypaste app", Token);

        Assert.Equal((AuditDecision.Granted, AuditMethod.Prompt), (person.Decision, person.Method));
        Assert.Equal(AccessOrigin.Settings, _fixture.Channel.LastAccessPrompt.Origin);
        Assert.Equal(2, _fixture.Channel.AccessAsked);

        _fixture.Clock.Advance(TimeSpan.FromSeconds(30));
        var halfway = await terminal.ChangeAccessAsync(Change("codex") with { Mode = "automatic" }, Token);

        Assert.Contains("; try again in 30 seconds, or make it in", halfway!.Reason, StringComparison.Ordinal);

        _fixture.Clock.Advance(TimeSpan.FromSeconds(31));
        var later = await renamed.ChangeAccessAsync(Asked("cursor") with { Mode = "automatic" }, Token);

        Assert.Equal(AuditMethod.Prompt, later!.Method);
        Assert.Equal(3, _fixture.Channel.AccessAsked);
        Assert.Equal(["prompt", "cooldown", "cooldown", "cooldown", "narrowing", "prompt", "cooldown", "prompt"], Lines("method"));
    }

    /// <summary>
    /// An agent that is Automatic and asks for an end to it has that end applied at once on this
    /// computer, though nothing writes it to the vault, so past it the agent is asked again; and a later
    /// request of its own for less is still applied at once rather than put to the person as a request to
    /// lift the end it set.
    /// </summary>
    [Fact]
    public async Task AnAgentsOwnEndToAutomatic_HoldsOnThisComputer()
    {
        _fixture.Source.Tagged[_stripe] = ["env:billing"];
        _fixture.Settings.Records[_agent] = ApproverFixture.SeesAll with { Projects = [Row("billing:dev", AgentMode.Automatic)] };
        var end = _fixture.Clock.GetUtcNow().AddHours(1);
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        var limited = await client.ChangeAccessAsync(Asked() with { Project = "billing:dev", Mode = "automatic", Until = UntilTime.Recorded(end) }, Token);
        var before = await client.RequestAsync(Credential("STRIPE_SECRET_KEY"), Token);
        var narrowed = await client.ChangeAccessAsync(Asked() with { See = ["tag:env:billing"] }, Token);

        _fixture.Clock.Advance(TimeSpan.FromHours(1));
        var after = await client.RequestAsync(Credential("STRIPE_SECRET_KEY"), Token);

        Assert.Equal((AuditDecision.Granted, AuditMethod.Narrowing), (limited!.Decision, limited.Method));
        Assert.Empty(_fixture.Settings.Written);
        Assert.Equal(new AccessLimit { Until = end }, Assert.Single(_fixture.Settings.Caps.Rows, row => row.Project == Scope("billing:dev")).Limit);
        Assert.Equal(AuditMethod.Automatic, before!.Method);
        Assert.Equal((AuditDecision.Granted, AuditMethod.Narrowing), (narrowed!.Decision, narrowed.Method));
        Assert.Equal(AuditMethod.Prompt, after!.Method);
        Assert.Equal((0, 1), (_fixture.Channel.AccessAsked, _fixture.Channel.Asked));
    }

    /// <summary>
    /// A program gains by keeping a confirmation up until somebody presses yes, so one nobody answered,
    /// whether an agent or a terminal sent it, holds back the next request for more from any agent or
    /// terminal, under any name, while the person's own change in the app is still asked and starts nothing.
    /// </summary>
    [Fact]
    public async Task AnUnansweredRequestForMore_HoldsBackTheNextFromAnyAgentOrTerminal_ButNotTheApp()
    {
        _fixture.Channel.AccessAnswer = ApprovalAnswer.TimedOut;
        await using var owner = Owner.Start(this);
        await using var agent = await owner.AttachAsync();
        await using var renamed = await owner.AttachAsync(label: "cursor");
        await using var terminal = await owner.AttachAsync(withClient: false);
        var app = App();

        var unanswered = await agent.ChangeAccessAsync(Asked() with { Run = "on" }, Token);
        var another = await renamed.ChangeAccessAsync(Asked("cursor") with { Mode = "automatic" }, Token);
        var typed = await terminal.ChangeAccessAsync(Change("cursor") with { Mode = "automatic" }, Token);
        var person = await app.ChangeAccessAsync("cursor", RunOn(), "keypaste app", Token);

        Assert.Equal(
            (AuditMethod.TimedOut, AuditMethod.Cooldown, AuditMethod.Cooldown, AuditMethod.TimedOut),
            (unanswered!.Method, another!.Method, typed!.Method, person.Method));
        Assert.Equal(2, _fixture.Channel.AccessAsked);

        _fixture.Clock.Advance(ApprovalLimits.Default.DenialCooldown + TimeSpan.FromSeconds(1));
        var typedAgain = await terminal.ChangeAccessAsync(Change("cursor") with { Mode = "automatic" }, Token);
        var afterTerminal = await agent.ChangeAccessAsync(Asked() with { Mode = "automatic" }, Token);
        var personAgain = await app.ChangeAccessAsync("cursor", RunOn(), "keypaste app", Token);

        Assert.Equal(
            (AuditMethod.TimedOut, AuditMethod.Cooldown, AuditMethod.TimedOut),
            (typedAgain!.Method, afterTerminal!.Method, personAgain.Method));
        Assert.Equal(4, _fixture.Channel.AccessAsked);
        Assert.Empty(_fixture.Settings.Written);
    }

    /// <summary>The owner's own screen over the fixture's handler, as the app composes it beside its pipe.</summary>
    private SessionAuthority App() => new(
        VaultIdentity.Of(_directory, VaultPath),
        () => _lifetime,
        _fixture.Handler,
        () => _log.Log,
        clock: _fixture.Clock);

    private static AccessEdit RunOn()
    {
        Assert.True(AccessEdit.TryCreate(null, null, null, null, "on", null, null, false, out var runOn, out var error), error);
        return runOn;
    }

    /// <summary>Saves an agent's record into the vault as another program would leave it.</summary>
    private static void Hold(Vault vault, string record)
    {
        Assert.True(vault.WriteRecord(_record, AcafIds.Agent, AgentAccessStore.Field, record));
        vault.Save();
    }

    private static AcafRead Held(Vault vault) => AcafRecord.Read(vault.ReadField(_record, AgentAccessStore.Field)!);

    /// <summary>The fixture's handler over a real vault's settings, saving each change it makes, and this test's <c>clients.toml</c>.</summary>
    private ApproverHandler Saving(Vault vault) => new(
        _fixture.Source,
        _fixture.Source,
        _fixture.Gate,
        _fixture.Grants,
        new VaultAgentSettings(
            () => vault,
            edit =>
            {
                if (!edit(vault))
                {
                    return AgentSave.Unchanged;
                }

                vault.Save();
                return AgentSave.Saved;
            },
            new AccessCapsSource(ClientsPath)),
        hourly: _fixture.Hourly,
        clock: _fixture.Clock);

    /// <summary>Ceilings holding the most rows a file may, none of them this test's agent's.</summary>
    private static AccessCaps Full() =>
        Parsed(string.Concat(Enumerable.Range(0, AccessCaps.MaximumRows).Select(i => $"[[client]]\nlabel = \"other-{i}\"\nmode = \"ask\"\n")));

    private static AccessCaps Parsed(string text)
    {
        Assert.True(AccessCaps.TryParse(Encoding.UTF8.GetBytes(text), out var caps, out var problem), problem);
        return caps;
    }

    /// <summary>
    /// A change from the owner's own screen is put to the person as from there, recorded on its own line
    /// under that screen with the agent as its label, and refused unasked once the vault is locked.
    /// </summary>
    [Fact]
    public async Task AChangeFromTheOwnersOwnScreen_IsAskedRecordedAndRefusedOnceLocked()
    {
        _fixture.Channel.AccessAnswer = ApprovalAnswer.ApprovedOnce;
        var authority = new SessionAuthority(
            VaultIdentity.Of(_directory, VaultPath),
            () => _lifetime,
            _fixture.Handler,
            () => _log.Log,
            clock: _fixture.Clock);
        Assert.True(AccessEdit.TryCreate(null, null, null, null, "on", null, null, false, out var runOn, out var error), error);

        var changed = await authority.ChangeAccessAsync(_agent, runOn, "keypaste app", Token);

        Assert.Equal((AuditDecision.Granted, AuditMethod.Prompt, true), (changed.Decision, changed.Method, changed.Recorded));
        Assert.Equal(AccessOrigin.Settings, _fixture.Channel.LastAccessPrompt!.Origin);
        Assert.True(_fixture.Settings.Records[_agent].Run);
        Assert.True(authority.TrySettings(_agent, out var settings, out var problem), problem);
        Assert.True(settings.Vault.Run);

        _lifetime!.End();
        var locked = await authority.ChangeAccessAsync(_agent, runOn, "keypaste app", Token);

        Assert.Equal((AuditDecision.Denied, AuditMethod.VaultLocked), (locked.Decision, locked.Method));
        Assert.Equal(1, _fixture.Channel.AccessAsked);
        Assert.False(authority.TrySettings(_agent, out _, out _));
        Assert.Equal(["access_change", "access_change"], Lines("tool"));
        Assert.Equal(["granted", "denied"], Lines("decision"));
        Assert.All(_log.Lines(), line =>
        {
            Assert.Equal("keypaste app", line.GetProperty("client").GetProperty("name").GetString());
            Assert.Equal(_agent, line.GetProperty("client").GetProperty("label").GetString());
        });
    }

    /// <summary>
    /// Resetting ceilings keypaste cannot parse waits for the person, who is shown what each agent's
    /// settings in the vault then give beyond the defaults, and is recorded under every agent; ceilings it
    /// could not read at all are left as they are, and ones it can read have nothing to reset.
    /// </summary>
    [Fact]
    public async Task AResetOfCeilingsKeypasteCannotParse_ShowsWhatEachAgentThenGets_AndWaitsForThePerson()
    {
        _fixture.Settings.Records[_agent] = ApproverFixture.SeesAll with { Mode = AgentMode.Automatic };
        _fixture.Settings.CapsFile = CapsFile.Malformed;
        _fixture.Channel.AccessAnswer = ApprovalAnswer.Denied;
        var authority = new SessionAuthority(
            VaultIdentity.Of(_directory, VaultPath),
            () => _lifetime,
            _fixture.Handler,
            () => _log.Log,
            clock: _fixture.Clock);

        var refused = await authority.ResetCapsAsync("keypaste app", Token);

        Assert.Equal((AuditDecision.Denied, AuditMethod.Prompt), (refused.Decision, refused.Method));
        var asked = _fixture.Channel.LastAccessPrompt!;
        Assert.True(asked.ResetsCeilings);
        Assert.Contains("claude-code: sees **", asked.Gains);
        Assert.Contains(asked.Gains, gain => gain.StartsWith("claude-code: by default: Automatic", StringComparison.Ordinal));
        Assert.Equal(0, _fixture.Settings.Resets);

        _fixture.Channel.AccessAnswer = ApprovalAnswer.ApprovedOnce;
        var reset = await authority.ResetCapsAsync("keypaste app", Token);
        var nothing = await authority.ResetCapsAsync("keypaste app", Token);

        Assert.Equal((AuditDecision.Granted, AuditMethod.Prompt), (reset.Decision, reset.Method));
        Assert.Equal("clients.toml" + AccessCaps.KeptSuffix, reset.Kept);
        Assert.Contains($"was kept beside it as clients.toml{AccessCaps.KeptSuffix};", reset.Reason, StringComparison.Ordinal);
        Assert.Equal((AuditDecision.Denied, AuditMethod.InvalidRequest), (nothing.Decision, nothing.Method));
        Assert.Equal(1, _fixture.Settings.Resets);

        _fixture.Settings.CapsFile = CapsFile.Unreadable;
        var unread = await authority.ResetCapsAsync("keypaste app", Token);

        Assert.Equal((AuditDecision.Denied, AuditMethod.Failed), (unread.Decision, unread.Method));
        Assert.Equal((2, 1), (_fixture.Channel.AccessAsked, _fixture.Settings.Resets));
        Assert.Equal(["denied", "granted", "denied", "denied"], Lines("decision"));
        Assert.All(_log.Lines(), line => Assert.Equal(AgentNames.Any, line.GetProperty("client").GetProperty("label").GetString()));
    }

    /// <summary>An owner over the fixture's handler on its own pipe, recording in the test's log unless told otherwise.</summary>
    private sealed class Owner : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly ApproverListener _listener;
        private readonly Task _running;
        private readonly string _vault;

        private Owner(string pipeName, SessionAuthority authority, string vault)
        {
            PipeName = pipeName;
            Authority = authority;
            _vault = vault;
            _listener = new ApproverListener(pipeName, authority);
            _running = _listener.RunAsync(_stop.Token);
        }

        internal string PipeName { get; }

        internal SessionAuthority Authority { get; }

        internal static Owner Start(SessionAuthorityAccessTests test, Func<AuditLog?>? audit = null, ApproverHandler? handler = null) =>
            new(
                "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)),
                new SessionAuthority(
                    VaultIdentity.Of(test._directory, test.VaultPath),
                    () => test._lifetime,
                    handler ?? test._fixture.Handler,
                    audit ?? (() => test._log.Log),
                    clock: test._fixture.Clock),
                test.VaultPath);

        internal async Task<ApproverClient> AttachAsync(bool withClient = true, string label = _agent)
        {
            var client = await ApproverClient.TryConnectAsync(PipeName, _wait, Token);
            Assert.NotNull(client);
            var attach = new AttachRequest(_vault) { Client = withClient ? new AttachClient("claude-code", "2.1.0", label) : null };
            Assert.True((await client.AttachAsync(attach, Token))?.Attached);
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();

            try
            {
                await _running;
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
                // Tearing the listener down is how it stops.
            }

            _listener.Dispose();
            _stop.Dispose();
        }
    }
}
