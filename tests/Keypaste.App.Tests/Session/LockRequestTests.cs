using Keypaste.App.Session;
using Keypaste.Core.Ipc;
using Xunit;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// <c>keypaste lock</c> reaching the app over its real endpoint locks the session, through the same
/// action launch composes, with its own reason (D-0351).
/// </summary>
public sealed class LockRequestTests
{
    private static readonly TimeSpan _bound = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ALockRequest_LocksTheSession_WithReasonRequested()
    {
        using var fixture = new TempVault();
#pragma warning disable CA2000
        var session = new AppVaultSession(new ManualClock(), home: fixture.Home);
#pragma warning restore CA2000
        using var authority = new AppAuthority(session, null, () => new NobodyToAsk(), AppAuthority.RequestLock(session, run => run()));
        var locked = new TaskCompletionSource<VaultLockReason>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Locked += (_, reason) => locked.TrySetResult(reason);

        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(fixture.Path_, master.Value));
        }

        var serving = Assert.IsType<AuthorityStatus.Serving>(authority.Status);
        await using var client = await ApproverClient.TryConnectAsync(serving.Endpoint, _bound, Token);
        Assert.NotNull(client);
        var attached = await client.AttachAsync(new AttachRequest(fixture.Path_), Token);
        Assert.True(attached!.Attached);

        var reply = await client.LockAsync(new LockRequest { Vault = fixture.Path_, Session = serving.Session }, Token);

        Assert.Equal(new LockReply(true, string.Empty), reply);
        Assert.Equal(VaultLockReason.Requested, await locked.Task.WaitAsync(_bound, Token));
        Assert.False(session.IsUnlocked);
        Assert.IsType<AuthorityStatus.Locked>(authority.Status);
    }

    [Fact]
    public async Task WithoutALockAction_TheAppRefuses()
    {
        using var fixture = new TempVault();
#pragma warning disable CA2000
        using var authority = new AppAuthority(new AppVaultSession(new ManualClock(), home: fixture.Home), null, () => new NobodyToAsk());
#pragma warning restore CA2000

        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, authority.Session.TryUnlock(fixture.Path_, master.Value));
        }

        var serving = Assert.IsType<AuthorityStatus.Serving>(authority.Status);
        await using var client = await ApproverClient.TryConnectAsync(serving.Endpoint, _bound, Token);
        await client!.AttachAsync(new AttachRequest(fixture.Path_), Token);

        var reply = await client.LockAsync(new LockRequest { Vault = fixture.Path_, Session = serving.Session }, Token);

        Assert.False(reply!.Locking);
        Assert.True(authority.Session.IsUnlocked);
    }
}
