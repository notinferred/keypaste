using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>One app process per user and home, which a second start reaches only to ask it to show its window (D-0397).</summary>
public sealed class AppClaimTests : IDisposable
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _nobody = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan _atOnce = TimeSpan.FromSeconds(5);

    private readonly string _home = Directory.CreateTempSubdirectory("keypaste-app-claim-tests-").FullName;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void ASecondClaimOnOneHome_IsRefusedUntilTheFirstIsReleased()
    {
        Assert.True(AppClaim.TryAcquire(_home, out var first));

        using (first)
        {
            Assert.False(AppClaim.TryAcquire(_home, out var second));
            Assert.Null(second);
            second?.Dispose();
        }

        Assert.True(AppClaim.TryAcquire(_home, out var again));
        again.Dispose();
    }

    [Fact]
    public void TwoHomes_DoNotContendAndHaveTheirOwnEndpoints()
    {
        var other = Directory.CreateTempSubdirectory("keypaste-app-claim-tests-").FullName;

        try
        {
            Assert.True(AppClaim.TryAcquire(_home, out var first));
            using (first)
            {
                Assert.True(AppClaim.TryAcquire(other, out var second));
                using (second)
                {
                    Assert.NotEqual(first.Endpoint, second.Endpoint);
                    Assert.Equal(AppClaim.EndpointFor(_home), first.Endpoint);
                    Assert.StartsWith(AppClaim.Prefix, first.Endpoint, StringComparison.Ordinal);
                }
            }
        }
        finally
        {
            Directory.Delete(other, recursive: true);
        }
    }

    [Fact]
    public void AHolderThatIsNotListening_AnswersNoStart()
    {
        Assert.True(AppClaim.TryAcquire(_home, out var claim));
        using (claim)
        {
            Assert.False(Ask(_nobody));
        }
    }

    [Fact]
    public async Task ASecondStart_IsAnsweredOnceTheHolderHasShownItsWindow()
    {
        Assert.True(AppClaim.TryAcquire(_home, out var claim));
        using (claim)
        {
            using var shown = new SemaphoreSlim(0);
            using var listening = claim.Listen(() => Shown(shown), _ => { });

            Assert.True(Ask(_wait));
            Assert.True(await shown.WaitAsync(TimeSpan.Zero, Token), "the start was answered before the window was shown");

            Assert.True(Ask(_wait));
            Assert.True(await shown.WaitAsync(TimeSpan.Zero, Token), "a second start was not answered");
            Assert.Equal(2, claim.Requests);
        }
    }

    [Fact]
    public void AHolderTooBusyToShowItsWindow_IsNotTakenForAnAnswer()
    {
        Assert.True(AppClaim.TryAcquire(_home, out var claim));
        using (claim)
        {
            var never = new TaskCompletionSource();
            using var listening = claim.Listen(() => never.Task, _ => { });

            Assert.False(Ask(TimeSpan.FromSeconds(1)));
            Assert.Equal(1, claim.Requests);
        }
    }

    [Fact]
    public async Task AHolderThatQuitsWhileAStartWaits_LeavesThatStartTheHome()
    {
        Assert.True(AppClaim.TryAcquire(_home, out var claim));
        using (claim)
        {
            AppClaim? taken = null;
            var started = Stopwatch.GetTimestamp();
            var asking = Task.Run(() => AppClaim.AskToShow(_home, _wait, out taken), Token);

            await Task.Delay(_nobody, Token);
            claim.Dispose();

            Assert.False(await asking.WaitAsync(_wait, Token));
            Assert.True(Stopwatch.GetElapsedTime(started) < _atOnce, "the start waited out its time instead of taking the freed home");
            Assert.NotNull(taken);
            taken.Dispose();
        }
    }

    [Fact]
    public async Task WhatAPeerSends_IsNeverRead_AndTheNextStartIsStillAnswered()
    {
        Assert.True(AppClaim.TryAcquire(_home, out var claim));
        using (claim)
        {
            using var shown = new SemaphoreSlim(0);
            using var listening = claim.Listen(() => Shown(shown), _ => { });

            await using (var peer = new NamedPipeClientStream(
                ".", claim.Endpoint, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
            {
                peer.Connect(_wait);

                try
                {
                    await peer.WriteAsync(new byte[64 * 1024], Token);
                }
                catch (IOException)
                {
                    // The holder hangs up without reading, which can end the write.
                }
            }

            Assert.True(await shown.WaitAsync(_wait, Token), "a connection that sent bytes was not a request to show");

            Assert.True(Ask(_wait));
            Assert.True(await shown.WaitAsync(_wait, Token), "the start after it was not answered");
        }
    }

    [Fact]
    public void AStoppedListener_LeavesNothingAnswering()
    {
        Assert.True(AppClaim.TryAcquire(_home, out var claim));
        using (claim)
        {
            claim.Listen(() => Task.CompletedTask, _ => { }).Dispose();

            Assert.False(Ask(_nobody));
        }
    }

    [Fact]
    public void AnEndpointSomebodyElseTookFirst_IsReportedRatherThanListenedOn()
    {
        Assert.True(AppClaim.TryAcquire(_home, out var claim));
        using (claim)
        {
            using var taken = TakeName(claim.Endpoint);
            string? reason = null;

            using var listening = claim.Listen(() => Task.CompletedTask, why => reason = why);

            Assert.False(string.IsNullOrEmpty(reason), "a name somebody else holds was not reported");
            Assert.Equal(0, claim.Requests);
        }
    }

    [Fact]
    public void ASocketFileTheRuntimeRefuses_EndsAStartAtOnce()
    {
        if (OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess)
        {
            Assert.Skip("Windows has no socket file, and root may connect to any");
            return;
        }

        Assert.True(AppClaim.TryAcquire(_home, out var claim));
        using (claim)
        {
            var path = SocketPath(claim.Endpoint);
            using var squatter = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

            try
            {
                squatter.Bind(new UnixDomainSocketEndPoint(path));
                squatter.Listen(1);
                File.SetUnixFileMode(path, UnixFileMode.None);

                var started = Stopwatch.GetTimestamp();
                Assert.False(Ask(_wait));
                Assert.True(Stopwatch.GetElapsedTime(started) < _atOnce, "a refused start waited out its time");
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_home, recursive: true);
        }
        catch (IOException)
        {
            // A test that cannot clean up its temporary directory has still made its point.
        }
    }

    private bool Ask(TimeSpan wait)
    {
        var shown = AppClaim.AskToShow(_home, wait, out var taken);
        Assert.Null(taken);
        taken?.Dispose();
        return shown;
    }

    private static Task Shown(SemaphoreSlim shown)
    {
        shown.Release();
        return Task.CompletedTask;
    }

    // Where .NET puts a named pipe on Unix.
    private static string SocketPath(string endpoint) => Path.Combine(Path.GetTempPath(), "CoreFxPipe_") + endpoint;

    // A name held as another user's would be: a directory at the socket path on Unix, which binding cannot replace, and on Windows a pipe this process made first.
    private static IDisposable TakeName(string endpoint)
    {
        if (OperatingSystem.IsWindows())
        {
            return new NamedPipeServerStream(endpoint, PipeDirection.InOut, 2, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        }

        var path = SocketPath(endpoint);
        Directory.CreateDirectory(path);
        return new Removed(path);
    }

    private sealed class Removed(string directory) : IDisposable
    {
        public void Dispose() => Directory.Delete(directory);
    }
}
