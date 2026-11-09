using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// F.78b's acceptance on the request path: a real vault, the owner's own authority and log, and only the person faked.
/// The most specific answer decides, an item's field, then the agent's category, then Settings; an always holds only once
/// confirmed, Remove asks again, a remembered pick answers that agent alone for that name, and a lock releases nothing.
/// </summary>
public sealed class RememberedAnswerRequestTests : IDisposable
{
    private const string _agent = "claude-code";
    private const string _other = "cursor";
    private const string _password = "SENTINEL-REMEMBERED-PASSWORD";
    private static readonly EntryName _github = new("work", "github");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-remembered-").FullName;
    private readonly ManualClock _clock = new();
    private readonly FakeChannel _channel = new();
    private readonly FakeAgentRules _rules = new();
    private readonly ApprovalGate _gate;
    private readonly Vault _vault;
    private readonly OwnerLog _log;
    private readonly SessionLifetime _lifetime = new("session-one");
    private readonly SessionAuthority _authority;

    public RememberedAnswerRequestTests()
    {
        using (var created = Vault.Create(VaultPath, EnvStoreTests.MasterPassword))
        {
            created.AddEntry(new VaultEntry { GroupPath = _github.GroupPath, Title = _github.Title, Username = "octocat", Password = _password });
            ProjectVariables.Set(created, "acme-api", "DATABASE_URL", "SENTINEL-REMEMBERED-DB");

            foreach (var account in new[] { "alice", "bob" })
            {
                created.AddEntry(new VaultEntry { GroupPath = "services", Title = "Mail", Username = account, Password = $"SENTINEL-{account}" });
            }

            created.Save();
        }

        _vault = Vault.Open(VaultPath, EnvStoreTests.MasterPassword);
        _log = new OwnerLog(_directory);
        _gate = new ApprovalGate(_channel, _clock, ApprovalLimits.Default);
        _authority = new SessionAuthority(
            VaultIdentity.Of(_directory, VaultPath),
            () => _lifetime,
            new ApproverHandler(new VaultCredentialSource(() => _lifetime.IsLive ? _vault : null, _clock), new VaultEntryNameLister(() => _vault), _gate, _rules),
            () => _log.Log,
            new SessionEnvironments(_gate, lifetime => ReferenceEquals(lifetime, _lifetime) && lifetime.IsLive ? _vault : null, _clock),
            clock: _clock);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    private string GithubId => _vault.EntryUuid(_github)!;

    public void Dispose()
    {
        _gate.Dispose();
        _lifetime.Dispose();
        _vault.Dispose();
        _log.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task An_items_answer_decides_before_the_agents_rule_which_decides_before_Settings()
    {
        var item = new RememberedAnswer(_agent, GithubId, "password", CategoryRule.Allow);
        _rules.Rules = AgentRules.Defaults
            .Allowing(FieldCategory.Password, confirmed: true)
            .With(_agent, FieldCategory.Password, CategoryRule.Block)
            .Remembering(item, confirmed: true);

        var byItem = await RequestAsync("work/github", "password");
        _rules.Rules = _rules.Rules.Forgetting(item);
        var byAgent = await RequestAsync("work/github", "password");
        _rules.Rules = _rules.Rules.With(_agent, FieldCategory.Password, null);
        var bySettings = await RequestAsync("work/github", "password");

        Assert.Equal((AuditDecision.Granted, AuditMethod.Allowed, _password), (byItem.Decision, byItem.Method, byItem.Value));
        Assert.Equal("released without asking: the person always allows this item's password to this agent", byItem.Reason);
        Assert.Equal((AuditDecision.Denied, AuditMethod.Blocked, null), (byAgent.Decision, byAgent.Method, byAgent.Value));
        Assert.Equal("the person blocks passwords for this agent", byAgent.Reason);
        Assert.Equal((AuditDecision.Granted, AuditMethod.Allowed), (bySettings.Decision, bySettings.Method));
        Assert.Equal("released without asking: the person allows passwords for every agent", bySettings.Reason);
        Assert.Equal(0, _channel.Asked);
        Assert.Equal([("granted", "allowed"), ("denied", "blocked"), ("granted", "allowed")], Lines());
    }

    [Fact]
    public async Task An_agents_rule_and_its_items_answers_decide_nothing_for_another_agent()
    {
        _rules.Rules = AgentRules.Defaults
            .Allowing(_agent, FieldCategory.ProjectVariable, confirmed: true)
            .Remembering(new RememberedAnswer(_agent, GithubId, "password", CategoryRule.Allow), confirmed: true);
        _channel.Answer = ApprovalAnswer.Denied;

        var password = await RequestAsync("work/github", "password", _other);
        _clock.Advance(TimeSpan.FromMinutes(2));
        var variable = await RequestAsync("env/acme-api/.env", "DATABASE_URL", _other);

        Assert.Equal([AuditMethod.Prompt, AuditMethod.Prompt], [password.Method, variable.Method]);
        Assert.Equal(2, _channel.Asked);
    }

    [Fact]
    public async Task A_bridge_with_no_label_is_decided_by_Settings_and_offered_no_always()
    {
        _rules.Rules = AgentRules.Defaults.Remembering(new RememberedAnswer(_agent, GithubId, "password", CategoryRule.Allow), confirmed: true);
        _channel.Answer = ApprovalAnswer.AlwaysAllowed;

        var reply = await RequestAsync("work/github", "password", label: null);

        Assert.False(_channel.LastPrompt!.OffersAlways);
        Assert.Equal((AuditDecision.Denied, null), (reply.Decision, reply.Value));
        Assert.Single(_rules.Rules.Answers);
    }

    [Fact]
    public async Task Always_allow_confirmed_on_the_prompt_releases_now_and_then_without_asking()
    {
        _channel.Answer = ApprovalAnswer.AlwaysAllowed;

        var first = await RequestAsync("work/github", "password");
        var second = await RequestAsync("work/github", "password");

        Assert.True(_channel.LastPrompt!.OffersAlways);
        Assert.Equal((AuditDecision.Granted, AuditMethod.Prompt, _password), (first.Decision, first.Method, first.Value));
        Assert.Equal("a person approved this request and always allows this item's field to this agent", first.Reason);
        Assert.Equal((AuditDecision.Granted, AuditMethod.Allowed, _password), (second.Decision, second.Method, second.Value));
        Assert.Equal(1, _channel.Asked);
        Assert.Equal(new RememberedAnswer(_agent, GithubId.ToLowerInvariant(), "password", CategoryRule.Allow), Assert.Single(_rules.Rules.Answers));
    }

    [Fact]
    public async Task Always_block_confirmed_on_the_prompt_refuses_now_and_then_without_asking()
    {
        _channel.Answer = ApprovalAnswer.AlwaysBlocked;

        var first = await RequestAsync("work/github", "password");
        _clock.Advance(TimeSpan.FromMinutes(10));
        var second = await RequestAsync("work/github", "password");

        Assert.Equal((AuditDecision.Denied, AuditMethod.Prompt, null), (first.Decision, first.Method, first.Value));
        Assert.Equal((AuditDecision.Denied, AuditMethod.Blocked, null), (second.Decision, second.Method, second.Value));
        Assert.Equal("the person always refuses this item's password to this agent", second.Reason);
        Assert.Equal(1, _channel.Asked);
    }

    [Fact]
    public void Nothing_is_kept_without_the_persons_confirmation()
    {
        var answer = new RememberedAnswer(_agent, GithubId, "password", CategoryRule.Allow);
        var pick = new RememberedPick(_agent, "services/Mail", "password", GithubId);

        Assert.Equal(AgentRules.Defaults, AgentRules.Defaults.Remembering(answer, confirmed: false));
        Assert.Equal(AgentRules.Defaults, AgentRules.Defaults.Remembering(pick, confirmed: false));
        Assert.Equal(AgentRules.Defaults, AgentRules.Defaults.Allowing(_agent, FieldCategory.Password, confirmed: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => AgentRules.Defaults.With(_agent, FieldCategory.Password, CategoryRule.Allow));
    }

    [Fact]
    public async Task An_always_answer_on_a_prompt_that_did_not_offer_it_is_a_refusal()
    {
        _rules.Rules = Full();
        _channel.Answer = ApprovalAnswer.AlwaysAllowed;

        var reply = await RequestAsync("work/github", "password");

        Assert.False(_channel.LastPrompt!.OffersAlways);
        Assert.Equal((AuditDecision.Denied, AuditMethod.Prompt, null), (reply.Decision, reply.Method, reply.Value));
        Assert.Equal(AgentRules.MaximumRemembered, _rules.Rules.Answers.Count);
    }

    [Fact]
    public async Task Remove_asks_again()
    {
        _channel.Answer = ApprovalAnswer.AlwaysAllowed;
        await RequestAsync("work/github", "password");
        _rules.Rules = _rules.Rules.Forgetting(Assert.Single(_rules.Rules.Answers));
        _channel.Answer = ApprovalAnswer.Denied;

        var reply = await RequestAsync("work/github", "password");

        Assert.Equal((AuditDecision.Denied, AuditMethod.Prompt), (reply.Decision, reply.Method));
        Assert.Equal(2, _channel.Asked);
    }

    [Fact]
    public async Task A_passkey_stays_Blocked_under_any_remembered_answer()
    {
        _rules.Rules = AgentRules.Defaults
            .Remembering(new RememberedAnswer(_agent, GithubId, "KPEX_PASSKEY_PRIVATE_KEY_PEM", CategoryRule.Allow), confirmed: true);

        var reply = await RequestAsync("work/github", "KPEX_PASSKEY_PRIVATE_KEY_PEM");

        Assert.Equal((AuditDecision.Denied, AuditMethod.Blocked, null), (reply.Decision, reply.Method, reply.Value));
        Assert.Equal(0, _channel.Asked);
    }

    [Fact]
    public async Task The_pickers_Always_allow_answers_that_agent_alone_for_that_name_with_the_picked_item()
    {
        _channel.Answer = ApprovalAnswer.AlwaysAllowed;
        _channel.Pick = prompt => prompt.Candidates.ToList().FindIndex(candidate => candidate.Username == "bob");

        var picked = await RequestAsync("services/Mail", "password");
        var again = await RequestAsync("services/Mail", "password");
        _channel.Answer = ApprovalAnswer.Denied;
        var other = await RequestAsync("services/Mail", "password", _other);

        Assert.Equal(("SENTINEL-bob", "SENTINEL-bob"), (picked.Value, again.Value));
        Assert.Equal((AuditMethod.Prompt, AuditMethod.Allowed), (picked.Method, again.Method));
        Assert.Equal("released without asking: the person always picks this item for that name for this agent", again.Reason);
        Assert.Equal((AuditDecision.Denied, null), (other.Decision, other.Value));
        Assert.Equal(2, _channel.PickersAsked);
        Assert.Equal("services/Mail", Assert.Single(_rules.Rules.Picks).Name);
    }

    [Fact]
    public async Task A_remembered_pick_is_not_followed_to_an_item_the_person_always_blocks()
    {
        var bob = Uuid("bob");
        _rules.Rules = AgentRules.Defaults
            .Remembering(new RememberedPick(_agent, "services/Mail", "password", bob), confirmed: true)
            .Remembering(new RememberedAnswer(_agent, bob, "password", CategoryRule.Block), confirmed: true);
        _channel.Answer = ApprovalAnswer.ApprovedOnce;

        var reply = await RequestAsync("services/Mail", "password");

        Assert.Equal(("SENTINEL-alice", AuditMethod.Prompt), (reply.Value, reply.Method));
    }

    [Fact]
    public async Task A_lock_releases_nothing_under_a_remembered_answer_and_keeps_no_always_it_withdrew()
    {
        _channel.Hold = true;
        _channel.Answer = ApprovalAnswer.AlwaysAllowed;

        var asking = RequestAsync("work/github", "password");
        await _channel.Waiting.WaitAsync(TimeSpan.FromSeconds(10), Token);
        _lifetime.End();
        var withdrawn = await asking.AsTask().WaitAsync(TimeSpan.FromSeconds(10), Token);

        Assert.Equal((AuditDecision.Denied, null), (withdrawn.Decision, withdrawn.Value));
        Assert.Empty(_rules.Rules.Answers);

        _rules.Rules = AgentRules.Defaults.Remembering(new RememberedAnswer(_agent, GithubId, "password", CategoryRule.Allow), confirmed: true);
        var locked = await RequestAsync("work/github", "password", attach: false);

        Assert.Equal((AuditDecision.Denied, null), (locked.Decision, locked.Value));
        Assert.Empty(_authority.Released);
    }

    [Fact]
    public async Task A_run_is_refused_a_variable_the_agent_blocks()
    {
        _rules.Rules = AgentRules.Defaults.WithRun(_agent, run: true).With(_agent, FieldCategory.ProjectVariable, CategoryRule.Block);
        _channel.Answer = ApprovalAnswer.ApprovedOnce;

        var reply = await RunAsync();

        Assert.Equal(AuditMethod.Blocked, reply.Method);
        Assert.Contains("the person blocks project variables for this agent", reply.Reason, StringComparison.Ordinal);
        Assert.Null(_channel.LastRunPrompt);
    }

    /// <summary>A terminal proves no person agreed, so it may change an agent's rules only to what releases nothing more unasked.</summary>
    [Fact]
    public void A_terminal_narrows_an_agents_rules_and_never_widens_them()
    {
        var allowing = AgentRules.Defaults.Allowing(FieldCategory.Password, confirmed: true).With(_agent, FieldCategory.Password, CategoryRule.Block);

        Assert.Equal(CategoryRule.Ask, AgentRuleTarget.Apply(allowing, _agent, "password=ask").RuleFor(_agent, FieldCategory.Password));
        Assert.Throws<ArgumentException>(() => AgentRuleTarget.Apply(allowing, _agent, "password=settings"));
        Assert.Throws<ArgumentException>(() => AgentRuleTarget.Apply(allowing, _agent, "password=allow"));
        Assert.Throws<ArgumentException>(() => AgentRuleTarget.Apply(allowing, _agent, "passkey=block"));
        Assert.Null(AgentRuleTarget.Apply(AgentRules.Defaults.With(_agent, FieldCategory.Notes, CategoryRule.Block), _agent, "notes=settings").RuleFor(_agent, FieldCategory.Notes));
    }

    [Fact]
    public void A_terminal_forgets_an_Always_allow_or_a_pick_by_its_key_and_never_an_Always_block()
    {
        var allow = new RememberedAnswer(_agent, GithubId, "password", CategoryRule.Allow);
        var block = new RememberedAnswer(_agent, GithubId, "notes", CategoryRule.Block);
        var pick = new RememberedPick(_agent, "services/Mail", "password", Uuid("bob"));
        var rules = AgentRules.Defaults.Remembering(allow, confirmed: true).Remembering(block, confirmed: true).Remembering(pick, confirmed: true);

        var forgotten = rules.ForgettingKey(_agent, AgentRules.KeyOf(allow)).ForgettingKey(_agent, AgentRules.KeyOf(pick));

        Assert.Equal(CategoryRule.Block, Assert.Single(forgotten.Answers).Rule);
        Assert.Empty(forgotten.Picks);
        Assert.Throws<ArgumentException>(() => rules.ForgettingKey(_agent, AgentRules.KeyOf(block)));
        Assert.Throws<ArgumentException>(() => rules.ForgettingKey(_other, AgentRules.KeyOf(allow)));
        Assert.Equal(AgentRules.KeyOf(allow), AgentRules.KeyOf(allow with { Item = GithubId.ToUpperInvariant() }));
    }

    /// <summary>Rules with no room left to remember another answer.</summary>
    private static AgentRules Full()
    {
        var rules = AgentRules.Defaults;

        for (var i = 0; i < AgentRules.MaximumRemembered; i++)
        {
            rules = rules.Remembering(new RememberedAnswer(_agent, $"{i:x32}", "password", CategoryRule.Block), confirmed: true);
        }

        return rules;
    }

    private string Uuid(string username) => _vault.ReadEntries().Single(entry => entry.Username == username).Uuid!;

    private async ValueTask<CredentialReply> RequestAsync(string entry, string field, string? label = _agent, bool attach = true)
    {
        var connection = $"conn-{Guid.NewGuid():N}";
        var session = "session-one";

        if (attach)
        {
            var attached = await _authority.AttachAsync(new AttachRequest(VaultPath) { Client = new AttachClient("Claude Code", "2.1.0", label) }, connection, Token);
            Assert.True(attached.Attached, attached.Reason);
            session = attached.Session!;
        }

        return await _authority.RequestAsync(
            new CredentialRequest
            {
                Entry = entry,
                Field = field,
                Reason = "deploy the app",
                Exposure = ["**"],
                ClientName = "Claude Code",
                ClientLabel = label,
                Vault = VaultPath,
                Session = session,
            },
            connection,
            Token);
    }

    private async ValueTask<RunReply> RunAsync()
    {
        var connection = $"conn-{Guid.NewGuid():N}";
        var attached = await _authority.AttachAsync(new AttachRequest(VaultPath) { Client = new AttachClient("Claude Code", "2.1.0", _agent) }, connection, Token);

        return await _authority.ReleaseRunAsync(
            new RunRequest
            {
                Program = OperatingSystem.IsWindows() ? @"C:\tools\npm.exe" : "/usr/bin/npm",
                Command = ["npm", "run", "migrate"],
                Directory = _directory,
                Project = "acme-api",
                Reason = "run the pending migration",
                Exposure = ["**"],
                ClientName = "Claude Code",
                ClientLabel = _agent,
                Vault = VaultPath,
                Session = attached.Session!,
            },
            connection,
            Token);
    }

    private List<(string Decision, string Method)> Lines() =>
        [.. _log.Lines().Select(line => (line.GetProperty("decision").GetString()!, line.GetProperty("method").GetString()!))];
}
