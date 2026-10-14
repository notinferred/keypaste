using Keypaste.App.Session;
using Keypaste.App.Tests.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// Agents › MCP clients: who reaches this vault, and each one's settings opened from its card below the
/// cards (D-0361). Identities are display only, and settings follow the label a client's bridge was
/// started with, so an unlabeled client has none of its own.
/// </summary>
public sealed class AgentClientsTests
{
    private static readonly TimeSpan _connect = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static AccessCaps Caps(string path)
    {
        Assert.True(AccessCaps.TryLoad(path, out var caps, out var problem), problem);
        return caps;
    }

    [Fact]
    public async Task Cards_ListConnectedAndSeenClients_EachWithItsSettings()
    {
        await using var app = await App.StartAsync(new AttachClient("claude", "2.0", "claude-code"));
        app.Audit("cursor", app.Authority.Session.Identity!.Key);
        app.Audit("elsewhere", "ffffffffffffffff");

        using var model = app.Model();

        Assert.Equal(["Claude Code", "Cursor", "Every other client"], model.Clients.Select(card => card.Name));
        var connected = model.Clients[0];
        Assert.Equal(("claude-code", "cc", "Connected", "Claude Code · MCP stdio"), (connected.Title, connected.Initials, connected.Status, connected.Detail));
        Assert.Equal(("cursor", "cu", "Idle"), (model.Clients[1].Title, model.Clients[1].Initials, model.Clients[1].Status));
        Assert.All(model.Clients, card => Assert.True(card.HasSettings));
        Assert.False(model.HasSettings);
    }

    [Fact]
    public async Task Settings_OpenBelowTheCards_OneAgentAtATime_AndCloseOnASecondPress()
    {
        await using var app = await App.StartAsync(new AttachClient("claude", "2.0", "ci-probe"));
        using var model = app.Model();
        var card = model.Clients.Single(row => row.Label == "ci-probe");
        var any = model.Clients.Single(row => row.Label == AgentNames.Any);

        model.ToggleSettingsCommand.Execute(card);

        Assert.Equal("ci-probe", model.Settings!.Agent);
        Assert.True(card.IsSettingsOpen);
        Assert.Equal("chevron-down", card.SettingsIcon);

        model.ToggleSettingsCommand.Execute(any);

        Assert.Equal(AgentNames.Any, model.Settings!.Agent);
        Assert.False(card.IsSettingsOpen);
        Assert.True(any.IsSettingsOpen);

        model.ToggleSettingsCommand.Execute(any);

        Assert.Null(model.Settings);
        Assert.False(any.IsSettingsOpen);
    }

    /// <summary>
    /// The Always for notice opens an agent's settings by name, as its card's button does, and still when
    /// no card shows that agent yet; a second open of the same agent leaves them open.
    /// </summary>
    [Fact]
    public async Task Settings_OpenByName_WithOrWithoutACard()
    {
        await using var app = await App.StartAsync(new AttachClient("claude", "2.0", "ci-probe"));
        using var model = app.Model();
        var card = model.Clients.Single(row => row.Label == "ci-probe");

        model.OpenSettings("ci-probe");
        var open = model.Settings;
        model.OpenSettings("ci-probe");

        Assert.Equal("ci-probe", open!.Agent);
        Assert.Same(open, model.Settings);
        Assert.True(card.IsSettingsOpen);

        model.OpenSettings("codex");

        Assert.Equal("codex", model.Settings!.Agent);
        Assert.False(card.IsSettingsOpen);
    }

    /// <summary>The cards are rebuilt whenever a client is seen again, and the settings open stay open on the new card.</summary>
    [Fact]
    public async Task OpenSettings_StayOpen_WhenTheCardsAreBuiltAgain()
    {
        await using var app = await App.StartAsync(new AttachClient("claude", "2.0", "ci-probe"));
        using var model = app.Model();
        model.ToggleSettingsCommand.Execute(model.Clients.Single(row => row.Label == "ci-probe"));
        var open = model.Settings;

        app.Audit("cursor", app.Authority.Session.Identity!.Key);
        model.Refresh();

        Assert.Same(open, model.Settings);
        Assert.True(model.Clients.Single(row => row.Label == "ci-probe").IsSettingsOpen);
        Assert.False(model.Clients.Single(row => row.Label == "cursor").IsSettingsOpen);
    }

    [Fact]
    public async Task AnUnlabeledClient_HasNoSettingsOfItsOwn()
    {
        await using var app = await App.StartAsync(new AttachClient("Zed", null, null));

        using var model = app.Model();
        var card = model.Clients.Single(row => row.Name == "Zed");

        Assert.False(card.HasSettings);
        Assert.Contains("--client-label", card.Hint, StringComparison.Ordinal);
        Assert.False(model.ToggleSettingsCommand.CanExecute(card));

        model.ToggleSettingsCommand.Execute(card);

        Assert.Null(model.Settings);
    }

    [Fact]
    public async Task ALabelNoSettingsCanBeKeptUnder_HasNone()
    {
        await using var app = await App.StartAsync(null);
        app.Audit("old\"bridge", app.Authority.Session.Identity!.Key);

        using var model = app.Model();
        var card = model.Clients.Single(row => row.Label == "old\"bridge");

        Assert.False(card.HasSettings);
        Assert.False(model.ToggleSettingsCommand.CanExecute(card));
    }

    /// <summary>
    /// A file in the form before agents had settings is malformed now: it refuses every agent request, and
    /// Reset clears it only once the person confirms the reset in the window that confirms any change
    /// giving agents more, keeping the file beside it under a name no earlier copy holds, which it names;
    /// refused, the file stays as it was.
    /// </summary>
    [Fact]
    public async Task AMalformedFile_IsResetOnlyOnceThePersonConfirms()
    {
        const string Malformed = "[[client]]\nlabel = \"x\"\npolicy = \"ask\"\n";
        const string Later = "[[client]]\nlabel = \"y\"\npolicy = \"ask\"\n";
        var person = new Confirming();
        await using var app = await App.StartAsync(null, person);
        File.WriteAllText(app.ClientsPath, Malformed);
        List<string> toasts = [];

        using var model = new AgentActivityViewModel(app.Authority, app.Fixture.Home, app.Clock, toast: toasts.Add);

        Assert.True(model.HasClientsProblem);
        Assert.Contains("refused", model.ClientsProblem, StringComparison.Ordinal);

        model.ResetClientsCommand.Execute(null);
        await model.Resetting;

        Assert.True(Assert.Single(person.Asked).ResetsCeilings);
        Assert.True(model.HasClientsProblem);
        Assert.Equal(Malformed, File.ReadAllText(app.ClientsPath));

        person.Answer = ApprovalAnswer.ApprovedOnce;
        model.ResetClientsCommand.Execute(null);
        await model.Resetting;

        Assert.False(model.HasClientsProblem);
        Assert.Empty(Caps(app.ClientsPath).Rows);
        Assert.Equal(Malformed, File.ReadAllText(app.ClientsPath + AccessCaps.KeptSuffix));
        Assert.Contains($"was set aside as clients.toml{AccessCaps.KeptSuffix},", toasts[^1], StringComparison.Ordinal);

        File.WriteAllText(app.ClientsPath, Later);
        model.Refresh();
        model.ResetClientsCommand.Execute(null);
        await model.Resetting;

        Assert.Equal(Malformed, File.ReadAllText(app.ClientsPath + AccessCaps.KeptSuffix));
        Assert.Equal(Later, File.ReadAllText(app.ClientsPath + AccessCaps.KeptSuffix + "-2"));
        Assert.Contains($"was set aside as clients.toml{AccessCaps.KeptSuffix}-2,", toasts[^1], StringComparison.Ordinal);
    }

    private sealed class App : IAsyncDisposable
    {
        private readonly ApproverClient _client;

        private App(TempVault fixture, ManualClock clock, AppAuthority authority, ApproverClient client)
        {
            Fixture = fixture;
            Clock = clock;
            Authority = authority;
            _client = client;
        }

        internal TempVault Fixture { get; }

        internal ManualClock Clock { get; }

        internal AppAuthority Authority { get; }

        internal string ClientsPath => KeypasteHome.ClientsPath(Fixture.Home);

        internal static async Task<App> StartAsync(AttachClient? identity, IApprovalChannel? person = null)
        {
            var fixture = new TempVault();
            fixture.Sees();
            var clock = new ManualClock(AppClock.Start);
#pragma warning disable CA2000
            var authority = new AppAuthority(new AppVaultSession(clock, home: fixture.Home), null, () => person ?? new NobodyToAsk());
#pragma warning restore CA2000

            using (var master = TempVault.Secret(TempVault.Password))
            {
                Assert.Equal(UnlockOutcome.Opened, authority.Session.TryUnlock(fixture.Path_, master.Value));
            }

            var serving = Assert.IsType<AuthorityStatus.Serving>(authority.Status);
            var client = await ApproverClient.TryConnectAsync(serving.Endpoint, _connect, Token);
            Assert.NotNull(client);
            Assert.True((await client.AttachAsync(new AttachRequest(fixture.Path_) { Client = identity }, Token))!.Attached);

            return new App(fixture, clock, authority, client);
        }

        internal AgentActivityViewModel Model() => new(Authority, Fixture.Home, Clock);

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

    /// <summary>The person at the app: answers each confirmation as the test says, and keeps what it was shown.</summary>
    private sealed class Confirming : IApprovalChannel
    {
        internal ApprovalAnswer Answer { get; set; } = ApprovalAnswer.Denied;

        internal List<AccessChangePrompt> Asked { get; } = [];

        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ApprovalAnswer.Denied);

        public ValueTask<ApprovalAnswer> AskAsync(AccessChangePrompt prompt, CancellationToken cancellationToken)
        {
            Asked.Add(prompt);
            return ValueTask.FromResult(Answer);
        }
    }
}
