using System.IO.Pipes;
using System.Net.Sockets;

namespace Keypaste.Core.Ownership;

/// <summary>
/// Accepts, one at a time, connections to a pipe restricted to the current user where a connection is the
/// whole request, as a second start's to the app and a bridge's to a locked app are.
/// </summary>
/// <remarks>
/// The pipe is bound before the constructor returns. A connection the runtime refuses, which another user's
/// is (THREATS.md T-10), and a peer gone before it was accepted are not answered, and the next is accepted.
/// On Windows the pipe admits only this user, so a peer gone before it was accepted can still be told of.
/// </remarks>
internal sealed class KnockListener : IDisposable
{
    private static readonly TimeSpan _stopGrace = TimeSpan.FromSeconds(2);

    // ERROR_NO_DATA: the peer connected and closed before the pipe waited for it.
    private const int _goneBeforeAccepted = unchecked((int)0x800700E8);

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
        _accepting = AcceptAsync(endpoint, answer, unreachable, goneBeforeAccepted, _stop.Token);

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

        _stop.Dispose();
    }

    // One instance at a time, which on Windows makes each the pipe's first, so a pipe somebody else made under this name is refused rather than joined.
    private static NamedPipeServerStream Create(string endpoint) =>
        new(endpoint, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private static async Task AcceptAsync(
        string endpoint,
        Func<NamedPipeServerStream, CancellationToken, Task> answer,
        Action<string> unreachable,
        Action? goneBeforeAccepted,
        CancellationToken stop)
    {
        try
        {
            while (true)
            {
                await AnswerOneAsync(endpoint, answer, goneBeforeAccepted, stop).ConfigureAwait(false);
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

    private static async Task AnswerOneAsync(
        string endpoint,
        Func<NamedPipeServerStream, CancellationToken, Task> answer,
        Action? goneBeforeAccepted,
        CancellationToken stop)
    {
        await using var pipe = Create(endpoint);

        try
        {
            await pipe.WaitForConnectionAsync(stop).ConfigureAwait(false);
        }
        catch (IOException ex) when (goneBeforeAccepted is not null && OperatingSystem.IsWindows() && ex.HResult == _goneBeforeAccepted)
        {
            goneBeforeAccepted();
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SocketException)
        {
            // Another user's connection, which the runtime refuses, or a peer gone before it was accepted.
            return;
        }

        await answer(pipe, stop).ConfigureAwait(false);
    }
}
