using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>The pipe a locked app listens on for a vault, which a bridge about to refuse its agent for want of an owner connects to (D-0466).</summary>
public sealed class LockedAppEndpointTests : IDisposable
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _bound = TimeSpan.FromSeconds(1);

    private readonly string _home = Directory.CreateTempSubdirectory("keypaste-locked-app-tests-").FullName;
    private readonly VaultIdentity _vault;
    private readonly string _name;

    public LockedAppEndpointTests()
    {
        _vault = VaultIdentity.Of(_home, Path.Combine(_home, "vault.kdbx"));
        _name = LockedAppEndpoint.NameFor(_vault);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void EachVault_HasItsOwnName_ApartFromItsApproverAndTheApps()
    {
        var other = VaultIdentity.Of(_home, Path.Combine(_home, "other.kdbx"));

        Assert.Equal(LockedAppEndpoint.Prefix + _vault.Key, _name);
        Assert.NotEqual(LockedAppEndpoint.NameFor(other), _name);
        Assert.NotEqual(ApproverEndpoint.Resolve(null, null, _vault), _name);
        Assert.NotEqual(AppClaim.EndpointFor(_home), _name);
        Assert.True(_name.Length <= LaunchEndpoint.NewName().Length, "the name is longer than the launch endpoint's, which keeps its socket path inside macOS's limit");
    }

    [Fact]
    public async Task ABridgeThatFindsTheLockedApp_IsToldSo_AndTheAppHearsIt()
    {
        using var heard = new SemaphoreSlim(0);
        using var endpoint = LockedAppEndpoint.Listen(_name, () => heard.Release(), _ => { });

        Assert.Equal(_name, endpoint.Name);
        Assert.True(await LockedAppEndpoint.TellAsync(_name, Token), "the bridge did not reach the app");
        Assert.True(await heard.WaitAsync(_wait, Token), "the app did not hear the bridge");

        Assert.True(await LockedAppEndpoint.TellAsync(_name, Token), "a second bridge did not reach the app");
        Assert.True(await heard.WaitAsync(_wait, Token), "the app did not hear a second bridge");
        Assert.Equal(2, endpoint.Requests);
    }

    [Fact]
    public async Task WithNothingListening_TheBridgeHearsNoWithinItsBound()
    {
        var started = Stopwatch.GetTimestamp();

        Assert.False(await LockedAppEndpoint.TellAsync(_name, Token));
        Assert.True(Stopwatch.GetElapsedTime(started) < _bound, "a bridge with no app to reach waited past its bound");
    }

    [Fact]
    public async Task WhatAPeerSends_IsNeverUsed_AndTheNextIsStillHeard()
    {
        using var heard = new SemaphoreSlim(0);
        using var endpoint = LockedAppEndpoint.Listen(_name, () => heard.Release(), _ => { });

        await using (var peer = Peer())
        {
            peer.Connect(_wait);

            try
            {
                await peer.WriteAsync(new byte[64 * 1024], Token);
            }
            catch (IOException)
            {
                // The app ends a connection still open at its bound, which can end the write.
            }
        }

        Assert.True(await heard.WaitAsync(_wait, Token), "a connection that sent bytes was not heard");

        Assert.True(await LockedAppEndpoint.TellAsync(_name, Token), "the bridge after it did not reach the app");
        Assert.True(await heard.WaitAsync(_wait, Token), "the bridge after it was not heard");
        Assert.Equal(2, endpoint.Requests);
    }

    [Fact]
    public async Task BridgesInQuickSuccession_AreEachHeard()
    {
        // Each bridge hangs up as soon as it connects, which can be before the app waits again; on Windows that
        // connection reaches the app already gone, 1 in 200 times in F.66's probe.
        const int bridges = 300;
        var heard = 0;
        string? unreachable = null;
        using var endpoint = LockedAppEndpoint.Listen(_name, () => Interlocked.Increment(ref heard), why => unreachable = why);

        for (var i = 0; i < bridges; i++)
        {
            Assert.True(await LockedAppEndpoint.TellAsync(_name, Token), $"bridge {i} did not reach the app; the app stopped listening: {unreachable ?? "no"}");
        }

        var started = Stopwatch.GetTimestamp();
        while (Volatile.Read(ref heard) < bridges && Stopwatch.GetElapsedTime(started) < _wait)
        {
            await Task.Delay(20, Token);
        }

        Assert.Equal(bridges, Volatile.Read(ref heard));
        Assert.Equal(bridges, endpoint.Requests);
        Assert.Null(unreachable);
    }

    [Fact]
    public async Task AListenerThatHangsUpAtOnce_FailsNoBridge()
    {
        // On Windows the runtime checks who made the pipe after connecting, which a listener that has hung up fails: 178 of 200 times in F.70's probe.
        const int bridges = 50;
        using var listener = new HangsUpAtOnce(_name);

        for (var i = 0; i < bridges; i++)
        {
            var thrown = await Record.ExceptionAsync(() => LockedAppEndpoint.TellAsync(_name, Token));
            Assert.True(thrown is null, $"bridge {i} failed rather than being told no: {thrown}");
        }
    }

    [Fact]
    public async Task APeerThatNeverHangsUp_HoldsNothingUp()
    {
        using var heard = new SemaphoreSlim(0);
        using var endpoint = LockedAppEndpoint.Listen(_name, () => heard.Release(), _ => { });
        await using var peer = Peer();

        peer.Connect(_wait);
        Assert.True(await heard.WaitAsync(_wait, Token), "a peer's connection was not heard");

        Assert.True(await LockedAppEndpoint.TellAsync(_name, Token), "a peer holding its connection kept the next bridge out");
        Assert.True(await heard.WaitAsync(_wait, Token), "the bridge after it was not heard");

        var stopping = Stopwatch.GetTimestamp();
        endpoint.Dispose();
        Assert.True(Stopwatch.GetElapsedTime(stopping) < _bound, "a peer holding its connection held up stopping");
    }

    [Fact]
    public async Task AStoppedListener_LeavesNothingAnswering()
    {
        LockedAppEndpoint.Listen(_name, () => { }, _ => { }).Dispose();

        Assert.False(await LockedAppEndpoint.TellAsync(_name, Token));
    }

    [Fact]
    public void ANameSomebodyElseTookFirst_IsReported()
    {
        using var taken = TakeName(_name);
        string? reason = null;

        using var endpoint = LockedAppEndpoint.Listen(_name, () => { }, why => reason = why);

        Assert.False(string.IsNullOrEmpty(reason), "a name somebody else holds was not reported");
        Assert.Equal(0, endpoint.Requests);
    }

    [Fact]
    public async Task ASocketFileTheRuntimeRefuses_EndsATellAtOnce()
    {
        if (OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess)
        {
            Assert.Skip("Windows has no socket file, and root may connect to any");
            return;
        }

        var path = SocketPath(_name);
        using var squatter = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

        try
        {
            squatter.Bind(new UnixDomainSocketEndPoint(path));
            squatter.Listen(1);
            File.SetUnixFileMode(path, UnixFileMode.None);

            var started = Stopwatch.GetTimestamp();
            Assert.False(await LockedAppEndpoint.TellAsync(_name, Token), "a socket the runtime refuses was taken for the app");
            Assert.True(Stopwatch.GetElapsedTime(started) < _bound, "a refused bridge waited past its bound");
        }
        finally
        {
            File.Delete(path);
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

    // Where .NET puts a named pipe on Unix.
    private static string SocketPath(string name) => Path.Combine(Path.GetTempPath(), "CoreFxPipe_") + name;

    // A name held as another user's would be: a directory at the socket path on Unix, which binding cannot replace, and on Windows a pipe this process made first.
    private static IDisposable TakeName(string name)
    {
        if (OperatingSystem.IsWindows())
        {
            return new NamedPipeServerStream(name, PipeDirection.InOut, 2, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        }

        var path = SocketPath(name);
        Directory.CreateDirectory(path);
        return new Removed(path);
    }

    private NamedPipeClientStream Peer() =>
        new(".", _name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private sealed class Removed(string directory) : IDisposable
    {
        public void Dispose() => Directory.Delete(directory);
    }

    // A listener of this user that hangs up on each connection as soon as it arrives, which no keypaste app does.
    private sealed class HangsUpAtOnce : IDisposable
    {
        private readonly string _name;
        private readonly Thread _serving;
        private volatile bool _stopping;

        public HangsUpAtOnce(string name)
        {
            _name = name;
            _serving = new Thread(Serve) { IsBackground = true };
            _serving.Start();
        }

        public void Dispose()
        {
            _stopping = true;
            using var wake = new NamedPipeClientStream(".", _name, PipeDirection.InOut, PipeOptions.None);

            try
            {
                wake.Connect(1000);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                // Nothing is waiting to be woken.
            }

            _serving.Join(_wait);
        }

        private void Serve()
        {
            while (!_stopping)
            {
                using var pipe = new NamedPipeServerStream(_name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);

                try
                {
                    pipe.WaitForConnection();
                    pipe.Disconnect();
                }
                catch (Exception ex) when (ex is IOException or SocketException)
                {
                    // The bridge hung up first.
                }
            }
        }
    }
}
