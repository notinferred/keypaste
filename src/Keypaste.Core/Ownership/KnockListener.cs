using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Threading.Channels;

namespace Keypaste.Core.Ownership;

/// <summary>
/// Accepts, one at a time, connections to a pipe restricted to the current user where a connection is the
/// request, as a second start's to the app, which may also name a file, and a bridge's to a locked app are.
/// </summary>
/// <remarks>
/// The pipe is bound before the constructor returns. A connection the runtime refuses, which another user's
/// is (THREATS.md T-10), and a peer gone before it was accepted are not answered, and the next is accepted.
/// On Windows the pipe admits only this user, so a peer gone before it was accepted can still be told of.
/// On Unix one instance answers connection after connection: the kernel queues connections behind the one answered,
/// and making the instance anew closes its socket and drops them. Windows queues none, so instances wait beside the
/// one answered and a thread of its own replaces each as it is taken, however long the pool takes to answer (F.70, F.100);
/// each is made anew, since disconnecting discards a reply its peer has not yet read.
/// </remarks>
internal sealed class KnockListener : IDisposable
{
    private static readonly TimeSpan _stopGrace = TimeSpan.FromSeconds(2);

    // ERROR_NO_DATA: the peer connected and closed before the pipe waited for it.
    private const int _goneBeforeAccepted = unchecked((int)0x800700E8);

    // Peers connected and not yet answered, while the pool stands still; past this no instance is made until one has been.
    private const int _unansweredBound = 1024;

    private readonly CancellationTokenSource _stop = new();
    private readonly Task _accepting;

    /// <summary>Binds <paramref name="endpoint"/> and answers each connection to it until disposed.</summary>
    /// <param name="endpoint">The pipe's name.</param>
    /// <param name="answer">Answers one accepted connection, which is hung up once the answer completes; given the token that stops listening.</param>
    /// <param name="unreachable">Told why, when the endpoint cannot be bound.</param>
    /// <param name="goneBeforeAccepted">On Windows, told of a peer of this user that hung up before it was accepted; null leaves it unanswered.</param>
    internal KnockListener(
        string endpoint,
        Func<NamedPipeServerStream, CancellationToken, Task> answer,
        Action<string> unreachable,
        Action? goneBeforeAccepted = null) =>
        _accepting = OperatingSystem.IsWindows()
            ? AcceptOnWindows(endpoint, answer, unreachable, goneBeforeAccepted, _stop.Token)
            : AcceptAsync(endpoint, answer, unreachable, _stop.Token);

    public void Dispose()
    {
        if (_stop.IsCancellationRequested)
        {
            return;
        }

        _stop.Cancel();

        try
        {
            _accepting.Wait(_stopGrace);
        }
        catch (AggregateException)
        {
            // Stopping is the answer either way.
        }

        // A listener still on its way out needs the token.
        if (_accepting.IsCompleted)
        {
            _stop.Dispose();
        }
    }

    private static NamedPipeServerStream Create(string endpoint, int instances, PipeOptions options) =>
        new(endpoint, PipeDirection.InOut, instances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | options);

    private static async Task AcceptAsync(
        string endpoint,
        Func<NamedPipeServerStream, CancellationToken, Task> answer,
        Action<string> unreachable,
        CancellationToken stop)
    {
        try
        {
            while (true)
            {
                await AnswerOnInstanceAsync(endpoint, answer, stop).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping, or the app's dispatcher ending, is how this ends.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SocketException)
        {
            unreachable(ex.Message);
        }
    }

    private static async Task AnswerOnInstanceAsync(
        string endpoint,
        Func<NamedPipeServerStream, CancellationToken, Task> answer,
        CancellationToken stop)
    {
        await using var pipe = Create(endpoint, 1, PipeOptions.None);

        while (true)
        {
            try
            {
                await pipe.WaitForConnectionAsync(stop).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SocketException)
            {
                // Another user's connection, which the runtime refuses, or a peer gone before it was accepted.
                return;
            }

            await answer(pipe, stop).ConfigureAwait(false);
            HangUp(pipe);
        }
    }

    // Only the first instance may be the pipe's first, so a pipe somebody else made under this name is refused rather than joined.
    // Every later one is made while an earlier one holds the name, so it can only join this pipe.
    [SupportedOSPlatform("windows")]
    private static Task AcceptOnWindows(
        string endpoint,
        Func<NamedPipeServerStream, CancellationToken, Task> answer,
        Action<string> unreachable,
        Action? goneBeforeAccepted,
        CancellationToken stop)
    {
        NamedPipeServerStream? next = null;
        NamedPipeServerStream? spare = null;

        try
        {
            // KeepListening owns both once it starts; CA2000 cannot see past the thread.
#pragma warning disable CA2000
            next = CreateOnWindows(endpoint, PipeOptions.FirstPipeInstance);
            spare = CreateOnWindows(endpoint, PipeOptions.None);
#pragma warning restore CA2000
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            next?.Dispose();
            unreachable(ex.Message);
            return Task.CompletedTask;
        }

        var (first, second) = (next, spare);
        var arrivals = Channel.CreateBounded<NamedPipeServerStream>(new BoundedChannelOptions(_unansweredBound) { SingleReader = true, SingleWriter = true });
        var listening = new TaskCompletionSource();
        var keeper = new Thread(() =>
        {
            KeepListening(endpoint, first, second, arrivals.Writer, unreachable, stop);
            listening.SetResult();
        })
        {
            IsBackground = true,
            Name = "keypaste knock listener",
        };
        keeper.Start();

        return Task.WhenAll(listening.Task, AnswerArrivalsAsync(arrivals, answer, unreachable, goneBeforeAccepted, stop));
    }

    // A thread of its own, not the pool's: a peer must always find an instance listening, and the pool has stood still for longer than a peer waits (F.100).
    [SupportedOSPlatform("windows")]
    private static void KeepListening(
        string endpoint,
        NamedPipeServerStream first,
        NamedPipeServerStream second,
        ChannelWriter<NamedPipeServerStream> arrivals,
        Action<string> unreachable,
        CancellationToken stop)
    {
        var next = first;
        NamedPipeServerStream? spare = second;

        try
        {
            while (true)
            {
                PipeArrival.Wait(next, stop);
                arrivals.WriteAsync(next, stop).AsTask().GetAwaiter().GetResult();
                next = spare;
                spare = null;
                spare = CreateOnWindows(endpoint, PipeOptions.None);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ChannelClosedException)
        {
            // Stopping, or the answering stopped.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            unreachable(ex.Message);
        }
        finally
        {
            arrivals.TryComplete();
            next.Dispose();
            spare?.Dispose();
        }
    }

    private static async Task AnswerArrivalsAsync(
        Channel<NamedPipeServerStream> arrivals,
        Func<NamedPipeServerStream, CancellationToken, Task> answer,
        Action<string> unreachable,
        Action? goneBeforeAccepted,
        CancellationToken stop)
    {
        try
        {
            await foreach (var pipe in arrivals.Reader.ReadAllAsync(stop).ConfigureAwait(false))
            {
                await using (pipe)
                {
                    if (await ConnectedAsync(pipe, goneBeforeAccepted, stop).ConfigureAwait(false))
                    {
                        await answer(pipe, stop).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping, or the app's dispatcher ending, is how this ends.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            unreachable(ex.Message);
        }
        finally
        {
            arrivals.Writer.TryComplete();
            while (arrivals.Reader.TryRead(out var unanswered))
            {
                unanswered.Dispose();
            }
        }
    }

    // No limit, since an instance closed here lives on while its peer holds it, as a second start reading its reply does.
    private static NamedPipeServerStream CreateOnWindows(string endpoint, PipeOptions options) =>
        Create(endpoint, NamedPipeServerStream.MaxAllowedServerInstances, options);

    // Whether the instance's peer is there to answer.
    private static async Task<bool> ConnectedAsync(NamedPipeServerStream pipe, Action? goneBeforeAccepted, CancellationToken stop)
    {
        try
        {
            await pipe.WaitForConnectionAsync(stop).ConfigureAwait(false);
            return true;
        }
        catch (IOException ex) when (ex.HResult == _goneBeforeAccepted)
        {
            goneBeforeAccepted?.Invoke();
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void HangUp(NamedPipeServerStream pipe)
    {
        try
        {
            pipe.Disconnect();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // The answer already hung up.
        }
    }
}
