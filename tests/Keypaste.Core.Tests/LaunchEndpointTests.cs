using System.Security.Cryptography;
using System.Text.Json;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Launch;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The app's own endpoint for the runs Terminal starts on macOS, over a real pipe on every runner: one
/// unlock's authority answers the run carrying its pass there whether or not the vault's endpoint is
/// bound, and answers nothing else (D-0451).
/// </summary>
public sealed class LaunchEndpointTests : IDisposable
{
    private const string _token = "sk_live_launch_endpoint_sentinel";
    private const string _launchOnly = "this connection is only for a run confirmed in the keypaste app";
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);
    private static readonly string[] _launched = ["/bin/sh", "-c", TerminalLaunch.RunScript, "keypaste", "npm start"];

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-launch-endpoint-").FullName;
    private readonly FakeChannel _channel = new();
    private readonly ManualClock _clock = new();
    private readonly SessionLifetime _lifetime = new("session-one");
    private readonly Vault _vault;
    private readonly AuditLog _audit;
    private readonly SessionServer _server;

    public LaunchEndpointTests()
    {
        using (var created = Vault.Create(VaultPath, EnvStoreTests.MasterPassword))
        {
            ProjectVariables.Set(created, "dev", "TOKEN", _token);
            created.Save();
        }

        _vault = Vault.Open(VaultPath, EnvStoreTests.MasterPassword);
        Assert.True(AuditLog.TryOpen(AuditPath, _clock, out var audit, out var error), error);
        _audit = audit;
        _server = SessionServer.Build(new SessionServerOptions
        {
            Vault = VaultIdentity.Of(_directory, VaultPath),
            Lifetime = _lifetime,
            CurrentLifetime = () => _lifetime,
            UnlockedFor = lifetime => ReferenceEquals(lifetime, _lifetime) && lifetime.IsLive ? _vault : null,
            Channel = _channel,
            Settings = IAgentSettings.Defaults,
            Hourly = new HourlyLimit(_clock),
            Audit = () => _audit,
            WatchEdits = _ => { },
            UnwatchEdits = _ => { },
            Clock = _clock,
        });
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    private string AuditPath => Path.Combine(_directory, "audit.jsonl");

    private string WorkDirectory => Path.Combine(_directory, "work");

    public void Dispose()
    {
        _server.Dispose();
        _lifetime.Dispose();
        _vault.Dispose();
        _audit.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    /// <summary>With no vault endpoint bound, as when <c>KEYPASTE_APPROVER</c> names no usable pipe, the run Terminal starts still gets the set the app's card named, unasked and recorded.</summary>
    [Fact]
    public async Task With_the_vaults_endpoint_unbound_the_run_carrying_its_pass_is_released_and_recorded()
    {
        await using var served = Served.Start(LaunchEndpoint.NewName(), _server.Authority);
        await using var client = await AttachedAsync(served.Name);
        var claim = Claim();
        var pass = _server.Authority.IssueLaunch(claim, "session-one");
        Assert.NotNull(pass);

        var reply = await client.ReleaseEnvAsync(Launched(claim, pass), Token);

        Assert.NotNull(reply);
        Assert.Equal(EnvOutcome.Resolved, reply.Set.Outcome);
        Assert.Equal([new EnvVariable("TOKEN", _token)], reply.Set.Variables);
        Assert.Equal(0, _channel.Asked);

        var line = Assert.Single(AuditLines());
        Assert.Equal("granted", line.GetProperty("decision").GetString());
        Assert.Equal("prompt", line.GetProperty("method").GetString());
        Assert.Equal("1 variable(s), confirmed in the app", line.GetProperty("reason").GetString());
        Assert.Equal(["env/dev/.env"], line.GetProperty("entries").EnumerateArray().Select(entry => entry.GetString()));
        Assert.DoesNotContain(_token, AuditText(), StringComparison.Ordinal);
    }

    /// <summary>A run without a pass is the owner's prompt's business, which this endpoint never reaches: it is refused unasked, whatever the person would answer, and recorded.</summary>
    [Fact]
    public async Task A_run_without_a_pass_is_refused_unasked_and_recorded()
    {
        _channel.Answer = ApprovalAnswer.Approved;
        await using var served = Served.Start(LaunchEndpoint.NewName(), _server.Authority);
        await using var client = await AttachedAsync(served.Name);

        var reply = await client.ReleaseEnvAsync(Launched(Claim(), pass: null), Token);

        Assert.NotNull(reply);
        Assert.Equal(EnvOutcome.Unauthorized, reply.Set.Outcome);
        Assert.Equal(_launchOnly, reply.Reason);
        Assert.Empty(reply.Set.Variables);
        Assert.Equal(0, _channel.Asked);

        var line = Assert.Single(AuditLines());
        Assert.Equal("denied", line.GetProperty("decision").GetString());
        Assert.Equal("invalid-request", line.GetProperty("method").GetString());
        Assert.Equal(_launchOnly, line.GetProperty("reason").GetString());
    }

    /// <summary>
    /// An agent that finds the endpoint gets nothing: a bridge's attachment is answered but not shown
    /// as connected, and every request an agent, <c>keypaste grants</c>, <c>keypaste lock</c>,
    /// <c>keypaste mcp policy</c> or a token run makes ends the connection unanswered, asking nobody and
    /// recording nothing.
    /// </summary>
    [Theory]
    [InlineData("names")]
    [InlineData("credential")]
    [InlineData("run")]
    [InlineData("token")]
    [InlineData("grants")]
    [InlineData("revoke")]
    [InlineData("lock")]
    [InlineData("access-list")]
    [InlineData("access-change")]
    [InlineData("edit")]
    public async Task Every_other_request_ends_its_connection_unanswered(string kind)
    {
        _channel.Answer = ApprovalAnswer.Approved;
        await using var served = Served.Start(LaunchEndpoint.NewName(), _server.Authority);
        await using var client = await ApproverClient.TryConnectAsync(served.Name, _wait, Token);
        Assert.NotNull(client);

        var attached = await client.AttachAsync(new AttachRequest(VaultPath) { Client = new AttachClient("claude-code", "2.1", null), Exposure = ["**"] }, Token);
        Assert.True(attached?.Attached);
        Assert.Empty(_server.Authority.Clients);

        object? reply = kind switch
        {
            "names" => await client.ListAsync(new NamesRequest(["**"]) { Vault = VaultPath, Session = "session-one" }, Token),
            "credential" => await client.RequestAsync(Credential(), Token),
            "run" => await client.ReleaseRunAsync(Run(), Token),
            "token" => await client.ReleaseTokenEnvAsync(
                new TokenEnvRequest("kpt_launch_endpoint", "dev", EnvProfileNames.Default, ["deploy"], WorkDirectory) { Vault = VaultPath, Session = "session-one" },
                Token),
            "grants" => await client.GrantsAsync(new GrantsRequest { Vault = VaultPath, Session = "session-one" }, Token),
            "revoke" => await client.RevokeGrantsAsync(new RevokeGrantsRequest([], null, All: true) { Vault = VaultPath, Session = "session-one" }, Token),
            "access-list" => await client.ListAccessAsync(new AccessListRequest { Vault = VaultPath, Session = "session-one" }, Token),
            "access-change" => await client.ChangeAccessAsync(
                new AccessChangeRequest("claude-code", "so deploys stop asking") { Origin = AccessOrigin.Agent, Mode = "automatic", Vault = VaultPath, Session = "session-one" },
                Token),
            "edit" => await client.EditAsync(
                new EditRequest(new Editing.RemoveItem("env/dev/STRIPE_KEY")) { Vault = VaultPath, Session = "session-one" },
                Token),
            _ => await client.LockAsync(new LockRequest { Vault = VaultPath, Session = "session-one" }, Token),
        };

        Assert.Null(reply);
        Assert.Equal(0, _channel.Asked);
        Assert.Empty(AuditLines());
        Assert.True(_lifetime.IsLive);
    }

    /// <summary>
    /// One authority answers both endpoints, so their connections are told apart: a run that attaches
    /// and hangs up on the launch endpoint leaves an agent attached on the vault's as it was.
    /// </summary>
    [Fact]
    public async Task A_run_hanging_up_on_the_launch_endpoint_leaves_an_agent_on_the_vaults_attached()
    {
        var vaultEndpoint = "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        _server.Bind(vaultEndpoint);
        using var stop = new CancellationTokenSource();
        var agents = _server.RunAsync(stop.Token);

        try
        {
            await using var served = Served.Start(LaunchEndpoint.NewName(), _server.Authority);
            await using var agent = await ApproverClient.TryConnectAsync(vaultEndpoint, _wait, Token);
            Assert.NotNull(agent);
            Assert.True((await agent.AttachAsync(new AttachRequest(VaultPath) { Client = new AttachClient("claude-code", "2.1", null) }, Token))?.Attached);

            var runner = await AttachedAsync(served.Name);
            await runner.DisposeAsync();

            Assert.Equal("claude-code", Assert.Single(_server.Authority.Clients).Name);

            var listed = await agent.ListAsync(new NamesRequest(["env/**"]) { Vault = VaultPath, Session = "session-one" }, Token);
            Assert.True(listed?.VaultUnlocked, listed?.Reason);
        }
        finally
        {
            await stop.CancelAsync();
            await agents;
        }
    }

    /// <summary>
    /// The app's name for its launch endpoint is one no other process chose, which a pipe may carry; once
    /// a lock disposes the endpoint nothing answers there, and the next unlock binds the name again.
    /// </summary>
    [Fact]
    public async Task A_launch_endpoints_name_answers_nothing_once_disposed_and_binds_again()
    {
        var name = LaunchEndpoint.NewName();
        Assert.StartsWith(LaunchEndpoint.Prefix, name, StringComparison.Ordinal);
        Assert.NotEqual(name, LaunchEndpoint.NewName());
        Assert.True(ApproverEndpoint.IsValidName(name, out var error), error);

        var first = Served.Start(name, _server.Authority);
        await first.DisposeAsync();

        await using (var gone = await ApproverClient.TryConnectAsync(name, TimeSpan.FromMilliseconds(300), Token))
        {
            Assert.Null(gone);
        }

        await using var again = Served.Start(name, _server.Authority);
        await using var client = await AttachedAsync(again.Name);
    }

    /// <summary>What the app's card showed for dev, which its pass names.</summary>
    private LaunchClaim Claim() =>
        new("dev", EnvProfileNames.Default, WorkDirectory, _launched, EnvResolution.Resolve(_vault, "dev", EnvProfileNames.Default, keys: null, _clock).Preview);

    /// <summary>The request the window's <c>keypaste run --session</c> sends for a claim.</summary>
    private EnvRequest Launched(LaunchClaim claim, string? pass) =>
        new(claim.Project, claim.Command, claim.Directory) { Vault = VaultPath, Session = "session-one", Profile = claim.Profile, Launch = pass };

    private CredentialRequest Credential() => new()
    {
        Entry = "env/dev/.env",
        Field = "TOKEN",
        Reason = "deploy",
        TtlSeconds = 60,
        Exposure = ["**"],
        Vault = VaultPath,
        Session = "session-one",
    };

    private RunRequest Run() => new()
    {
        Program = "deploy",
        Command = ["deploy"],
        Directory = WorkDirectory,
        Project = "dev",
        Reason = "deploy",
        Exposure = ["**"],
        Vault = VaultPath,
        Session = "session-one",
    };

    private async Task<ApproverClient> AttachedAsync(string endpoint)
    {
        var client = await ApproverClient.TryConnectAsync(endpoint, _wait, Token);
        Assert.NotNull(client);
        Assert.True((await client.AttachAsync(new AttachRequest(VaultPath), Token))?.Attached);
        return client;
    }

    /// <summary>The log as the owner still holds it open for writing, which File.ReadAllText cannot share on Windows.</summary>
    private string AuditText()
    {
        using var stream = new FileStream(AuditPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    private List<JsonElement> AuditLines()
    {
        return [.. AuditText().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(Parse)];

        static JsonElement Parse(string line)
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }
    }

    /// <summary>A launch endpoint accepting until the test is done with it.</summary>
    private sealed class Served : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly LaunchEndpoint _endpoint;
        private readonly Task _running;

        private Served(LaunchEndpoint endpoint)
        {
            _endpoint = endpoint;
            _running = endpoint.RunAsync(_stop.Token);
        }

        internal string Name => _endpoint.Name;

        internal static Served Start(string name, SessionAuthority authority) => new(LaunchEndpoint.Listen(name, authority));

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

            _endpoint.Dispose();
            _stop.Dispose();
        }
    }
}
