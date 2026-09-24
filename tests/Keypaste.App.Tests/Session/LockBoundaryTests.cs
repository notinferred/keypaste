using Keypaste.App.Session;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Xunit;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// Every kind of lock the app takes is the one transition that withdraws what an agent has waiting,
/// zeroes its grants and refuses what has not been released, over the app's real endpoint (D-0313).
/// </summary>
public sealed class LockBoundaryTests
{
    private static readonly TimeSpan _connect = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // Quitting is AppAuthorityTests', through the composition launch runs.
    public static TheoryData<string> Locks => ["manual", "minimized", "idle"];

    [Theory]
    [MemberData(nameof(Locks))]
    public async Task A_request_waiting_for_a_person_is_denied_as_locked_by_every_kind_of_lock(string kind)
    {
        using var fixture = new TempVault();
        var clock = new ManualClock();
        var prompt = new ScriptedPrompt { Hold = true };
        using var session = new AppVaultSession(clock, home: fixture.Home);
        using var host = new SessionHost(session, approverOverride: null, () => prompt);
        Unlock(session, fixture.Path_);

        await using var client = await ConnectAsync(host);
        var attached = await client.AttachAsync(new AttachRequest(fixture.Path_), Token);
        var pending = client.RequestAsync(Request(fixture.Path_, attached!.Session!), Token).AsTask();
        await prompt.Waiting.WaitAsync(_connect, Token);

        switch (kind)
        {
            case "manual":
                session.Lock(VaultLockReason.Manual);
                break;
            case "minimized":
                session.Lock(VaultLockReason.Minimized);
                break;
            case "idle":
                clock.Advance(AppVaultSession.DefaultIdleTimeout);
                break;
        }

        var reply = await pending.WaitAsync(_connect, Token);

        Assert.False(session.IsUnlocked);
        Assert.NotNull(reply);
        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Equal(AuditMethod.VaultLocked, reply.Method);
        Assert.Equal(attached.Session, reply.Session);
        Assert.Null(reply.Value);
        Assert.True(prompt.Withdrawn);
    }

    [Fact]
    public async Task A_request_after_sleeping_past_the_deadline_is_refused_and_locks_the_vault()
    {
        using var fixture = new TempVault();
        var clock = new ManualClock();
        var prompt = new ScriptedPrompt { Answer = ApprovalAnswer.Approved };
        using var session = new AppVaultSession(clock, home: fixture.Home);
        using var host = new SessionHost(session, approverOverride: null, () => prompt);
        Unlock(session, fixture.Path_);

        await using var client = await ConnectAsync(host);
        var attached = await client.AttachAsync(new AttachRequest(fixture.Path_), Token);

        clock.AdvanceWallOnly(AppVaultSession.DefaultIdleTimeout + TimeSpan.FromMinutes(1));
        var reply = await client.RequestAsync(Request(fixture.Path_, attached!.Session!), Token);

        Assert.NotNull(reply);
        Assert.Equal(AuditMethod.VaultLocked, reply.Method);
        Assert.Null(reply.Value);
        Assert.Equal(0, prompt.Asked);
        await WaitUntilAsync(() => !session.IsUnlocked);
    }

    [Fact]
    public async Task Agent_requests_do_not_move_the_idle_deadline()
    {
        using var fixture = new TempVault();
        var clock = new ManualClock();
        using var session = new AppVaultSession(clock, home: fixture.Home);
        using var host = new SessionHost(session, approverOverride: null, () => new NobodyToAsk());
        Unlock(session, fixture.Path_);

        await using var client = await ConnectAsync(host);
        var attached = await client.AttachAsync(new AttachRequest(fixture.Path_), Token);
        var id = attached!.Session!;

        for (var minute = 1; minute < AppVaultSession.DefaultIdleTimeout.TotalMinutes; minute++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));

            var listing = await client.ListAsync(new NamesRequest(["**"]) { Vault = fixture.Path_, Session = id }, Token);
            var refused = await client.RequestAsync(Request(fixture.Path_, id), Token);

            Assert.True(listing!.VaultUnlocked);
            Assert.Equal(AuditMethod.NoApprover, refused!.Method);
        }

        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.False(session.IsUnlocked);
    }

    [Fact]
    public async Task The_next_unlock_starts_with_no_grant_from_before_the_lock()
    {
        using var fixture = new TempVault();
        var prompt = new ScriptedPrompt { Answer = ApprovalAnswer.Approved };
        using var session = new AppVaultSession(new ManualClock(), home: fixture.Home);
        using var host = new SessionHost(session, approverOverride: null, () => prompt);
        Unlock(session, fixture.Path_);

        await using (var client = await ConnectAsync(host))
        {
            var attached = await client.AttachAsync(new AttachRequest(fixture.Path_), Token);
            var first = await client.RequestAsync(Request(fixture.Path_, attached!.Session!), Token);
            var reused = await client.RequestAsync(Request(fixture.Path_, attached.Session!), Token);

            Assert.Equal(AuditMethod.Prompt, first!.Method);
            Assert.Equal(AuditMethod.GrantCache, reused!.Method);
        }

        session.Lock(VaultLockReason.Manual);
        Unlock(session, fixture.Path_);
        prompt.Answer = ApprovalAnswer.Denied;

        await using var after = await ConnectAsync(host);
        var reattached = await after.AttachAsync(new AttachRequest(fixture.Path_), Token);
        var again = await after.RequestAsync(Request(fixture.Path_, reattached!.Session!), Token);

        Assert.Equal(AuditDecision.Denied, again!.Decision);
        Assert.Equal(AuditMethod.Prompt, again.Method);
        Assert.Equal(2, prompt.Asked);
    }

    [Fact]
    public void A_lifetime_that_ended_cannot_reach_the_vault_a_later_unlock_opened()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock(), home: fixture.Home);
        Unlock(session, fixture.Path_);
        var before = session.Lifetime!;

        session.Lock(VaultLockReason.Manual);
        Unlock(session, fixture.Path_);

        Assert.False(before.IsLive);
        Assert.Null(session.UnlockedFor(before));
        Assert.NotNull(session.UnlockedFor(session.Lifetime!));
    }

    private static CredentialRequest Request(string vault, string session) => new()
    {
        Entry = "example",
        Field = "password",
        Reason = "deploy",
        TtlSeconds = 60,
        Exposure = ["**"],
        Vault = vault,
        Session = session,
    };

    private static void Unlock(AppVaultSession session, string path)
    {
        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(path, master.Value));
    }

    private static async Task<ApproverClient> ConnectAsync(SessionHost host)
    {
        Assert.NotNull(host.Endpoint);
        var client = await ApproverClient.TryConnectAsync(host.Endpoint, _connect, Token);
        Assert.NotNull(client);
        return client;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + _connect;

        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "the condition never held");
            await Task.Delay(20, Token);
        }
    }

    /// <summary>A person who answers as told, or who has not answered yet.</summary>
    private sealed class ScriptedPrompt : IApprovalChannel
    {
        private readonly TaskCompletionSource _waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal ApprovalAnswer Answer { get; set; } = ApprovalAnswer.Denied;

        internal bool Hold { get; init; }

        internal int Asked { get; private set; }

        internal bool Withdrawn { get; private set; }

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
                Withdrawn = true;
            }

            return ApprovalAnswer.Denied;
        }
    }
}
