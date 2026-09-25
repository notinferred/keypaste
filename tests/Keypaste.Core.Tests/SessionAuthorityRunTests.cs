using System.Security.Cryptography;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Clients;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Keypaste.Core.Policy;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// An agent's run over a real pipe (D-0358): the owner releases the variables only to a connection of
/// its current session, only within the bridge's exposure and the client's policy, after the person
/// approves the exact program, command, directory and names, or under a timed grant that same
/// connection holds for that same command line.
/// </summary>
public sealed class SessionAuthorityRunTests : IDisposable
{
    private const string _database = "postgres://app:SENTINEL-RUN-DB@db/app";
    private const string _stripe = "sk_live_SENTINEL_RUN_STRIPE";
    private const string _prod = "SENTINEL-RUN-PROD";
    private const string _github = "SENTINEL-RUN-GITHUB";
    private const string _label = "claude-code";
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-authority-run-").FullName;
    private readonly ApproverFixture _fixture = new();
    private readonly Vault _vault;
    private readonly EnvGrantCache _grants;
    private readonly List<string> _narration = [];
    private SessionLifetime? _lifetime = new("session-one");

    public SessionAuthorityRunTests()
    {
        using (var created = Vault.Create(VaultPath, EnvStoreTests.MasterPassword))
        {
            var store = new EnvStore(created);
            store.TrySet("acme-api", "DATABASE_URL", _database, out _);
            store.TrySet("acme-api", "STRIPE_SECRET_KEY", _stripe, out _);
            created.AddEntry(new VaultEntry { GroupPath = "env/acme-api/prod", Title = "DATABASE_URL", Password = _prod });
            created.AddEntry(new VaultEntry { GroupPath = "env/hijack", Title = "PATH", Password = "/tmp/evil" });
            created.AddEntry(new VaultEntry { GroupPath = "env/billing", Title = "TOKEN", Password = "billing-token" });
            created.AddEntry(new VaultEntry { GroupPath = "personal", Title = "github", Username = _github, Password = "gh-password" });
            created.AddEntry(new VaultEntry { GroupPath = ".keypaste/tokens", Title = "t1", Password = "verifier" });

            foreach (var i in Enumerable.Range(0, 33))
            {
                created.AddEntry(new VaultEntry { GroupPath = "env/big", Title = $"KEY_{i}", Password = $"v{i}" });
                created.AddEntry(new VaultEntry { GroupPath = "env/outside-big", Title = $"KEY_{i}", Password = $"v{i}" });
            }

            foreach (var i in Enumerable.Range(0, 30))
            {
                created.AddEntry(new VaultEntry { GroupPath = "env/wide", Title = $"KEY_{i}_" + new string('W', 70), Password = $"w{i}" });
            }

            created.Save();
        }

        _vault = Vault.Open(VaultPath, EnvStoreTests.MasterPassword);
        _grants = new EnvGrantCache(_fixture.Clock);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    private string ClientsPath => Path.Combine(_directory, "clients.toml");

    private static string Program => OperatingSystem.IsWindows() ? @"C:\tools\npm.exe" : "/usr/bin/npm";

    public void Dispose()
    {
        _lifetime?.Dispose();
        _grants.Dispose();
        _vault.Dispose();
        _fixture.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private RunRequest Run(string session = "session-one", string project = "acme-api", IReadOnlyList<string>? command = null) => new()
    {
        Program = Program,
        Command = command ?? ["npm", "run", "migrate"],
        Directory = _directory,
        Project = project,
        Reason = "run the pending migration",
        Exposure = ["env/acme-api/**", "env/big/**", "env/hijack/**", "env/wide/**", "personal/**", ".keypaste/**"],
        ClientName = "claude-code-cli",
        ClientVersion = "1.0",
        ClientLabel = _label,
        Vault = VaultPath,
        Session = session,
    };

    private RunRequest References(params (string Name, string Reference)[] pairs) =>
        Run() with { Project = null, References = [.. pairs.Select(pair => new RunReference(pair.Name, pair.Reference))] };

    private async Task<ApproverClient> AttachedAsync(Owner owner)
    {
        var client = await ApproverClient.TryConnectAsync(owner.PipeName, _wait, Token);
        Assert.NotNull(client);
        Assert.True((await client.AttachAsync(new AttachRequest(VaultPath) { Client = new AttachClient("claude-code-cli", "1.0", _label) }, Token))?.Attached);
        return client;
    }

    private static void AssertNothingReleased(RunReply? reply)
    {
        Assert.NotNull(reply);
        Assert.NotEqual(EnvOutcome.Resolved, reply.Set.Outcome);
        Assert.Empty(reply.Set.Variables);
    }

    [Fact]
    public async Task AnApprovedRun_ReleasesTheSet_AndNamesItsEntries()
    {
        _fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var reply = await client.ReleaseRunAsync(Run(), Token);

        Assert.NotNull(reply);
        Assert.Equal(EnvOutcome.Resolved, reply.Set.Outcome);
        Assert.Equal([new EnvVariable("DATABASE_URL", _database), new EnvVariable("STRIPE_SECRET_KEY", _stripe)], reply.Set.Variables);
        Assert.Equal(["env/acme-api/DATABASE_URL", "env/acme-api/STRIPE_SECRET_KEY"], reply.Entries);
        Assert.Equal(AuditMethod.Prompt, reply.Method);
        Assert.Equal(0, reply.GrantedSeconds);
        Assert.Equal("session-one", reply.Session);
    }

    [Fact]
    public async Task ThePrompt_CarriesClientToolProgramCommandDirectoryVariablesAndReason()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Denied;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        await client.ReleaseRunAsync(Run(command: ["npm", "run", "migrate --to staging"]), Token);

        var prompt = Assert.IsType<RunPrompt>(_fixture.Channel.LastRunPrompt);
        Assert.Equal("claude-code-cli", prompt.Client);
        Assert.Equal(_label, prompt.Label);
        Assert.Equal(Program, prompt.Program);
        Assert.Equal("npm run \"migrate --to staging\"", prompt.Command);
        Assert.Equal(_directory, prompt.Directory);
        Assert.Equal("acme-api", prompt.Project);
        Assert.Equal("dev", prompt.Profile);
        Assert.Equal(
            [new RunPromptVariable("DATABASE_URL", "env/acme-api/DATABASE_URL", "password"), new RunPromptVariable("STRIPE_SECRET_KEY", "env/acme-api/STRIPE_SECRET_KEY", "password")],
            prompt.Variables);
        Assert.Equal("run the pending migration", prompt.Reason);
        Assert.Equal(EnvGrantCache.CeilingSeconds, prompt.GrantSeconds);
        Assert.DoesNotContain(_database, prompt.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllowForTheWindow_ServesTheSameRunOnTheSameConnection_Unasked_WithTheLatestValues()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var first = await client.ReleaseRunAsync(Run(), Token);
        Assert.Equal(EnvGrantCache.CeilingSeconds, first!.GrantedSeconds);

        new EnvStore(_vault).TrySet("acme-api", "DATABASE_URL", "postgres://rotated", out _);
        _vault.Save();
        _fixture.Channel.Answer = ApprovalAnswer.Denied;

        var second = await client.ReleaseRunAsync(Run(), Token);

        Assert.NotNull(second);
        Assert.Equal(EnvOutcome.Resolved, second.Set.Outcome);
        Assert.Equal(AuditMethod.GrantCache, second.Method);
        Assert.Equal("postgres://rotated", second.Set.Variables[0].Value);
        Assert.Equal(1, _fixture.Channel.Asked);
        Assert.Contains(_narration, line => line.Contains("from a timed grant", StringComparison.Ordinal) && line.Contains("npm run migrate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheRunGrant_DoesNotCoverAnotherConnectionCommandDirectoryOrNameList()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);
        Assert.Equal(EnvOutcome.Resolved, (await client.ReleaseRunAsync(Run(), Token))!.Set.Outcome);

        _fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;
        await using var other = await AttachedAsync(owner);

        await other.ReleaseRunAsync(Run(), Token);
        await client.ReleaseRunAsync(Run(command: ["npm", "run", "seed"]), Token);
        await client.ReleaseRunAsync(Run() with { Directory = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar) }, Token);
        await client.ReleaseRunAsync(Run() with { Keys = ["DATABASE_URL"] }, Token);

        Assert.Equal(5, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task Disconnecting_EndsThatConnectionsRunGrants()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        var client = await AttachedAsync(owner);
        await client.ReleaseRunAsync(Run(), Token);
        Assert.Single(owner.Authority.Activity.EnvGrants);

        await client.DisposeAsync();

        var deadline = DateTime.UtcNow + _wait;
        while (owner.Authority.Activity.EnvGrants.Count > 0)
        {
            Assert.True(DateTime.UtcNow < deadline, "the grant outlived its connection");
            await Task.Delay(20, Token);
        }
    }

    [Fact]
    public async Task AllowOnce_StoresNothing()
    {
        _fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        await client.ReleaseRunAsync(Run(), Token);
        await client.ReleaseRunAsync(Run(), Token);

        Assert.Empty(owner.Authority.Activity.EnvGrants);
        Assert.Equal(2, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task AProtectedProfile_IsOnceOnly_AndNeverServedFromAGrant()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var first = await client.ReleaseRunAsync(Run() with { Profile = "prod" }, Token);
        var second = await client.ReleaseRunAsync(Run() with { Profile = "prod" }, Token);

        Assert.Equal(_prod, first!.Set.Variables.Single().Value);
        Assert.Equal(EnvOutcome.Resolved, second!.Set.Outcome);
        Assert.Equal(2, _fixture.Channel.Asked);
        Assert.Equal(0, _fixture.Channel.LastRunPrompt!.GrantSeconds);
        Assert.Equal(OnceOnly.ProtectedProfile, _fixture.Channel.LastRunPrompt.OnceOnly);
        Assert.Empty(owner.Authority.Activity.EnvGrants);
    }

    [Fact]
    public async Task AReferenceIntoAProtectedProfile_IsOnceOnly()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var reply = await client.ReleaseRunAsync(References(("DB", "kp://acme-api/prod/DATABASE_URL"), ("GH", "kp:///personal/github#username")), Token);

        Assert.Equal([new EnvVariable("DB", _prod), new EnvVariable("GH", _github)], reply!.Set.Variables);
        Assert.Equal(OnceOnly.ProtectedProfile, _fixture.Channel.LastRunPrompt!.OnceOnly);
        Assert.Equal(["env/acme-api/prod/DATABASE_URL", "personal/github"], reply.Entries);
        Assert.Empty(owner.Authority.Activity.EnvGrants);
    }

    [Fact]
    public async Task AnEntryOutsideTheExposure_IsRefusedUnasked()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var narrow = Run() with { Exposure = ["env/acme-api/DATABASE_URL"] };
        var replies = new[]
        {
            await client.ReleaseRunAsync(Run(project: "billing"), Token),
            await client.ReleaseRunAsync(narrow, Token),
            await client.ReleaseRunAsync(References(("T", "kp://billing/dev/TOKEN")), Token),
        };

        foreach (var reply in replies)
        {
            AssertNothingReleased(reply);
            Assert.Equal(AuditMethod.OutOfScope, reply!.Method);
        }

        Assert.Equal(0, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task AMissingProjectAndAnOutOfScopeOne_LookTheSameToTheAgent()
    {
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var missing = await client.ReleaseRunAsync(Run(project: "no-such-project"), Token);
        var outside = await client.ReleaseRunAsync(Run(project: "billing"), Token);

        Assert.Equal(AuditMethod.OutOfScope, missing!.Method);
        Assert.Equal(AuditMethod.OutOfScope, outside!.Method);
        Assert.Equal(0, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task AReservedReference_IsRefused()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var reply = await client.ReleaseRunAsync(References(("T", "kp:///.keypaste/tokens/t1")), Token);

        AssertNothingReleased(reply);
        Assert.Equal(AuditMethod.OutOfScope, reply!.Method);
        Assert.Equal(0, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task MoreThan32Variables_IsRefusedUnasked_ButOutsideTheExposureIsOutOfScopeFirst()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var big = await client.ReleaseRunAsync(Run(project: "big"), Token);
        var outside = await client.ReleaseRunAsync(Run(project: "outside-big"), Token);

        Assert.Equal(AuditMethod.InvalidRequest, big!.Method);
        Assert.Equal(AuditMethod.OutOfScope, outside!.Method);
        Assert.Equal(0, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task AWholeSetNamingPath_IsRefusedUnasked()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var reply = await client.ReleaseRunAsync(Run(project: "hijack"), Token);

        AssertNothingReleased(reply);
        Assert.Equal(AuditMethod.InvalidRequest, reply!.Method);
        Assert.Equal(0, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task EntriesTooLongForOneAuditLine_AreRefusedUnasked()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var reply = await client.ReleaseRunAsync(Run(project: "wide"), Token);

        AssertNothingReleased(reply);
        Assert.Equal(AuditMethod.InvalidRequest, reply!.Method);
        Assert.Equal(0, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task AskEveryTime_OffersNoTimedChoice_AndUsesNoGrant()
    {
        File.WriteAllText(ClientsPath, "[[client]]\nlabel = \"claude-code\"\npolicy = \"ask\"\n");
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this, clients: true);
        await using var client = await AttachedAsync(owner);

        await client.ReleaseRunAsync(Run(), Token);
        await client.ReleaseRunAsync(Run(), Token);

        Assert.Equal(2, _fixture.Channel.Asked);
        Assert.Equal(0, _fixture.Channel.LastRunPrompt!.GrantSeconds);
        Assert.Equal(OnceOnly.ClientPolicy, _fixture.Channel.LastRunPrompt.OnceOnly);
        Assert.Empty(owner.Authority.Activity.EnvGrants);
    }

    [Fact]
    public async Task InjectOnly_StillRuns()
    {
        File.WriteAllText(ClientsPath, "[[client]]\nlabel = \"*\"\npolicy = \"inject-only\"\n");
        _fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;
        await using var owner = Owner.Start(this, clients: true);
        await using var client = await AttachedAsync(owner);

        var reply = await client.ReleaseRunAsync(Run(), Token);

        Assert.Equal(EnvOutcome.Resolved, reply!.Set.Outcome);
    }

    [Fact]
    public async Task AnUnreadableClientsFile_RefusesUnasked()
    {
        File.WriteAllText(ClientsPath, "[[client]]\nlabel = \"claude-code\"\npolicy = \"sometimes\"\n");
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this, clients: true);
        await using var client = await AttachedAsync(owner);

        var reply = await client.ReleaseRunAsync(Run(), Token);

        AssertNothingReleased(reply);
        Assert.Equal(AuditMethod.Failed, reply!.Method);
        Assert.Equal(0, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task ADenial_CoolsDownEveryRunOfThatConnection()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Denied;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var denied = await client.ReleaseRunAsync(Run(), Token);
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        var altered = await client.ReleaseRunAsync(Run(command: ["npm", "run", "migrate", "--verbose"]), Token);

        Assert.Equal(AuditMethod.Prompt, denied!.Method);
        Assert.Equal(AuditMethod.Cooldown, altered!.Method);
        Assert.Equal(1, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task LockWhileAsked_ReleasesNothing()
    {
        _fixture.Channel.Hold = true;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var pending = client.ReleaseRunAsync(Run(), Token).AsTask();
        await _fixture.Channel.Waiting.WaitAsync(_wait, Token);
        _lifetime!.End();
        var reply = await pending.WaitAsync(_wait, Token);

        AssertNothingReleased(reply);
        Assert.Equal(AuditMethod.VaultLocked, reply!.Method);
        Assert.True(_fixture.Channel.Withdrawn);
    }

    [Fact]
    public async Task ChangedOnDisk_IsVaultChanged_AndEndsTheRunGrants()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);
        await client.ReleaseRunAsync(Run(), Token);
        Assert.Single(owner.Authority.Activity.EnvGrants);

        using (var other = Vault.Open(VaultPath, EnvStoreTests.MasterPassword))
        {
            new EnvStore(other).TrySet("acme-api", "NEW_KEY", "x", out _);
            other.Save();
        }

        var reply = await client.ReleaseRunAsync(Run(), Token);

        AssertNothingReleased(reply);
        Assert.Equal(AuditMethod.VaultChanged, reply!.Method);
        Assert.Empty(owner.Authority.Activity.EnvGrants);
    }

    [Fact]
    public async Task EditingAnInjectedEntry_EndsItsRunGrant()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);
        await client.ReleaseRunAsync(Run(), Token);
        await client.ReleaseRunAsync(References(("GH", "kp:///personal/github#username")), Token);
        Assert.Equal(2, owner.Authority.Activity.EnvGrants.Count);

        _grants.RevokeEntries(VaultEdit.Of(new EntryName("env/acme-api", "STRIPE_SECRET_KEY")));

        var left = Assert.Single(owner.Authority.Activity.EnvGrants);
        Assert.Equal(["personal/github"], left.Entries);
    }

    [Fact]
    public async Task AnotherSessionsRequest_IsRefused()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var stale = await client.ReleaseRunAsync(Run(session: "session-zero"), Token);

        AssertNothingReleased(stale);
        Assert.Equal(AuditMethod.NoSession, stale!.Method);
        Assert.Equal(0, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task NoStandingRuleReleasesARun()
    {
        Assert.True(Toml.TryParse(
            "[[allow]]\nclient = \"claude-code\"\nentries = [\"**\"]\nfields = [\"password\"]\nmax_ttl_seconds = 60\n",
            out var syntax,
            out var syntaxError), syntaxError);
        Assert.True(PolicyDocument.TryCreate(syntax, out var rules, out var error), error);
        using var fixture = new ApproverFixture(rules);
        fixture.Channel.Answer = ApprovalAnswer.Denied;
        await using var owner = Owner.Start(this, fixture);
        await using var client = await AttachedAsync(owner);

        var reply = await client.ReleaseRunAsync(Run(), Token);

        AssertNothingReleased(reply);
        Assert.Equal(1, fixture.Channel.Asked);
    }

    [Fact]
    public async Task Activity_ListsTheWaitingRun_AndItsGrant_AsKindRun()
    {
        _fixture.Channel.Hold = true;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var pending = client.ReleaseRunAsync(Run(), Token).AsTask();
        await _fixture.Channel.Waiting.WaitAsync(_wait, Token);

        var waiting = Assert.Single(owner.Authority.Activity.WaitingRuns);
        Assert.Equal("npm run migrate", waiting.Prompt.Command);

        _fixture.Clock.Advance(ApprovalLimits.Default.Window);
        await pending.WaitAsync(_wait, Token);

        _fixture.Channel.Hold = false;
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var fresh = await AttachedAsync(owner);
        await fresh.ReleaseRunAsync(Run(), Token);

        var grant = Assert.Single(GrantSummary.Of(owner.Authority.Activity));
        Assert.Equal("run", grant.Kind);
        Assert.Equal("claude-code-cli", grant.Client);
    }

    [Fact]
    public async Task RevokeClient_EndsItsCredentialAndRunGrants()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);
        await client.ReleaseRunAsync(Run(), Token);
        Assert.Single(owner.Authority.Activity.EnvGrants);

        owner.Authority.RevokeClient("someone-else");
        Assert.Single(owner.Authority.Activity.EnvGrants);

        owner.Authority.RevokeClient(_label);
        Assert.Empty(owner.Authority.Activity.EnvGrants);
    }

    [Fact]
    public async Task EveryCommittedRelease_IsInTheLedger_AndANewLifetimeStartsEmpty()
    {
        _fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        await client.ReleaseRunAsync(Run(), Token);

        var released = owner.Authority.Released;
        Assert.Equal(["env/acme-api/STRIPE_SECRET_KEY", "env/acme-api/DATABASE_URL"], released.Select(seen => seen.Entry));
        Assert.All(released, seen => Assert.Equal(("claude-code", "run"), (seen.Client, seen.How)));

        _lifetime!.End();
        _lifetime = new SessionLifetime("session-two");

        Assert.Empty(owner.Authority.Released);
    }

    [Fact]
    public async Task AttachedBridges_AreListedWithTheirIdentity()
    {
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);
        await using var runner = await ApproverClient.TryConnectAsync(owner.PipeName, _wait, Token);
        await runner!.AttachAsync(new AttachRequest(VaultPath), Token);

        var listed = Assert.Single(owner.Authority.Clients);
        Assert.Equal(("claude-code-cli", _label), (listed.Name, listed.Label));
    }

    /// <summary>A real owner of the test's vault on its own pipe.</summary>
    private sealed class Owner : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly ApproverListener _listener;
        private readonly Task _running;

        private Owner(string pipeName, SessionAuthority authority)
        {
            PipeName = pipeName;
            Authority = authority;
            _listener = new ApproverListener(pipeName, authority);
            _running = _listener.RunAsync(_stop.Token);
        }

        internal string PipeName { get; }

        internal SessionAuthority Authority { get; }

        internal static Owner Start(SessionAuthorityRunTests test, ApproverFixture? fixture = null, bool clients = false)
        {
            var used = fixture ?? test._fixture;
            var handler = new ApproverHandler(
                used.Source,
                used.Source,
                used.Gate,
                used.Grants,
                used.Policy,
                clients: clients ? new ClientPolicySource(test.ClientsPath) : null);

            var authority = new SessionAuthority(
                VaultIdentity.Of(test._directory, test.VaultPath),
                () => test._lifetime,
                handler,
                new SessionEnvironments(
                    used.Gate,
                    lifetime => ReferenceEquals(lifetime, test._lifetime) && lifetime.IsLive ? test._vault : null,
                    used.Clock,
                    test._grants,
                    test._narration.Add),
                clock: used.Clock);

            return new Owner("keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)), authority);
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
