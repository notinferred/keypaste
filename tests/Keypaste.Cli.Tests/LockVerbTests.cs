using System.IO.Pipes;
using System.Security.Cryptography;
using Keypaste.Core.Infrastructure;
using Keypaste.Core.Ipc;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste lock</c> against a real owner on a real pipe: it reports a lock once the owner answers that
/// the session has ended, and asks for no password (D-0351).
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
        _harness.Environment[ApproverEndpoint.EnvironmentVariable] = "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

        _harness.AssertExit(CliApp.ExitSuccess, Lock());

        Assert.Equal($"  nothing to lock: no keypaste process holds {_harness.VaultPath} unlocked" + Environment.NewLine, _harness.Err);
        Assert.Empty(_harness.Prompt.PromptsSeen);
    }

    /// <summary>
    /// The owner's reply is the confirmation, so an owner whose connection neither answers nor ends after
    /// it replies, as <c>keypaste agent</c>'s closing one did on Windows for three seconds (F.62), is
    /// reported locked at once and asked nothing more.
    /// </summary>
    [Fact]
    public async Task Lock_TakesTheOwnersReply_AndAsksNothingMoreOnItsConnection()
    {
        var pipe = "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        _harness.Environment[ApproverEndpoint.EnvironmentVariable] = pipe;
        using var done = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var owner = LingeringOwnerAsync(pipe, done.Token);

        var exit = Lock();
        await done.CancelAsync();

        _harness.AssertExit(CliApp.ExitSuccess, exit);
        Assert.Equal($"  ✓ locked {_harness.VaultPath} · agents paused" + Environment.NewLine, _harness.Err);
        Assert.Equal(0, await owner);
    }

    [Fact]
    public async Task Lock_OwnerWithoutLockNow_IsRefused_Exit2()
    {
        await using var owner = SessionOwner.Start(_harness);

        _harness.AssertExit(CliApp.ExitInternalError, Lock());

        Assert.Contains("keypaste lock: the keypaste process holding this vault cannot be locked from outside", _harness.Err, StringComparison.Ordinal);
        Assert.True(owner.Lifetime.IsLive);
    }

    /// <summary>Attaches the verb and answers its lock as a locked owner does, then holds the connection, answering nothing, until the test is done.</summary>
    /// <returns>How many frames arrived after the lock's reply.</returns>
    private static async Task<int> LingeringOwnerAsync(string pipe, CancellationToken done)
    {
        await using var server = new NamedPipeServerStream(
            pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await server.WaitForConnectionAsync(done);
        using var framer = new MessageFramer(server, ownsStream: false);

        var attach = await framer.ReadAsync(done);
        Assert.True(ApproverProtocol.TryDecode(attach, out AttachRequest? _));
        await framer.WriteAsync(ApproverProtocol.Encode(AttachReply.To("lingering-session")), done);

        var lockNow = await framer.ReadAsync(done);
        Assert.True(ApproverProtocol.TryDecode(lockNow, out LockRequest? _));
        await framer.WriteAsync(ApproverProtocol.Encode(new LockReply(true, string.Empty)), done);

        var after = 0;

        try
        {
            while (await framer.ReadAsync(done) is not null)
            {
                after++;
            }
        }
        catch (OperationCanceledException)
        {
            // The test is done with the connection.
        }

        return after;
    }
}
