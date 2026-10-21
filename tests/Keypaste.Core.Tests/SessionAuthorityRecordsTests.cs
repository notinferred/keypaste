using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Infrastructure;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The owner records every listing, credential and run request it answers, once, in its own home's log,
/// before the reply leaves, and refuses a release it cannot record (THREATS.md T-6, T-14). Over a real
/// pipe, so the line is shown to be written before the reply is read.
/// </summary>
public sealed class SessionAuthorityRecordsTests : IDisposable
{
    private const string _stripe = "sk_live_RECORDS_SENTINEL_51c7";
    private const string _database = "postgres://records-sentinel@db/app";
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-authority-records-").FullName;
    private readonly ApproverFixture _fixture = new();
    private readonly List<string> _narration = [];
    private readonly Vault _vault;
    private readonly OwnerLog _log;
    private SessionLifetime? _lifetime = new("session-one");

    public SessionAuthorityRecordsTests()
    {
        using (var created = Vault.Create(VaultPath, EnvStoreTests.MasterPassword))
        {
            created.AddEntry(new VaultEntry { GroupPath = "env/dev", Title = "STRIPE_KEY", Password = _stripe });
            ProjectVariables.Set(created, "acme-api", "DATABASE_URL", _database);
            created.Save();
        }

        _vault = Vault.Open(VaultPath, EnvStoreTests.MasterPassword);
        _log = new OwnerLog(_directory);
        _fixture.Settings.Records[_agent] = Sees;
    }

    private const string _agent = "billing-bot";

    /// <summary>What the test's agent may do: see as far as its bridge exposes, and run commands.</summary>
    private static AgentAccess Sees { get; } = ApproverFixture.SeesAll with { Run = true };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Program => OperatingSystem.IsWindows() ? @"C:\tools\npm.exe" : "/usr/bin/npm";

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    private string VaultKey => VaultIdentity.Of(_directory, VaultPath).Key;

    public void Dispose()
    {
        _lifetime?.Dispose();
        _log.Dispose();
        _vault.Dispose();
        _fixture.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private CredentialRequest Credential(string session = "session-one", string? vault = null) => new()
    {
        Entry = "env/dev/STRIPE_KEY",
        Field = "password",
        Reason = "deploy billing to staging",
        TtlSeconds = 60,
        Exposure = ["env/**"],
        ClientName = "claude-code",
        ClientVersion = "2.1.0",
        ClientLabel = "billing-bot",
        Vault = vault ?? VaultPath,
        Session = session,
    };

    private NamesRequest Listing(string session = "session-one") => new(["env/**"]) { ClientLabel = _agent, Vault = VaultPath, Session = session };

    private RunRequest Run(string session = "session-one") => new()
    {
        Program = Program,
        Command = ["npm", "run", "migrate"],
        Directory = _directory,
        Project = "acme-api",
        Reason = "run the pending migration",
        Exposure = ["env/acme-api/**"],
        ClientName = "claude-code",
        ClientVersion = "2.1.0",
        ClientLabel = "billing-bot",
        Vault = VaultPath,
        Session = session,
    };

    /// <summary>
    /// A program that speaks the owner's pipe itself, with no bridge in front of it to write a line, is
    /// recorded by the owner composed as the app and <c>keypaste approve</c> compose it, under the client
    /// it claims (T-14).
    /// </summary>
    [Fact]
    public async Task ARequestStraightToThePipeIsRecorded()
    {
        var pipe = "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        using var lifetime = new SessionLifetime();
        var channel = new FakeChannel { Answer = ApprovalAnswer.ApprovedOnce };
        var settings = new FakeAgentSettings();
        settings.Records["no-bridge"] = ApproverFixture.SeesAll;

        using (var server = SessionServer.Listen(pipe, new SessionServerOptions
        {
            Vault = VaultIdentity.Of(_directory, VaultPath),
            Lifetime = lifetime,
            CurrentLifetime = () => lifetime,
            UnlockedFor = asked => ReferenceEquals(asked, lifetime) && asked.IsLive ? _vault : null,
            Channel = channel,
            Settings = settings,
            Hourly = new HourlyLimit(TimeProvider.System),
            Audit = () => _log.Log,
            WatchEdits = _ => { },
            UnwatchEdits = _ => { },
        }))
        {
            using var stop = new CancellationTokenSource();
            var running = server.RunAsync(stop.Token);

            await using (var client = await ApproverClient.TryConnectAsync(pipe, _wait, Token))
            {
                Assert.NotNull(client);
                var attached = await client.AttachAsync(new AttachRequest(VaultPath) { Client = new AttachClient("no-bridge", "0.1", null) }, Token);
                Assert.True(attached?.Attached);

                var listing = await client.ListAsync(Listing(lifetime.Id) with { ClientLabel = "no-bridge" }, Token);
                var reply = await client.RequestAsync(Credential(lifetime.Id) with { ClientName = "no-bridge", ClientVersion = "0.1", ClientLabel = "no-bridge" }, Token);

                Assert.True(listing?.VaultUnlocked);
                Assert.Equal(_stripe, reply?.Value);
            }

            await stop.CancelAsync();

            try
            {
                await running;
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
                // Stopping the listener is how it ends.
            }
        }

        var lines = _log.Lines();
        Assert.Equal(["list_entry_names", "request_credential"], lines.Select(line => line.GetProperty("tool").GetString()));
        Assert.All(lines, line =>
        {
            Assert.Equal("no-bridge", line.GetProperty("client").GetProperty("name").GetString());
            Assert.Equal("granted", line.GetProperty("decision").GetString());
            Assert.Equal(lifetime.Id, line.GetProperty("session").GetString());
            Assert.Equal(VaultKey, line.GetProperty("vault").GetString());
        });
        Assert.Equal("env/dev/STRIPE_KEY", lines[1].GetProperty("args").GetProperty("entry").GetString());
        Assert.DoesNotContain(_stripe, _log.Text(), StringComparison.Ordinal);
    }

    /// <summary>A listing carries nothing of its own, so it is recorded under the client its connection attached as, counting what it named.</summary>
    [Fact]
    public async Task AListing_IsRecordedUnderTheClientItsConnectionAttachedAs()
    {
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var reply = await client.ListAsync(Listing(), Token);

        Assert.True(reply?.Recorded);
        var line = Assert.Single(_log.Lines());
        Assert.Equal("list_entry_names", line.GetProperty("tool").GetString());
        Assert.Equal("claude-code", line.GetProperty("client").GetProperty("name").GetString());
        Assert.Equal("2.1.0", line.GetProperty("client").GetProperty("version").GetString());
        Assert.Equal("billing-bot", line.GetProperty("client").GetProperty("label").GetString());
        Assert.Equal("granted", line.GetProperty("decision").GetString());
        Assert.Equal("exposure", line.GetProperty("method").GetString());
        Assert.Equal($"listed {reply!.Names.Count} item names", line.GetProperty("reason").GetString());
        Assert.Equal(["env/**"], line.GetProperty("exposure").EnumerateArray().Select(glob => glob.GetString()));
        Assert.Equal("session-one", line.GetProperty("session").GetString());
        Assert.Equal(VaultKey, line.GetProperty("vault").GetString());
    }

    /// <summary>A person's yes is recorded before the reply is read, with what was asked and for how long, and never the value.</summary>
    [Fact]
    public async Task ACredentialGrant_IsRecordedBeforeItsReplyIsRead_WithoutItsValue()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var reply = await client.RequestAsync(Credential(), Token);
        var line = Assert.Single(_log.Lines());

        Assert.Equal(ApproverFixture.Sentinel, reply?.Value);
        Assert.True(reply!.Recorded);
        Assert.Equal("request_credential", line.GetProperty("tool").GetString());
        Assert.Equal("claude-code", line.GetProperty("client").GetProperty("name").GetString());
        Assert.Equal("billing-bot", line.GetProperty("client").GetProperty("label").GetString());
        Assert.Equal("env/dev/STRIPE_KEY", line.GetProperty("args").GetProperty("entry").GetString());
        Assert.Equal("password", line.GetProperty("args").GetProperty("field").GetString());
        Assert.Equal(60, line.GetProperty("args").GetProperty("ttl_seconds").GetInt32());
        Assert.Equal("granted", line.GetProperty("decision").GetString());
        Assert.Equal("prompt", line.GetProperty("method").GetString());
        Assert.Equal(reply.TtlSeconds, line.GetProperty("granted_seconds").GetInt32());
        Assert.Equal("session-one", line.GetProperty("session").GetString());
        Assert.Equal(VaultKey, line.GetProperty("vault").GetString());
        Assert.DoesNotContain(ApproverFixture.Sentinel, _log.Text(), StringComparison.Ordinal);
        Assert.Single(owner.Authority.Released);
    }

    /// <summary>A refusal says who refused it, and keeps no grant length.</summary>
    [Fact]
    public async Task APersonsRefusal_IsRecordedAsTheirs()
    {
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var reply = await client.RequestAsync(Credential(), Token);
        var line = Assert.Single(_log.Lines());

        Assert.Equal(AuditDecision.Denied, reply?.Decision);
        Assert.True(reply!.Recorded);
        Assert.Equal("denied", line.GetProperty("decision").GetString());
        Assert.Equal("prompt", line.GetProperty("method").GetString());
        Assert.False(line.TryGetProperty("granted_seconds", out _));
    }

    /// <summary>
    /// A release nobody was asked about has the line as its only witness, so it says <c>automatic</c>,
    /// names whose mode released it, and keeps the agent's stated reason that nobody read (T-12).
    /// </summary>
    [Fact]
    public async Task AnAutomaticRelease_IsRecordedAsAutomatic()
    {
        using var automatic = new ApproverFixture();
        automatic.Settings.Records[_agent] = Sees with { Mode = AgentMode.Automatic };
        await using var owner = Owner.Start(this, automatic);
        await using var client = await AttachedAsync(owner);

        var reply = await client.RequestAsync(Credential(), Token);
        var line = Assert.Single(_log.Lines());

        Assert.Equal(AuditMethod.Automatic, reply?.Method);
        Assert.Equal(0, automatic.Channel.Asked);
        Assert.Equal("granted", line.GetProperty("decision").GetString());
        Assert.Equal("automatic", line.GetProperty("method").GetString());
        Assert.Contains("billing-bot's mode for this item is Automatic", line.GetProperty("reason").GetString()!, StringComparison.Ordinal);
        Assert.NotNull(line.GetProperty("args").GetProperty("reason_sha256").GetString());
        Assert.DoesNotContain(ApproverFixture.Sentinel, _log.Text(), StringComparison.Ordinal);
    }

    /// <summary>A request refused before it is considered is recorded too, under no session, against this vault only when it named it.</summary>
    [Fact]
    public async Task ARequestRefusedAtAdmission_IsRecordedUnderNoSession()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);

        await using (var unattached = await ConnectAsync(owner))
        {
            await unattached.ListAsync(Listing(), Token);
            await unattached.RequestAsync(Credential(), Token);
            await unattached.ReleaseRunAsync(Run(), Token);
        }

        await using (var client = await AttachedAsync(owner))
        {
            await client.RequestAsync(Credential(vault: Path.Combine(_directory, "other.kdbx")), Token);
            _lifetime = null;
            await client.RequestAsync(Credential(), Token);
        }

        var lines = _log.Lines();
        Assert.Equal(
            ["list_entry_names", "request_credential", "run", "request_credential", "request_credential"],
            lines.Select(line => line.GetProperty("tool").GetString()));
        Assert.Equal(
            ["no-session", "no-session", "no-session", "no-session", "vault-locked"],
            lines.Select(line => line.GetProperty("method").GetString()));
        Assert.Equal(new[] { VaultKey, VaultKey, VaultKey, null, VaultKey }, lines.Select(line => line.TryGetProperty("vault", out var vault) ? vault.GetString() : null));
        Assert.All(lines, line =>
        {
            Assert.Equal("denied", line.GetProperty("decision").GetString());
            Assert.False(line.TryGetProperty("session", out _));
        });
        Assert.False(lines[0].GetProperty("client").TryGetProperty("name", out _));
        Assert.Equal(0, _fixture.Channel.Asked);
    }

    /// <summary>A request the owner reads and cannot consider is recorded under the session it named.</summary>
    [Fact]
    public async Task AnInvalidRequest_IsRecordedAsInvalid()
    {
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        await client.RequestAsync(Credential() with { Reason = string.Empty }, Token);
        await client.ReleaseRunAsync(Run() with { Reason = string.Empty }, Token);

        var lines = _log.Lines();
        Assert.Equal(["invalid-request", "invalid-request"], lines.Select(line => line.GetProperty("method").GetString()));
        Assert.All(lines, line => Assert.Equal("session-one", line.GetProperty("session").GetString()));
        Assert.Equal(0, _fixture.Channel.Asked);
    }

    /// <summary>An approved run is recorded before its values leave, naming its command, its hash and its entries, and never a value.</summary>
    [Fact]
    public async Task ARun_IsRecordedWithItsCommandAndEntries_BeforeItsValuesLeave()
    {
        _fixture.Channel.Answer = ApprovalAnswer.ApprovedOnce;
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var reply = await client.ReleaseRunAsync(Run(), Token);
        var line = Assert.Single(_log.Lines());

        Assert.Equal(EnvOutcome.Resolved, reply?.Set.Outcome);
        Assert.True(reply!.Recorded);
        Assert.Equal("run", line.GetProperty("tool").GetString());
        Assert.Equal("claude-code", line.GetProperty("client").GetProperty("name").GetString());
        Assert.Equal("granted", line.GetProperty("decision").GetString());
        Assert.Equal("prompt", line.GetProperty("method").GetString());
        Assert.Equal("npm run migrate", line.GetProperty("command").GetString());
        Assert.Equal(AuditRecord.HashOf([Program, "run", "migrate"]), line.GetProperty("command_sha256").GetString());
        Assert.Equal(["env/acme-api/.env"], line.GetProperty("entries").EnumerateArray().Select(entry => entry.GetString()));
        Assert.Equal("session-one", line.GetProperty("session").GetString());
        Assert.DoesNotContain(_database, _log.Text(), StringComparison.Ordinal);
        Assert.Contains(_narration, said => said.StartsWith("released 1 variable(s) to billing-bot", StringComparison.Ordinal));
    }

    /// <summary>
    /// A release the owner cannot record is not made: an Automatic release, which no person witnesses,
    /// the listing, and a run each come back with nothing in them, say they were not recorded, and leave
    /// the ledger as it was (T-6).
    /// </summary>
    /// <remarks>
    /// The failure is provoked by holding the log's sidecar lock, which is how <c>AuditLog</c>
    /// serialises writers; it also asserts the lock was contended, so a rename fails rather than passes.
    /// </remarks>
    [Fact]
    public async Task AReleaseTheOwnerCannotRecord_IsRefusedWithNothingInIt()
    {
        using var policed = new ApproverFixture();
        policed.Settings.Records[_agent] = Sees with { Mode = AgentMode.Automatic };
        policed.Channel.Answer = ApprovalAnswer.ApprovedOnce;
        await using var owner = Owner.Start(this, policed);
        await using var client = await AttachedAsync(owner);

        using (var held = new FileStream(_log.Path + ".lock", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var credential = await client.RequestAsync(Credential(), Token);
            var listing = await client.ListAsync(Listing(), Token);
            var run = await client.ReleaseRunAsync(Run(), Token);

            Assert.Equal(AuditDecision.Denied, credential?.Decision);
            Assert.Equal(AuditMethod.Failed, credential!.Method);
            Assert.Null(credential.Value);
            Assert.False(credential.Recorded);
            Assert.Equal(1, policed.Source.Reads);

            Assert.Empty(listing!.Names);
            Assert.False(listing.Recorded);

            Assert.NotEqual(EnvOutcome.Resolved, run?.Set.Outcome);
            Assert.Empty(run!.Set.Variables);
            Assert.False(run.Recorded);
            Assert.NotEmpty(held.Name);
        }

        Assert.Empty(_log.Lines());
        Assert.Empty(owner.Authority.Released);
        Assert.Contains("refused env/dev/STRIPE_KEY for billing-bot: the audit log could not be written, so nothing was sent", _narration);
        Assert.Contains("refused a run for billing-bot: the audit log could not be written, so nothing was sent", _narration);
    }

    /// <summary>
    /// An owner whose log will not open refuses a credential, a listing, a run and a <c>run --session</c>
    /// at once, before anybody is asked, each reply saying it was not recorded (T-6).
    /// </summary>
    [Fact]
    public async Task AnOwnerWhoseLogWillNotOpen_RefusesEveryRequest_BeforeAnybodyIsAsked()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this, audit: () => null);
        await using var client = await AttachedAsync(owner);

        var credential = await client.RequestAsync(Credential(), Token);
        var listing = await client.ListAsync(Listing(), Token);
        var run = await client.ReleaseRunAsync(Run(), Token);
        var env = await client.ReleaseEnvAsync(new EnvRequest("acme-api", ["npm", "run", "migrate"], _directory) { Vault = VaultPath, Session = "session-one" }, Token);

        Assert.Equal(0, _fixture.Channel.Asked);

        Assert.NotNull(credential);
        Assert.Equal((AuditDecision.Denied, AuditMethod.Failed, false), (credential.Decision, credential.Method, credential.Recorded));
        Assert.Null(credential.Value);

        Assert.NotNull(listing);
        Assert.Equal(AuditMethod.Failed, listing.Refusal);
        Assert.False(listing.Recorded);
        Assert.Empty(listing.Names);

        Assert.NotNull(run);
        Assert.Equal((EnvOutcome.Unreadable, AuditMethod.Failed, false), (run.Set.Outcome, run.Method, run.Recorded));
        Assert.Empty(run.Set.Variables);

        Assert.NotNull(env);
        Assert.Equal(EnvOutcome.Unreadable, env.Set.Outcome);
        Assert.Empty(env.Set.Variables);

        Assert.All([credential.Reason, listing.Reason, run.Reason, env.Reason], reason => Assert.Equal("the audit log could not be opened", reason));
        Assert.Empty(owner.Authority.Released);
    }

    /// <summary>A failure while the owner decides is answered as a failure and recorded, rather than dropping the connection unrecorded (law 3.7).</summary>
    [Fact]
    public async Task AFailureWhileDeciding_IsRecordedAsFailed()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        _fixture.Source.During = () => throw new InvalidOperationException("the disk went away");
        await using var owner = Owner.Start(this);
        await using var client = await AttachedAsync(owner);

        var reply = await client.RequestAsync(Credential(), Token);
        var line = Assert.Single(_log.Lines());

        Assert.Equal(AuditMethod.Failed, reply?.Method);
        Assert.Null(reply!.Value);
        Assert.True(reply.Recorded);
        Assert.Equal("failed", line.GetProperty("method").GetString());
        Assert.Equal("the request could not be completed", line.GetProperty("reason").GetString());
    }

    /// <summary>A peer that hangs up while the person is being asked leaves one line, cancelled, though nobody reads its reply.</summary>
    [Fact]
    public async Task APeerThatHangsUpWhileAsked_IsRecordedOnceAsCancelled()
    {
        _fixture.Channel.Hold = true;
        await using var owner = Owner.Start(this);

        await using (var pipe = new NamedPipeClientStream(".", owner.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            await pipe.ConnectAsync(Token);
            using var framer = new MessageFramer(pipe, ownsStream: false);

            await framer.WriteAsync(ApproverProtocol.Encode(new AttachRequest(VaultPath)), Token);
            Assert.True(ApproverProtocol.TryDecode(await framer.ReadAsync(Token) ?? [], out AttachReply? attached));
            Assert.True(attached.Attached);

            await framer.WriteAsync(ApproverProtocol.Encode(Credential()), Token);
            await _fixture.Channel.Waiting.WaitAsync(_wait, Token);
        }

        var line = await Until(() => _log.Lines() is [var only] ? only : (JsonElement?)null);
        Assert.Equal("cancelled", line.GetProperty("method").GetString());
        Assert.Equal("session-one", line.GetProperty("session").GetString());
        Assert.True(_fixture.Channel.Withdrawn);
        Assert.Single(_log.Lines());
    }

    private static async Task<JsonElement> Until(Func<JsonElement?> read)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (read() is { } found)
            {
                return found;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), Token);
        }

        Assert.Fail("the owner recorded nothing");
        return default;
    }

    private static async Task<ApproverClient> ConnectAsync(Owner owner)
    {
        var client = await ApproverClient.TryConnectAsync(owner.PipeName, _wait, Token);
        Assert.NotNull(client);
        return client;
    }

    private async Task<ApproverClient> AttachedAsync(Owner owner)
    {
        var client = await ConnectAsync(owner);
        Assert.True((await client.AttachAsync(new AttachRequest(VaultPath) { Client = new AttachClient("claude-code", "2.1.0", "billing-bot") }, Token))?.Attached);
        return client;
    }

    /// <summary>An owner of the test's vault on its own pipe, recording in the test's log unless told otherwise.</summary>
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

        internal static Owner Start(SessionAuthorityRecordsTests test, ApproverFixture? fixture = null, Func<AuditLog?>? audit = null)
        {
            var used = fixture ?? test._fixture;
            var authority = new SessionAuthority(
                VaultIdentity.Of(test._directory, test.VaultPath),
                () => test._lifetime,
                used.Handler,
                audit ?? (() => test._log.Log),
                new SessionEnvironments(
                    used.Gate,
                    lifetime => ReferenceEquals(lifetime, test._lifetime) && lifetime.IsLive ? test._vault : null,
                    used.Clock,
                    Narrate: test._narration.Add),
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
