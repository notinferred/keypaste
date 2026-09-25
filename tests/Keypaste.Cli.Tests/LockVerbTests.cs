using System.Security.Cryptography;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste lock</c> against a real owner on a real pipe: it confirms a lock only once the owner has
/// stopped answering for the session, and asks for no password (D-0351).
/// </summary>
public sealed class LockVerbTests : IDisposable
{
    private readonly CliHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private int Lock() => _harness.Run("lock", "--vault", _harness.VaultPath);

    /// <summary>Both shapes an owner locks in: the agent stops listening, the desktop keeps listening and refuses.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Lock_EndsTheOwnersLifetime_AndConfirms(bool stopsListening)
    {
        await using var owner = SessionOwner.Start(
            _harness,
            stopsListening ? SessionOwner.LockKind.StopsListening : SessionOwner.LockKind.KeepsListening);
        owner.Grant("claude-code", "env/acme-api", "STRIPE_KEY");

        _harness.AssertExit(CliApp.ExitSuccess, Lock());

        Assert.False(owner.Lifetime.IsLive);
        Assert.Empty(owner.Authority.Activity.Grants);
        Assert.Equal($"  ✓ locked {_harness.VaultPath} · agents paused" + Environment.NewLine, _harness.Err);
        Assert.Empty(_harness.Out);
        Assert.Empty(_harness.Prompt.PromptsSeen);

        if (stopsListening)
        {
            await owner.Stopped.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public void Lock_NothingUnlocked_ExitsZero()
    {
        _harness.Environment[Core.Ipc.ApproverEndpoint.EnvironmentVariable] = "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

        _harness.AssertExit(CliApp.ExitSuccess, Lock());

        Assert.Equal($"  nothing to lock: no keypaste process holds {_harness.VaultPath} unlocked" + Environment.NewLine, _harness.Err);
        Assert.Empty(_harness.Prompt.PromptsSeen);
    }

    [Fact]
    public async Task Lock_OwnerWithoutLockNow_IsRefused_Exit2()
    {
        await using var owner = SessionOwner.Start(_harness);

        _harness.AssertExit(CliApp.ExitInternalError, Lock());

        Assert.Contains("keypaste lock: the keypaste process holding this vault cannot be locked from outside", _harness.Err, StringComparison.Ordinal);
        Assert.True(owner.Lifetime.IsLive);
    }
}
