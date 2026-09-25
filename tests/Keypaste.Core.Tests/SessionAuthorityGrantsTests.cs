using System.Security.Cryptography;
using Keypaste.Core.Approval;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The owner answers <c>grants</c>, <c>revoke-grants</c> and <c>lock</c> over its real endpoint only
/// for a connection attached to the live session, lists names and never a value, and runs its lock
/// action after the reply has left (D-0351).
/// </summary>
public sealed class SessionAuthorityGrantsTests : IDisposable
{
    private static readonly TimeSpan _bound = TimeSpan.FromSeconds(10);

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-grants-tests-").FullName;
    private readonly ApproverFixture _fixture = new();
    private SessionLifetime? _lifetime = new("session-one");

    public SessionAuthorityGrantsTests() => _fixture.Channel.Answer = ApprovalAnswer.Approved;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    public void Dispose()
    {
        _lifetime?.Dispose();
        _fixture.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private GrantsRequest Grants(string session = "session-one") => new() { Vault = VaultPath, Session = session };

    private RevokeGrantsRequest Revoke(IReadOnlyList<string> ids, string? client = null, bool all = false, string session = "session-one") =>
        new(ids, client, all) { Vault = VaultPath, Session = session };

    private LockRequest Lock(string session = "session-one") => new() { Vault = VaultPath, Session = session };

    /// <summary>Has a person approve one request, which leaves a grant scoped to this connection.</summary>
    private async Task GrantAsync(ApproverClient client, string clientName, string entry, string field = "password")
    {
        var reply = await client.RequestAsync(
            new CredentialRequest
            {
                Entry = entry,
                Field = field,
                Reason = "deploy",
                TtlSeconds = 60,
                Exposure = ["**"],
                ClientName = clientName,
                Vault = VaultPath,
                Session = "session-one",
            },
            Token);

        Assert.Equal(Audit.AuditDecision.Granted, reply!.Decision);
    }

    [Fact]
    public async Task Grants_ListWithoutValues()
    {
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();
        await GrantAsync(client, "claude-code", "env/dev/STRIPE_KEY");

        var reply = await client.GrantsAsync(Grants(), Token);

        Assert.NotNull(reply);
        Assert.True(reply.Answered);
        Assert.True(reply.Complete);
        var grant = Assert.Single(reply.Grants);
        Assert.Equal(GrantId.Of(Assert.Single(owner.Authority.Activity.Grants).Key), grant.Id);
        Assert.Equal(new GrantSummary(grant.Id, "credential", "claude-code", "env/dev/STRIPE_KEY", "password", 3600), grant);
        Assert.DoesNotContain(ApproverFixture.Sentinel, grant.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(ApproverFixture.Sentinel, System.Text.Encoding.UTF8.GetString(ApproverProtocol.Encode(reply)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revoke_ByIdEndsOnlyThatGrant()
    {
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();
        await GrantAsync(client, "claude-code", "env/dev/STRIPE_KEY");
        await GrantAsync(client, "claude-code", "env/dev/STRIPE_KEY", "username");
        var ended = owner.Authority.Activity.Grants.Single(grant => grant.Key.Field == "password");

        var reply = await client.RevokeGrantsAsync(Revoke([GrantId.Of(ended.Key)]), Token);

        Assert.Equal(new RevokeGrantsReply(1, string.Empty), reply);
        Assert.Equal("username", Assert.Single(owner.Authority.Activity.Grants).Key.Field);
    }

    [Fact]
    public async Task Revoke_ByClient()
    {
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();
        await GrantAsync(client, "claude-code", "env/dev/STRIPE_KEY");
        await GrantAsync(client, "claude-code", "env/dev/STRIPE_KEY", "username");
        await GrantAsync(client, "cursor", "personal/bank");

        var reply = await client.RevokeGrantsAsync(Revoke([], "claude-code"), Token);
        var nobody = await client.RevokeGrantsAsync(Revoke([], "claude"), Token);

        Assert.Equal(new RevokeGrantsReply(2, string.Empty), reply);
        Assert.Equal(new RevokeGrantsReply(0, string.Empty), nobody);
        Assert.Equal("cursor", Assert.Single(owner.Authority.Activity.Grants).Approved.Client);
    }

    [Fact]
    public async Task Revoke_All()
    {
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();
        await GrantAsync(client, "claude-code", "env/dev/STRIPE_KEY");
        await GrantAsync(client, "cursor", "personal/bank");

        var reply = await client.RevokeGrantsAsync(Revoke([], all: true), Token);

        Assert.Equal(new RevokeGrantsReply(2, string.Empty), reply);
        Assert.Empty(owner.Authority.Activity.Grants);
    }

    /// <summary>
    /// The lock action waits for the reply to have arrived; run inline, it would hold the reply back
    /// and this would time out.
    /// </summary>
    [Fact]
    public async Task Lock_RepliesThenEndsTheLifetime()
    {
        var replied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var locked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lifetime = _lifetime!;

        await using var owner = Owner.Start(this, () =>
        {
            replied.Task.Wait(_bound);
            lifetime.End();
            locked.SetResult();
        });
        await using var client = await owner.AttachAsync();
        await GrantAsync(client, "claude-code", "env/dev/STRIPE_KEY");

        var reply = await client.LockAsync(Lock(), Token).AsTask().WaitAsync(_bound, Token);

        Assert.Equal(new LockReply(true, string.Empty), reply);
        Assert.True(lifetime.IsLive);

        replied.SetResult();
        await locked.Task.WaitAsync(_bound, Token);

        Assert.False(lifetime.IsLive);
        Assert.Same(ApproverActivity.None, owner.Authority.Activity);
    }

    [Fact]
    public async Task ARequestFromAnEndedSession_IsRefused()
    {
        var asked = 0;
        await using var owner = Owner.Start(this, () => asked++);
        await using var client = await owner.AttachAsync();
        await GrantAsync(client, "claude-code", "env/dev/STRIPE_KEY");
        _lifetime = new SessionLifetime("session-two");

        var grants = await client.GrantsAsync(Grants(), Token);
        var revoked = await client.RevokeGrantsAsync(Revoke([], all: true), Token);
        var locking = await client.LockAsync(Lock(), Token);

        Assert.NotNull(grants);
        Assert.False(grants.Answered);
        Assert.Empty(grants.Grants);
        Assert.Equal("the request belongs to a session that has ended", grants.Reason);
        Assert.Equal(new RevokeGrantsReply(0, "the request belongs to a session that has ended"), revoked);
        Assert.Equal(new LockReply(false, "the request belongs to a session that has ended"), locking);
        Assert.Equal(1, _fixture.Grants.Count);
        Assert.Equal(0, asked);
    }

    [Fact]
    public async Task ARequestFromAConnectionThatNeverAttached_IsRefused()
    {
        await using var owner = Owner.Start(this, () => _lifetime!.End());
        await using var client = await ApproverClient.TryConnectAsync(owner.PipeName, _bound, Token);

        var locking = await client!.LockAsync(Lock(), Token);

        Assert.Equal(new LockReply(false, "the request came from a connection attached to no session"), locking);
        Assert.True(_lifetime!.IsLive);
    }

    [Fact]
    public async Task Locked_ListsNothing()
    {
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();
        await GrantAsync(client, "claude-code", "env/dev/STRIPE_KEY");
        _lifetime = null;

        var reply = await client.GrantsAsync(Grants(), Token);

        Assert.NotNull(reply);
        Assert.False(reply.Answered);
        Assert.Empty(reply.Grants);
        Assert.Equal("the vault is locked", reply.Reason);
    }

    [Fact]
    public async Task NoLockNow_IsRefused()
    {
        await using var owner = Owner.Start(this);
        await using var client = await owner.AttachAsync();

        var reply = await client.LockAsync(Lock(), Token);

        Assert.Equal(new LockReply(false, "the keypaste process holding this vault cannot be locked from outside"), reply);
        Assert.True(_lifetime!.IsLive);
    }

    /// <summary>An owner of the test's vault on its own pipe, whose session the test changes.</summary>
    private sealed class Owner : IAsyncDisposable
    {
        private readonly SessionAuthorityGrantsTests _test;
        private readonly CancellationTokenSource _stop = new();
        private readonly ApproverListener _listener;
        private readonly Task _running;

        private Owner(SessionAuthorityGrantsTests test, Action? lockNow)
        {
            _test = test;
            PipeName = "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
            Authority = new SessionAuthority(
                VaultIdentity.Of(test._directory, test.VaultPath),
                () => test._lifetime,
                test._fixture.Handler,
                lockNow: lockNow);
            _listener = new ApproverListener(PipeName, Authority);
            _running = _listener.RunAsync(_stop.Token);
        }

        internal string PipeName { get; }

        internal SessionAuthority Authority { get; }

        internal static Owner Start(SessionAuthorityGrantsTests test, Action? lockNow = null) => new(test, lockNow);

        internal async Task<ApproverClient> AttachAsync()
        {
            var client = await ApproverClient.TryConnectAsync(PipeName, _bound, Token);
            Assert.NotNull(client);

            var attached = await client.AttachAsync(new AttachRequest(_test.VaultPath), Token);
            Assert.True(attached!.Attached);

            return client;
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
