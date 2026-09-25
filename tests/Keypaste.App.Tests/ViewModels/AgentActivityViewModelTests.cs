using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// Agent Activity lists what the app's session authority holds and what the audit file says this
/// session answered, over the app's real endpoint, and says so when either cannot be read.
/// </summary>
public sealed class AgentActivityViewModelTests
{
    private static readonly TimeSpan _connect = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_request_in_front_of_a_person_is_listed_and_counts_down()
    {
        await using var app = await App.StartAsync();
        app.Person.Hold = true;

        var pending = app.RequestAsync();
        await app.Person.Waiting.WaitAsync(_connect, Token);

        using var model = app.Model();

        var waiting = Assert.Single(model.Waiting);
        Assert.Equal("example · password", waiting.What);
        Assert.Equal("claude-code · label ci-probe", waiting.Who);
        Assert.Equal("answered for you in 45 s", waiting.Left);
        Assert.Empty(model.Grants);
        Assert.True(model.IsAvailable);

        app.Clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal("answered for you in 40 s", Assert.Single(model.Waiting).Left);

        app.Clock.Advance(TimeSpan.FromSeconds(40));
        await pending.WaitAsync(_connect, Token);
        model.Refresh();

        Assert.Empty(model.Waiting);
        Assert.True(model.NothingWaiting);
    }

    [Fact]
    public async Task An_approved_request_is_listed_as_a_grant_with_its_remaining_lifetime_and_no_value()
    {
        await using var app = await App.StartAsync();
        app.Person.Answer = ApprovalAnswer.Approved;

        var reply = await app.RequestAsync();
        Assert.Equal(AuditMethod.Prompt, reply!.Method);

        using var model = app.Model();

        var grant = Assert.Single(model.Grants);
        Assert.Equal("example · password", grant.What);
        Assert.Equal("claude-code · label ci-probe", grant.Who);
        Assert.Equal("ends in 3600 s", grant.Left);
        Assert.NotNull(grant.Grant);
        Assert.True(model.RevokeAllCommand.CanExecute(null));

        app.Clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal("ends in 3590 s", Assert.Single(model.Grants).Left);
        Assert.DoesNotContain(reply.Value!, Everything(model), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Revoke_ends_the_grant_in_the_authority_so_the_same_request_is_asked_again(bool all)
    {
        await using var app = await App.StartAsync();
        app.Person.Answer = ApprovalAnswer.Approved;

        await app.RequestAsync();
        Assert.Equal(AuditMethod.GrantCache, (await app.RequestAsync())!.Method);

        using var model = app.Model();

        if (all)
        {
            model.RevokeAllCommand.Execute(null);
        }
        else
        {
            model.RevokeCommand.Execute(Assert.Single(model.Grants));
        }

        Assert.Empty(model.Grants);
        Assert.Empty(app.Authority.Activity.Grants);
        Assert.False(model.RevokeAllCommand.CanExecute(null));

        var again = await app.RequestAsync();

        Assert.Equal(AuditMethod.Prompt, again!.Method);
        Assert.Equal(2, app.Person.Asked);
    }

    [Fact]
    public async Task History_is_the_audit_records_naming_this_session()
    {
        await using var app = await App.StartAsync();
        app.Audit("env/other/FROM_BEFORE", "an-earlier-session");
        app.Audit("example", app.Session);

        using var model = app.Model();

        Assert.False(model.HasHistoryMessage);
        Assert.Contains($"1 record of 2 in {model.AuditPath}, session {app.Session}", model.History, StringComparison.Ordinal);
        Assert.Contains("example", model.History, StringComparison.Ordinal);
        Assert.DoesNotContain("FROM_BEFORE", model.History, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_record_written_while_the_screen_is_open_appears_at_the_next_tick()
    {
        await using var app = await App.StartAsync();
        app.Audit("example", app.Session);

        using var model = app.Model();
        app.Audit("example", app.Session);

        app.Clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Contains("2 records of 2", model.History, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_log_is_unavailable_rather_than_a_session_with_no_history()
    {
        await using var app = await App.StartAsync();

        using var model = app.Model();

        Assert.False(model.HasHistory);
        Assert.Equal($"History unavailable: there is no audit log at {model.AuditPath}.", model.HistoryMessage);
    }

    [Fact]
    public async Task An_unreadable_log_is_unavailable()
    {
        await using var app = await App.StartAsync();
        Directory.CreateDirectory(KeypasteHome.AuditPath(app.Fixture.Home));

        using var model = app.Model();

        Assert.False(model.HasHistory);
        Assert.StartsWith("History unavailable: the audit log couldn't be read", model.HistoryMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_lock_empties_both_lists_because_the_authority_holds_nothing()
    {
        await using var app = await App.StartAsync();
        app.Person.Answer = ApprovalAnswer.Approved;
        await app.RequestAsync();

        using var model = app.Model();
        Assert.Single(model.Grants);

        app.Authority.Session.Lock(VaultLockReason.Manual);
        model.Refresh();

        Assert.Same(ApproverActivity.None, app.Authority.Activity);
        Assert.Empty(model.Grants);
        Assert.Empty(model.Waiting);
        Assert.True(model.IsUnavailable);
        Assert.False(model.NoGrants);
        Assert.Equal("History can't be shown, because this app is not answering agents for this vault.", model.HistoryMessage);
    }

    [Fact]
    public void Without_an_authority_nothing_is_read_and_the_screen_says_so()
    {
        using var home = new TempAuditHome();
        using var model = new AgentActivityViewModel(null, home.Home, new ManualClock());

        Assert.True(model.IsUnavailable);
        Assert.Empty(model.Waiting);
        Assert.Empty(model.Grants);
        Assert.False(model.NothingWaiting);
    }

    [Fact]
    public async Task A_disposed_screen_stops_reading()
    {
        await using var app = await App.StartAsync();
        app.Person.Answer = ApprovalAnswer.Approved;
        await app.RequestAsync();

        var model = app.Model();
        var before = Assert.Single(model.Grants);
        model.Dispose();

        app.Clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Same(before, Assert.Single(model.Grants));
    }

    private static string Everything(AgentActivityViewModel model) =>
        string.Join(
            '\n',
            [
                model.Status, model.Unavailable, model.History, model.HistoryMessage,
                .. model.Waiting.Concat(model.Grants).Select(row => $"{row.Who} {row.What} {row.Left}"),
            ]);

    /// <summary>The app's authority on a manual clock with a scripted person, attached to over its real endpoint.</summary>
    private sealed class App : IAsyncDisposable
    {
        private readonly ApproverClient _client;

        private App(TempVault fixture, ManualClock clock, ScriptedPerson person, AppAuthority authority, ApproverClient client, string session)
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

        internal ScriptedPerson Person { get; }

        internal AppAuthority Authority { get; }

        internal string Session { get; }

        internal static async Task<App> StartAsync()
        {
            var fixture = new TempVault();
            var clock = new ManualClock();
            var person = new ScriptedPerson();
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
            var attached = await client.AttachAsync(new AttachRequest(fixture.Path_), Token);
            Assert.Equal(serving.Session, attached!.Session);

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
                    ClientName = "claude-code",
                    ClientLabel = "ci-probe",
                    Vault = Fixture.Path_,
                    Session = Session,
                },
                Token).AsTask();

        /// <summary>Appends a prompted grant naming <paramref name="session"/>, as keypaste-mcp writes one.</summary>
        internal void Audit(string entry, string session)
        {
            Assert.True(AuditLog.TryOpen(KeypasteHome.AuditPath(Fixture.Home), Clock, out var log, out var error), error);

            using (log)
            {
                var record = new AuditRecord
                {
                    Tool = "request_credential",
                    Client = new AuditClient("claude-code", null, "ci-probe"),
                    Args = AuditArgs.ForCredentialRequest(entry, "password", 60, "deploy"),
                    Decision = AuditDecision.Granted,
                    Method = AuditMethod.Prompt,
                    Reason = "a person approved this request",
                    Exposure = ["**"],
                    Session = session,
                };

                Assert.True(log.TryAppend(record, out var failure), failure);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _client.DisposeAsync();
            Authority.Dispose();
            Fixture.Dispose();
        }
    }

    /// <summary>A person who answers as told, or who has not answered yet.</summary>
    private sealed class ScriptedPerson : IApprovalChannel
    {
        private readonly TaskCompletionSource _waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal ApprovalAnswer Answer { get; set; } = ApprovalAnswer.Denied;

        internal bool Hold { get; set; }

        internal int Asked { get; private set; }

        internal Task Waiting => _waiting.Task;

        public async ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken)
        {
            Asked++;

            if (!Hold)
            {
                return Answer;
            }

            _waiting.TrySetResult();

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Withdrawn, which is the only way a held prompt ends.
            }

            return ApprovalAnswer.Denied;
        }
    }
}
