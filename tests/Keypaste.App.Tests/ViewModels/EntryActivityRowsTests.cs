using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Tests;
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
    public async Task SortingByLastUsed_ReadsTimesNotWords_AndWhatWasNeverUsedStaysLast()
    {
        await using var app = await App.StartAsync();
        var key = app.Authority.Session.Identity!.Key;
        Assert.True(app.Authority.Session.Write(
            vault =>
            {
                vault.AddEntry(new VaultEntry { Title = "days" });
                vault.AddEntry(new VaultEntry { Title = "minutes" });
                vault.AddEntry(new VaultEntry { Title = "never" });
                return true;
            },
            _ => true).Problem("add them") is null);
        app.Audit("days", key, ago: TimeSpan.FromDays(3));
        app.Audit("minutes", key, ago: TimeSpan.FromMinutes(30));
        app.Person.Answer = ApprovalAnswer.ApprovedOnce;
        Assert.Equal(AuditMethod.Prompt, (await app.RequestAsync())!.Method);

        using var screen = app.Screen();
        var entries = screen.Entries;

        entries.Table.Press(TableStates.LastUsed);
        Assert.Equal(["example", "minutes", "days"], entries.Rows.Take(3).Select(row => row.Title));
        Assert.Equal("never", entries.Rows[^1].Title);

        entries.Table.Press(TableStates.LastUsed);
        Assert.Equal(["days", "minutes", "example"], entries.Rows.Take(3).Select(row => row.Title));
        Assert.Equal("never", entries.Rows[^1].Title);
    }

    /// <summary>
    /// The card belongs to an item agents can see: with nothing released and no bridge attached it is
    /// absent, a bridge whose agent's settings do not reach the item leaves it absent, and one whose
    /// settings do brings it, saying which agent can see the item (N.5).
    /// </summary>
    /// <summary>
    /// Of two items sharing a name, each row and card shows only what names its own UUID: a line naming the path alone,
    /// as every line did before the picker, is neither's, and one naming an item is that item's (F.76).
    /// </summary>
    [Fact]
    public async Task TwoItemsOfOneName_ShowOnlyWhatIsRecordedForEachItem()
    {
        await using var app = await App.StartAsync();
        var key = app.Authority.Session.Identity!.Key;
        Assert.True(app.Authority.Session.Write(
            vault =>
            {
                vault.AddEntry(new VaultEntry { Title = "example", Username = "second" });
                return true;
            },
            _ => true).Problem("add the second") is null);

        string second;
        using (var first = app.Screen())
        {
            second = first.Entries.Rows.Single(entry => entry.Path == "example" && entry.Username == "second").Uuid;
        }

        app.Audit("example", key, ago: TimeSpan.FromDays(3));
        app.Audit("example", key, ago: TimeSpan.FromMinutes(30), item: second);

        using var screen = app.Screen();
        var rows = screen.Entries.Rows.Where(entry => entry.Path == "example").ToList();
        var named = rows.Single(row => row.Uuid == second);
        var other = rows.Single(row => row.Uuid != second);

        Assert.Equal(("30m ago", true), (named.LastUsedText, named.IsRecent));
        Assert.Equal(("never", true), (other.LastUsedText, other.IsIdle));

        screen.Entries.Selected = named;
        Assert.True(screen.Entries.Detail!.SharesItsName);
        Assert.Single(screen.Entries.Detail.AgentAccess!.Clients);

        screen.Entries.Selected = other;
        Assert.Empty(screen.Entries.Detail!.AgentAccess!.Clients);
    }

    [Fact]
    public async Task TheAgentAccessCard_ShowsOnlyWhenAgentsCanSeeTheItem()
    {
        await using var app = await App.StartAsync();

        using (var screen = app.Screen())
        {
            screen.Entries.Selected = screen.Entries.Rows.Single(entry => entry.Path == "example");
            Assert.False(screen.Entries.Detail!.ShowsAgentAccess);
        }

        await using (await app.AttachBridgeAsync("no-settings"))
        {
            using var screen = app.Screen();
            screen.Entries.Selected = screen.Entries.Rows.Single(entry => entry.Path == "example");
            Assert.False(screen.Entries.Detail!.ShowsAgentAccess);
        }

        await using (await app.AttachBridgeAsync(TempVault.Agent))
        {
            using var screen = app.Screen();
            screen.Entries.Selected = screen.Entries.Rows.Single(entry => entry.Path == "example");
            Assert.True(screen.Entries.Detail!.ShowsAgentAccess);
            Assert.Equal("ci-probe can see it · nothing released", screen.Entries.Detail.AgentAccessSummary);
        }
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
            fixture.Sees();
            var clock = new ManualClock(AppClock.Start);
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
            var countdown = new ClipboardCountdown(new FakeClipboard(), new ManualClock(AppClock.Start));
            return new Screen(activity, countdown, new EntriesViewModel(Authority.Session, countdown, activity));
#pragma warning restore CA2000
        }

        /// <summary>A bridge attached as <paramref name="label"/>'s, announcing that it answers with every entry.</summary>
        internal async Task<ApproverClient> AttachBridgeAsync(string label)
        {
            var serving = Assert.IsType<AuthorityStatus.Serving>(Authority.Status);
            var bridge = await ApproverClient.TryConnectAsync(serving.Endpoint, _connect, Token);
            Assert.NotNull(bridge);
            var request = new AttachRequest(Fixture.Path_) { Client = new AttachClient("claude", "2.0", label), Exposure = ["**"] };
            Assert.True((await bridge.AttachAsync(request, Token))!.Attached);
            return bridge;
        }

        internal Task<CredentialReply?> RequestAsync() =>
            _client.RequestAsync(
                new CredentialRequest
                {
                    Entry = "example",
                    Field = "password",
                    Reason = "deploy",
                    Exposure = ["**"],
                    ClientName = "claude-code",
                    ClientLabel = "ci-probe",
                    Vault = Fixture.Path_,
                    Session = Session,
                },
                Token).AsTask();

        /// <summary>Appends a granted line written <paramref name="ago"/> before now, as a bridge wrote it then.</summary>
        internal void Audit(string entry, string vaultKey, string label = "ci-probe", TimeSpan ago = default, string? item = null)
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
                        Args = AuditArgs.ForCredentialRequest(entry, "password", "deploy") with { Item = item },
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
