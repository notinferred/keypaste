using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// The Secrets rows' dot and last-used time, and the detail's Agent access card, read from this
/// vault's audit lines and what the authority holds now (D-0361). Never a value.
/// </summary>
public sealed class EntryActivityRowsTests
{
    private static readonly TimeSpan _connect = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARowWithAGrant_IsInUse_AndTheCardSaysWhose()
    {
        await using var app = await App.StartAsync();
        app.Person.Answer = ApprovalAnswer.Approved;
        Assert.Equal(AuditMethod.Prompt, (await app.RequestAsync())!.Method);

        using var screen = app.Screen();
        var row = screen.Entries.Rows.Single(entry => entry.Path == "example");

        Assert.True(row.IsInUse);
        Assert.Equal("in use", row.LastUsedText);

        screen.Entries.Selected = row;
        Assert.Equal("claude-code · grant, 1h left", screen.Entries.Detail!.AgentAccessSummary);
        Assert.Equal("in use", screen.Entries.Detail.LastUsedText);
        Assert.Single(screen.Entries.Detail.AgentAccess!.Grants);
    }

    [Theory]
    [InlineData(30, "30m ago", true)]
    [InlineData(180, "3h ago", false)]
    public async Task LastUsedText_FollowsThisVaultsGrantedLines(int minutesAgo, string text, bool recent)
    {
        await using var app = await App.StartAsync();
        app.Audit("example", app.Authority.Session.Identity!.Key, ago: TimeSpan.FromMinutes(minutesAgo));

        using var screen = app.Screen();
        var row = screen.Entries.Rows.Single(entry => entry.Path == "example");

        Assert.Equal(recent, row.IsRecent);
        Assert.Equal(!recent, row.IsIdle);
        Assert.Equal(text, row.LastUsedText);
    }

    [Fact]
    public async Task TheAgentAccessCard_ListsClientsNewestFirst()
    {
        await using var app = await App.StartAsync();
        app.Audit("example", app.Authority.Session.Identity!.Key, label: "cursor", ago: TimeSpan.FromMinutes(15));
        app.Audit("example", app.Authority.Session.Identity.Key, label: "claude-code", ago: TimeSpan.FromMinutes(5));
        app.Audit("example", "ffffffffffffffff", label: "elsewhere", ago: TimeSpan.FromMinutes(1));

        using var screen = app.Screen();
        screen.Entries.Selected = screen.Entries.Rows.Single(entry => entry.Path == "example");
        var access = screen.Entries.Detail!.AgentAccess!;

        Assert.Equal(["claude-code", "cursor"], access.Clients.Select(client => client.Client));

        // Two clients are listed one per line, so the summary that would name the first again steps
        // aside, and the time is said once, on the Last used line.
        Assert.Equal("claude-code", screen.Entries.Detail.AgentAccessSummary);
        Assert.False(screen.Entries.Detail.ShowsAgentAccessSummary);
        Assert.Equal(["claude-code · 5m ago · 1×", "cursor · 15m ago · 1×"], screen.Entries.Detail.AgentLines);
        Assert.Equal("5m ago", screen.Entries.Detail.LastUsedText);
    }

    private sealed class Screen(EntryActivitySource activity, ClipboardCountdown countdown, EntriesViewModel entries) : IDisposable
    {
        internal EntryActivitySource Activity { get; } = activity;

        internal EntriesViewModel Entries { get; } = entries;

        public void Dispose()
        {
            Entries.Dispose();
            countdown.Dispose();
            Activity.Dispose();
        }
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

        internal static async Task<App> StartAsync()
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
            Assert.True((await client.AttachAsync(new AttachRequest(fixture.Path_), Token))!.Attached);

            return new App(fixture, clock, person, authority, client, serving.Session);
        }

        internal Screen Screen()
        {
#pragma warning disable CA2000
            var activity = new EntryActivitySource(Authority, KeypasteHome.AuditPath(Fixture.Home), Authority.Session.Identity!.Key, Clock);
            var countdown = new ClipboardCountdown(new FakeClipboard(), new ManualClock());
            return new Screen(activity, countdown, new EntriesViewModel(Authority.Session, countdown, activity));
#pragma warning restore CA2000
        }

        internal Task<CredentialReply?> RequestAsync() =>
            _client.RequestAsync(
                new CredentialRequest
                {
                    Entry = "example",
                    Field = "password",
                    Reason = "deploy",
                    TtlSeconds = 60,
                    Exposure = ["**"],
                    ClientName = "claude-code",
                    ClientLabel = "ci-probe",
                    Vault = Fixture.Path_,
                    Session = Session,
                },
                Token).AsTask();

        /// <summary>Appends a granted line written <paramref name="ago"/> before now, as a bridge wrote it then.</summary>
        internal void Audit(string entry, string vaultKey, string label = "ci-probe", TimeSpan ago = default)
        {
            var then = new ManualClock(Clock.GetUtcNow() - ago);
            Assert.True(AuditLog.TryOpen(KeypasteHome.AuditPath(Fixture.Home), then, out var log, out var error), error);

            using (log)
            {
                Assert.True(log.TryAppend(
                    new AuditRecord
                    {
                        Tool = "request_credential",
                        Client = new AuditClient("some-client", null, label),
                        Args = AuditArgs.ForCredentialRequest(entry, "password", 60, "deploy"),
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
