using System.Security.Cryptography;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// A vault's owner over a real pipe: only a connection attached to the session now holding the vault
/// it names is answered (D-0310), and nothing is released across the lock that ends it (D-0313).
/// </summary>
public sealed class SessionAuthorityTests : IDisposable
{
    private static readonly TimeSpan _connectTimeout = TimeSpan.FromSeconds(10);

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-authority-tests-").FullName;
    private readonly ApproverFixture _fixture = new();
    private SessionLifetime? _lifetime = new("session-one");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    private CredentialRequest Request(string session, string? vault = null) => new()
    {
        Entry = "env/dev/STRIPE_KEY",
        Field = "password",
        Reason = "deploy billing to staging",
        TtlSeconds = 60,
        Exposure = ["env/**"],
        Vault = vault ?? VaultPath,
        Session = session,
    };

    private NamesRequest Listing(string session) => new(["env/**"]) { Vault = VaultPath, Session = session };

    [Fact]
    public async Task AnAttachedListing_IsAnsweredFromTheVaultAndNamesTheSession()
    {
        await using var owner = Owner.Start(this);
        await using var client = await ConnectAsync(owner.PipeName);

        var attached = await client.AttachAsync(new AttachRequest(VaultPath), Token);
        Assert.NotNull(attached);
        Assert.True(attached.Attached);
        Assert.Equal("session-one", attached.Session);

        var reply = await client.ListAsync(Listing("session-one"), Token);

        Assert.NotNull(reply);
        Assert.True(reply.VaultUnlocked);
        Assert.Equal([new EntryName("env/dev", "STRIPE_KEY")], reply.Names);
        Assert.Equal("session-one", reply.Session);
    }

    [Fact]
    public async Task AnAttachedRequest_IsDecidedAndNamesTheSession()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await ConnectAsync(owner.PipeName);

        await client.AttachAsync(new AttachRequest(VaultPath), Token);
        var reply = await client.RequestAsync(Request("session-one"), Token);

        Assert.NotNull(reply);
        Assert.Equal(AuditDecision.Granted, reply.Decision);
        Assert.Equal(ApproverFixture.Sentinel, reply.Value);
        Assert.Equal("session-one", reply.Session);
    }

    [Fact]
    public async Task ARequestFromAConnectionThatNeverAttached_IsRefusedUnasked()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await ConnectAsync(owner.PipeName);

        var reply = await client.RequestAsync(Request("session-one"), Token);
        var listing = await client.ListAsync(Listing("session-one"), Token);

        Assert.NotNull(reply);
        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Equal(AuditMethod.NoSession, reply.Method);
        Assert.Null(reply.Value);
        Assert.NotNull(listing);
        Assert.False(listing.VaultUnlocked);
        Assert.Empty(listing.Names);
        Assert.Equal(0, _fixture.Channel.Asked);
        Assert.Equal(0, _fixture.Source.Reads);
    }

    [Fact]
    public async Task AnAttachmentNamingAnotherVault_IsRefused()
    {
        await using var owner = Owner.Start(this);
        await using var client = await ConnectAsync(owner.PipeName);

        var elsewhere = Path.Combine(_directory, "other.kdbx");
        var attached = await client.AttachAsync(new AttachRequest(elsewhere), Token);
        var reply = await client.RequestAsync(Request("session-one", elsewhere), Token);

        Assert.NotNull(attached);
        Assert.False(attached.Attached);
        Assert.Equal(AuditMethod.NoSession, attached.Refusal);
        Assert.NotNull(reply);
        Assert.Equal(AuditMethod.NoSession, reply.Method);
        Assert.Equal(0, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task ARequestNamingAnotherVaultThanItsAttachment_IsRefused()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await ConnectAsync(owner.PipeName);

        await client.AttachAsync(new AttachRequest(VaultPath), Token);
        var reply = await client.RequestAsync(Request("session-one", Path.Combine(_directory, "other.kdbx")), Token);

        Assert.NotNull(reply);
        Assert.Equal(AuditMethod.NoSession, reply.Method);
        Assert.Equal(0, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task ARequestFromASessionThatEnded_IsRefusedAfterTheVaultIsUnlockedAgain()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await ConnectAsync(owner.PipeName);

        await client.AttachAsync(new AttachRequest(VaultPath), Token);
        _lifetime = new SessionLifetime("session-two");

        var reply = await client.RequestAsync(Request("session-one"), Token);
        var listing = await client.ListAsync(Listing("session-one"), Token);

        Assert.NotNull(reply);
        Assert.Equal(AuditMethod.NoSession, reply.Method);
        Assert.Null(reply.Session);
        Assert.NotNull(listing);
        Assert.False(listing.VaultUnlocked);
        Assert.Equal(0, _fixture.Channel.Asked);
        Assert.Equal(0, _fixture.Source.Reads);
    }

    [Fact]
    public async Task ARequestPresentingTheNewSessionOnAnOldAttachment_IsRefused()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await ConnectAsync(owner.PipeName);

        await client.AttachAsync(new AttachRequest(VaultPath), Token);
        _lifetime = new SessionLifetime("session-two");

        var reply = await client.RequestAsync(Request("session-two"), Token);

        Assert.NotNull(reply);
        Assert.Equal(AuditMethod.NoSession, reply.Method);
        Assert.Equal(0, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task ALockedOwner_RefusesAttachmentAndEveryRequest()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        await using var owner = Owner.Start(this);
        await using var client = await ConnectAsync(owner.PipeName);

        await client.AttachAsync(new AttachRequest(VaultPath), Token);
        _lifetime = null;

        var reply = await client.RequestAsync(Request("session-one"), Token);
        var attached = await client.AttachAsync(new AttachRequest(VaultPath), Token);

        Assert.NotNull(reply);
        Assert.Equal(AuditMethod.VaultLocked, reply.Method);
        Assert.NotNull(attached);
        Assert.False(attached.Attached);
        Assert.Equal(AuditMethod.VaultLocked, attached.Refusal);
        Assert.Equal(0, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task AnotherSpellingOfTheVault_AttachesToIt()
    {
        await using var owner = Owner.Start(this);
        await using var client = await ConnectAsync(owner.PipeName);

        var respelled = Path.Combine(_directory, ".", "vault.kdbx");
        var attached = await client.AttachAsync(new AttachRequest(respelled), Token);
        var listing = await client.ListAsync(new NamesRequest(["env/**"]) { Vault = respelled, Session = "session-one" }, Token);

        Assert.NotNull(attached);
        Assert.True(attached.Attached);
        Assert.NotNull(listing);
        Assert.True(listing.VaultUnlocked);
    }

    [Fact]
    public async Task ARequestWaitingForAPerson_IsWithdrawnAndDeniedAsLockedWhenTheLifetimeEnds()
    {
        _fixture.Channel.Hold = true;
        await using var owner = Owner.Start(this);
        await using var client = await ConnectAsync(owner.PipeName);

        await client.AttachAsync(new AttachRequest(VaultPath), Token);
        var pending = client.RequestAsync(Request("session-one"), Token);
        await _fixture.Channel.Waiting.WaitAsync(_connectTimeout, Token);

        _lifetime!.End();
        var reply = await pending.AsTask().WaitAsync(_connectTimeout, Token);

        Assert.NotNull(reply);
        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Equal(AuditMethod.VaultLocked, reply.Method);
        Assert.Equal("the vault was locked before anybody answered", reply.Reason);
        Assert.Equal("session-one", reply.Session);
        Assert.Null(reply.Value);
        Assert.True(_fixture.Channel.Withdrawn);
        Assert.Equal(0, _fixture.Source.Reads);
    }

    [Fact]
    public async Task AnApprovalRacingTheLock_ReleasesNothingAndKeepsNoGrant()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        var lifetime = _lifetime!;
        lifetime.Own(_fixture.Grants);
        _fixture.Source.During = lifetime.End;
        await using var owner = Owner.Start(this);
        await using var client = await ConnectAsync(owner.PipeName);

        await client.AttachAsync(new AttachRequest(VaultPath), Token);
        var reply = await client.RequestAsync(Request("session-one"), Token);

        Assert.NotNull(reply);
        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Equal(AuditMethod.VaultLocked, reply.Method);
        Assert.Equal("the vault was locked before this was released", reply.Reason);
        Assert.Null(reply.Value);
        Assert.Equal(0, _fixture.Grants.Count);
    }

    [Fact]
    public async Task APolicyReleaseRacingTheLock_IsRefused()
    {
        using var policed = new ApproverFixture(ApproverHandlerPolicyTests.Policy());
        policed.Source.During = _lifetime!.End;
        await using var owner = Owner.Start(this, policed.Handler);
        await using var client = await ConnectAsync(owner.PipeName);

        await client.AttachAsync(new AttachRequest(VaultPath), Token);
        var reply = await client.RequestAsync(Request("session-one") with { ClientLabel = "billing-bot" }, Token);

        Assert.NotNull(reply);
        Assert.Equal(AuditMethod.VaultLocked, reply.Method);
        Assert.Null(reply.Value);
        Assert.Equal(1, policed.Source.Reads);
        Assert.Equal(0, policed.Channel.Asked);
    }

    [Fact]
    public async Task AGrantGivenBeforeALock_ReleasesNothingAfterTheNextUnlock()
    {
        _fixture.Channel.Answer = ApprovalAnswer.Approved;
        _lifetime!.Own(_fixture.Grants);
        await using var owner = Owner.Start(this);
        await using var client = await ConnectAsync(owner.PipeName);

        await client.AttachAsync(new AttachRequest(VaultPath), Token);
        var granted = await client.RequestAsync(Request("session-one"), Token);
        Assert.NotNull(granted);
        Assert.Equal(AuditMethod.Prompt, granted.Method);
        Assert.Equal(1, _fixture.Grants.Count);

        _lifetime.End();
        Assert.Equal(0, _fixture.Grants.Count);

        _lifetime = new SessionLifetime("session-two");
        _fixture.Channel.Answer = ApprovalAnswer.Denied;
        await client.AttachAsync(new AttachRequest(VaultPath), Token);
        var again = await client.RequestAsync(Request("session-two"), Token);

        Assert.NotNull(again);
        Assert.Equal(AuditDecision.Denied, again.Decision);
        Assert.Equal(AuditMethod.Prompt, again.Method);
        Assert.Equal(2, _fixture.Channel.Asked);
    }

    [Fact]
    public async Task AListingRacingTheLock_IsRefused()
    {
        _fixture.Source.During = _lifetime!.End;
        await using var owner = Owner.Start(this);
        await using var client = await ConnectAsync(owner.PipeName);

        await client.AttachAsync(new AttachRequest(VaultPath), Token);
        var listing = await client.ListAsync(Listing("session-one"), Token);

        Assert.NotNull(listing);
        Assert.False(listing.VaultUnlocked);
        Assert.Empty(listing.Names);
    }

    public void Dispose()
    {
        _lifetime?.Dispose();
        _fixture.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private static async Task<ApproverClient> ConnectAsync(string pipeName)
    {
        var client = await ApproverClient.TryConnectAsync(pipeName, _connectTimeout, Token);

        Assert.NotNull(client);
        return client;
    }

    /// <summary>An owner of the test's vault on its own pipe, whose session the test changes.</summary>
    private sealed class Owner : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly ApproverListener _listener;
        private readonly Task _running;

        private Owner(string pipeName, ApproverListener listener)
        {
            PipeName = pipeName;
            _listener = listener;
            _running = listener.RunAsync(_stop.Token);
        }

        internal string PipeName { get; }

        internal static Owner Start(SessionAuthorityTests test, ApproverHandler? handler = null)
        {
            var name = "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
            var authority = new SessionAuthority(
                VaultIdentity.Of(test._directory, test.VaultPath),
                () => test._lifetime,
                handler ?? test._fixture.Handler);

            return new Owner(name, new ApproverListener(name, authority));
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
