using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// The app starts, holds and ends its session authority, and says what agents meet as the authority
/// answering them says it, over the app's real endpoint (4.4b).
/// </summary>
public sealed class AppAuthorityTests
{
    private static readonly TimeSpan _connect = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _absent = TimeSpan.FromMilliseconds(300);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_status_follows_the_authority_from_launch_through_lock_and_quit()
    {
        using var fixture = new TempVault();
        var authority = Launch(fixture);

        try
        {
            Assert.IsType<AuthorityStatus.Locked>(authority.Status);

            Unlock(authority.Session, fixture.Path_);
            var first = await AssertServedAsync(authority, fixture.Path_);

            authority.Session.Lock(VaultLockReason.Manual);
            Assert.IsType<AuthorityStatus.Locked>(authority.Status);
            await AssertAbsentAsync(first.Endpoint);
            AssertNobodyHolds(fixture);

            Unlock(authority.Session, fixture.Path_);
            var second = await AssertServedAsync(authority, fixture.Path_);
            Assert.NotEqual(first.Session, second.Session);

            authority.Dispose();
            Assert.IsType<AuthorityStatus.Locked>(authority.Status);
            await AssertAbsentAsync(second.Endpoint);
            AssertNobodyHolds(fixture);
        }
        finally
        {
            authority.Dispose();
        }
    }

    [Fact]
    public void Past_the_idle_deadline_the_status_is_not_serving_although_the_listener_is_up()
    {
        using var fixture = new TempVault();
        var clock = new ManualClock();
        using var authority = Launch(fixture, clock);
        Unlock(authority.Session, fixture.Path_);
        Assert.IsType<AuthorityStatus.Serving>(authority.Status);

        clock.AdvanceWallOnly(AppVaultSession.DefaultIdleTimeout + TimeSpan.FromMinutes(1));

        Assert.IsNotType<AuthorityStatus.Serving>(authority.Status);
    }

    [Fact]
    public void An_endpoint_that_cannot_be_named_is_reported_while_the_vault_is_unlocked()
    {
        using var fixture = new TempVault();
        using var authority = Launch(fixture, approverOverride: "not/a/pipe");
        Unlock(authority.Session, fixture.Path_);

        var status = Assert.IsType<AuthorityStatus.NotServing>(authority.Status);
        Assert.Contains("cannot name a pipe", status.Reason, StringComparison.Ordinal);
        Assert.StartsWith("Agents cannot reach this vault:", AgentActivityViewModel.Describe(status), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_vault_keypaste_agent_holds_is_refused_and_the_status_names_the_agent()
    {
        using var fixture = new TempVault();
        using var authority = Launch(fixture);
        var agent = $"keypaste agent (process {Environment.ProcessId})";
        var unlocked = 0;
        using var model = new UnlockViewModel(authority.Session, fixture.Home, new FakeVaultFilePicker(), () => unlocked++);
        Assert.True(model.Offer(fixture.Path_));

        Assert.True(VaultClaim.TryAcquire(fixture.Home, fixture.Path_, OwnerKind.TerminalAgent, out var claim, out _));

        using (claim)
        {
            Type(model, TempVault.Password);
            await model.UnlockAsync();

            Assert.Equal(0, unlocked);
            Assert.Contains(agent, model.Message, StringComparison.Ordinal);
            Assert.Equal($"Keypaste agent (process {Environment.ProcessId}) holds this vault, and agents reach it there.", model.Owner);

            var held = Assert.IsType<AuthorityStatus.HeldBy>(authority.Status);
            Assert.Equal(OwnerKind.TerminalAgent, held.Owner.Kind);
            Assert.Equal(model.Owner, AgentActivityViewModel.Describe(held));

            Type(model, "x");
            Assert.False(model.HasMessage);
            Assert.True(model.HasOwner);
        }

        model.ClearPassword();
        Type(model, TempVault.Password);
        await model.UnlockAsync();

        Assert.Equal(1, unlocked);
        Assert.False(model.HasOwner);
        Assert.IsType<AuthorityStatus.Serving>(authority.Status);
    }

    [Fact]
    public async Task Quitting_answers_a_waiting_request_as_locked_before_the_endpoint_stops()
    {
        using var fixture = new TempVault();
        var prompt = new HeldPrompt();
        var authority = Launch(fixture, prompt: () => prompt);

        try
        {
            Unlock(authority.Session, fixture.Path_);
            var serving = Assert.IsType<AuthorityStatus.Serving>(authority.Status);

            await using var client = await ApproverClient.TryConnectAsync(serving.Endpoint, _connect, Token);
            Assert.NotNull(client);
            var attached = await client.AttachAsync(new AttachRequest(fixture.Path_), Token);
            var pending = client.RequestAsync(Request(fixture.Path_, attached!.Session!), Token).AsTask();
            await prompt.Waiting.WaitAsync(_connect, Token);

            authority.Dispose();
            var reply = await pending.WaitAsync(_connect, Token);

            Assert.NotNull(reply);
            Assert.Equal(AuditDecision.Denied, reply.Decision);
            Assert.Equal(AuditMethod.VaultLocked, reply.Method);
            Assert.Equal(serving.Session, reply.Session);
            Assert.Null(reply.Value);
        }
        finally
        {
            authority.Dispose();
        }
    }

    /// <summary>Composes the authority as launch does, around a session on <paramref name="fixture"/>'s home.</summary>
    private static AppAuthority Launch(
        TempVault fixture,
        ManualClock? clock = null,
        string? approverOverride = null,
        Func<IApprovalChannel>? prompt = null)
    {
        // The authority owns the session, as it does at launch.
#pragma warning disable CA2000
        return new AppAuthority(new AppVaultSession(clock ?? new ManualClock(), home: fixture.Home), approverOverride, prompt ?? (() => new NobodyToAsk()));
#pragma warning restore CA2000
    }

    private static async Task<AuthorityStatus.Serving> AssertServedAsync(AppAuthority authority, string vault)
    {
        var serving = Assert.IsType<AuthorityStatus.Serving>(authority.Status);
        Assert.Equal(authority.Session.SessionId, serving.Session);
        Assert.Equal(OwnerKind.DesktopApp, serving.Owner.Kind);
        Assert.Equal(Environment.ProcessId, serving.Owner.ProcessId);
        Assert.Contains(serving.Session, AgentActivityViewModel.Describe(serving), StringComparison.Ordinal);

        await using var client = await ApproverClient.TryConnectAsync(serving.Endpoint, _connect, Token);
        Assert.NotNull(client);
        var attached = await client.AttachAsync(new AttachRequest(vault), Token);
        Assert.NotNull(attached);
        Assert.Equal(serving.Session, attached.Session);
        return serving;
    }

    private static async Task AssertAbsentAsync(string endpoint)
    {
        await using var client = await ApproverClient.TryConnectAsync(endpoint, _absent, Token);
        Assert.Null(client);
    }

    private static void AssertNobodyHolds(TempVault fixture)
    {
        Assert.True(VaultClaim.TryAcquire(fixture.Home, fixture.Path_, OwnerKind.TerminalAgent, out var claim, out var refusal), refusal);
        claim.Dispose();
    }

    private static void Unlock(AppVaultSession session, string path)
    {
        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(path, master.Value));
    }

    private static void Type(UnlockViewModel model, string text)
    {
        foreach (var c in text)
        {
            model.Type(c);
        }
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

    /// <summary>A person in front of the prompt who never answers.</summary>
    private sealed class HeldPrompt : IApprovalChannel
    {
        private readonly TaskCompletionSource _waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task Waiting => _waiting.Task;

        public async ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken)
        {
            _waiting.TrySetResult();

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Withdrawn, which is what the test is waiting to see answered.
            }

            return ApprovalAnswer.Denied;
        }
    }
}
