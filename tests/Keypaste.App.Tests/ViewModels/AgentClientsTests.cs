using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Clients;
using Keypaste.Core.Ipc;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// Agents › MCP clients: who reaches this vault and how each is asked, written to <c>clients.toml</c>
/// (D-0360, D-0361). Identities are display only; a stricter policy also ends what that client holds.
/// </summary>
public sealed class AgentClientsTests
{
    private static readonly TimeSpan _connect = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Cards_ListConnectedAndSeenClients_WithPolicy()
    {
        await using var app = await App.StartAsync(new AttachClient("claude", "2.0", "claude-code"));
        app.Audit("cursor", app.Authority.Session.Identity!.Key);
        app.Audit("elsewhere", "ffffffffffffffff");

        using var model = app.Model();

        Assert.Equal(["Claude Code", "Cursor", "Every other client"], model.Clients.Select(card => card.Name));
        var connected = model.Clients[0];
        Assert.Equal(("CC", "Connected", "Claude Code · MCP stdio"), (connected.Initials, connected.Status, connected.Detail));
        Assert.Equal("Session grants up to 1h", connected.PolicyText);
        Assert.Equal("Idle", model.Clients[1].Status);
        Assert.Equal(3, AgentActivityViewModel.PolicyOptions.Count);
    }

    [Fact]
    public async Task SettingAskEveryTime_SavesAndEndsThatClientsGrants()
    {
        await using var app = await App.StartAsync(new AttachClient("claude", "2.0", "ci-probe"));
        app.Person.Answer = ApprovalAnswer.Approved;
        Assert.Equal(AuditMethod.Prompt, (await app.RequestAsync())!.Method);
        Assert.Single(app.Authority.Activity.Grants);

        using var model = app.Model();
        var card = model.Clients.Single(row => row.Label == "ci-probe");

        card.PolicyText = ClientPolicies.Describe(ClientPolicy.AskEveryTime);

        Assert.True(ClientPolicies.TryLoad(app.ClientsPath, out var written, out _));
        Assert.Equal(ClientPolicy.AskEveryTime, written!.For("ci-probe"));
        Assert.Empty(app.Authority.Activity.Grants);
        Assert.Equal(ClientPolicy.AskEveryTime, model.Clients.Single(row => row.Label == "ci-probe").Policy);
    }

    [Fact]
    public async Task AnUnlabeledClient_CannotBeGivenAPolicy_AndIsHeldToTheStarRow()
    {
        await using var app = await App.StartAsync(new AttachClient("Zed", null, null));
        File.WriteAllText(app.ClientsPath, "[[client]]\nlabel = \"*\"\npolicy = \"ask\"\n");

        using var model = app.Model();
        var card = model.Clients.Single(row => row.Name == "Zed");

        Assert.False(card.CanSetPolicy);
        Assert.Contains("--client-label", card.Hint, StringComparison.Ordinal);
        Assert.Equal(ClientPolicy.AskEveryTime, card.Policy);

        card.Policy = ClientPolicy.InjectOnly;

        Assert.Equal("[[client]]\nlabel = \"*\"\npolicy = \"ask\"\n", File.ReadAllText(app.ClientsPath));
    }

    [Fact]
    public async Task ALabelNoRowCanHold_CannotBeGivenAPolicy()
    {
        await using var app = await App.StartAsync(null);
        app.Audit("old\"bridge", app.Authority.Session.Identity!.Key);

        using var model = app.Model();
        var card = model.Clients.Single(row => row.Label == "old\"bridge");

        Assert.False(card.CanSetPolicy);

        card.PolicyText = ClientPolicies.Describe(ClientPolicy.AskEveryTime);

        Assert.False(File.Exists(app.ClientsPath));
    }

    [Fact]
    public async Task AMalformedFile_IsShown_AndResetWritesAnEmptyOne()
    {
        await using var app = await App.StartAsync(null);
        File.WriteAllText(app.ClientsPath, "[[client]]\nlabel = \"x\"\npolicy = \"maybe\"\n");

        using var model = app.Model();

        Assert.True(model.HasClientsProblem);
        Assert.Contains("refused", model.ClientsProblem, StringComparison.Ordinal);

        model.ResetClientsCommand.Execute(null);

        Assert.False(model.HasClientsProblem);
        Assert.True(ClientPolicies.TryLoad(app.ClientsPath, out var reset, out _));
        Assert.Empty(reset!.Rows);
    }

    private sealed class App : IAsyncDisposable
    {
        private readonly ApproverClient _client;

        private App(TempVault fixture, ManualClock clock, Person person, AppAuthority authority, ApproverClient client, string session)
        {
            Fixture = fixture;
            Clock = clock;
            Person = person;
            Authority = authority;
            _client = client;
            Session = session;
        }

        internal TempVault Fixture { get; }

        internal ManualClock Clock { get; }

        internal Person Person { get; }

        internal AppAuthority Authority { get; }

        internal string Session { get; }

        internal string ClientsPath => KeypasteHome.ClientsPath(Fixture.Home);

        internal static async Task<App> StartAsync(AttachClient? identity)
        {
            var fixture = new TempVault();
            var clock = new ManualClock();
            var person = new Person();
#pragma warning disable CA2000
            var authority = new AppAuthority(new AppVaultSession(clock, home: fixture.Home), null, () => person);
#pragma warning restore CA2000

            using (var master = TempVault.Secret(TempVault.Password))
            {
                Assert.Equal(UnlockOutcome.Opened, authority.Session.TryUnlock(fixture.Path_, master.Value));
            }

            var serving = Assert.IsType<AuthorityStatus.Serving>(authority.Status);
            var client = await ApproverClient.TryConnectAsync(serving.Endpoint, _connect, Token);
            Assert.NotNull(client);
            Assert.True((await client.AttachAsync(new AttachRequest(fixture.Path_) { Client = identity }, Token))!.Attached);

            return new App(fixture, clock, person, authority, client, serving.Session);
        }

        internal AgentActivityViewModel Model() => new(Authority, Fixture.Home, Clock);

        internal Task<CredentialReply?> RequestAsync() =>
            _client.RequestAsync(
                new CredentialRequest
                {
                    Entry = "example",
                    Field = "password",
                    Reason = "deploy",
                    TtlSeconds = 60,
                    Exposure = ["**"],
                    ClientName = "claude",
                    ClientLabel = "ci-probe",
                    Vault = Fixture.Path_,
                    Session = Session,
                },
                Token).AsTask();

        /// <summary>Appends a granted line a bridge labelled <paramref name="label"/> wrote for a vault.</summary>
        internal void Audit(string label, string vaultKey)
        {
            Assert.True(AuditLog.TryOpen(KeypasteHome.AuditPath(Fixture.Home), Clock, out var log, out var error), error);

            using (log)
            {
                Assert.True(log.TryAppend(
                    new AuditRecord
                    {
                        Tool = "request_credential",
                        Client = new AuditClient(label, null, label),
                        Args = AuditArgs.ForCredentialRequest("example", "password", 60, "deploy"),
                        Decision = AuditDecision.Granted,
                        Method = AuditMethod.Prompt,
                        Reason = "a person approved this request",
                        Exposure = ["**"],
                        Vault = vaultKey,
                    },
                    out var failure), failure);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _client.DisposeAsync();
            Authority.Dispose();
            Fixture.Dispose();
        }
    }

    private sealed class Person : IApprovalChannel
    {
        internal ApprovalAnswer Answer { get; set; } = ApprovalAnswer.Denied;

        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Answer);
    }
}
