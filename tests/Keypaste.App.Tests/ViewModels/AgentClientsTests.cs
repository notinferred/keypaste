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
/// Agents › AI tools: who reaches this vault, each on its row with its settings under its Settings
/// disclosure (D-0361, D-0455). Identities are display only, and settings follow the name a bridge was
/// started with, so an agent with no name has none of its own (T-36).
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
    public async Task Rows_ListAttachedAndSeenAgents_EachWithItsSettingsAndWhatTheyGive()
    {
        await using var app = await App.StartAsync(new AttachClient("claude", "2.0", "claude-code"));
        app.Audit("cursor", app.Authority.Session.Identity!.Key);
        app.Audit("elsewhere", "ffffffffffffffff");

        using var model = app.Model();

        // ci-probe has settings in the vault and has not asked.
        Assert.Equal(["Claude Code", "Cursor", "ci-probe", "Every other agent"], model.Rows.Select(row => row.Title));
        var attached = model.Rows[0];
        Assert.Equal(("claude-code", "cc", "in use now", true), (attached.Label, attached.Initials, attached.Status, attached.IsLive));
        Assert.Equal(("cursor", "cu", "last asked just now", false), (model.Rows[1].Label, model.Rows[1].Initials, model.Rows[1].Status, model.Rows[1].IsLive));
        Assert.Equal(("cp", "hasn't asked in the last 30 days"), (model.Rows[2].Initials, model.Rows[2].Status));
        Assert.Equal(("*", "Agents without settings of their own"), (model.Rows[^1].Initials, model.Rows[^1].Status));
        Assert.All(model.Rows, row => Assert.True(row.HasSettings));
        Assert.Equal(
            ["Ask, allow for a while · sees all projects", "Ask, allow for a while · sees all projects", "Ask, allow for a while · sees every item · may run commands", "Ask, allow for a while · sees all projects"],
            model.Rows.Select(row => row.Summary));
        Assert.All(model.Rows, row => Assert.False(row.OffersConnect));
        Assert.All(model.Rows, row => Assert.Null(row.Settings));
    }

    [Fact]
    public async Task Settings_OpenOnTheirRows_SeveralAtOnce_AndCloseAgain()
    {
        await using var app = await App.StartAsync(new AttachClient("claude", "2.0", "ci-probe"));
        using var model = app.Model();
        var row = model.Rows.Single(row => row.Label == "ci-probe");
        var any = model.Rows.Single(row => row.Label == AgentNames.Any);

        row.IsOpen = true;
        any.IsOpen = true;

        Assert.Equal("ci-probe", row.Settings!.Agent);
        Assert.Equal(AgentNames.Any, any.Settings!.Agent);
        Assert.True(row.IsOpen);

        row.IsOpen = false;

        Assert.False(row.IsOpen);
        Assert.True(any.IsOpen);
    }

    /// <summary>
    /// The Always for notice opens an agent's settings by name, as its row's Settings does, and still when no
    /// row shows that agent yet, which then gets one; a second open leaves the same settings open.
    /// </summary>
    [Fact]
    public async Task Settings_OpenByName_WithOrWithoutARow()
    {
        await using var app = await App.StartAsync(new AttachClient("claude", "2.0", "ci-probe"));
        using var model = app.Model();
        var row = model.Rows.Single(row => row.Label == "ci-probe");

        model.OpenSettings("ci-probe");
        var open = row.Settings;
        model.OpenSettings("ci-probe");

        Assert.Equal("ci-probe", open!.Agent);
        Assert.Same(open, row.Settings);
        Assert.True(row.IsOpen);

        model.OpenSettings("codex");

        var codex = model.Rows.Single(row => row.Label == "codex");
        Assert.True(codex.IsOpen);
        Assert.Equal("codex", codex.Settings!.Agent);
        Assert.True(row.IsOpen);
        Assert.Equal(AgentNames.Any, model.Rows[^1].Label);
    }

    /// <summary>The rows are read again whenever an agent is seen again, and each keeps its open settings, so no unsaved edit is dropped.</summary>
    [Fact]
    public async Task OpenSettings_StayOpen_WhenTheRowsAreReadAgain()
    {
        await using var app = await App.StartAsync(new AttachClient("claude", "2.0", "ci-probe"));
        using var model = app.Model();
        var row = model.Rows.Single(row => row.Label == "ci-probe");
        row.IsOpen = true;
        var open = row.Settings;

        app.Audit("cursor", app.Authority.Session.Identity!.Key);
        model.Refresh();
        app.Clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Same(row, model.Rows.Single(listed => listed.Label == "ci-probe"));
        Assert.Same(open, row.Settings);
        Assert.True(row.IsOpen);
        Assert.False(model.Rows.Single(listed => listed.Label == "cursor").IsOpen);
    }

    [Fact]
    public async Task AnAgentWithNoName_HasNoSettingsOfItsOwn()
    {
        await using var app = await App.StartAsync(new AttachClient("Zed", null, null));

        using var model = app.Model();
        var row = model.Rows.Single(row => row.Title == "Zed");

        Assert.False(row.HasSettings);
        Assert.Equal("?", row.Initials);
        Assert.Contains("Every other agent's settings apply", row.Status, StringComparison.Ordinal);

        row.IsOpen = true;

        Assert.False(row.IsOpen);
        Assert.Null(row.Settings);
        Assert.Empty(row.Summary);
    }

    [Fact]
    public async Task ALabelNoSettingsCanBeKeptUnder_HasNone()
    {
        await using var app = await App.StartAsync(null);
        app.Audit("old\"bridge", app.Authority.Session.Identity!.Key);

        using var model = app.Model();
        var row = model.Rows.Single(row => row.Key == "old\"bridge");

        Assert.False(row.HasSettings);
        Assert.Null(row.Label);
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
