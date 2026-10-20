using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Principal;

namespace Keypaste.Core.Ownership;

/// <summary>
/// The endpoint the desktop app listens on for a vault while it is locked, which a bridge about to refuse
/// its agent for want of an owner connects to, so the app can tell the person an agent asked (D-0466).
/// </summary>
/// <remarks>
/// <para>
/// A pipe restricted to the current user, as the approver's is, named from the vault's identity alone, so
/// <c>KEYPASTE_APPROVER</c> and <c>--approver</c> never move it. A connection is the whole request: nothing
/// is written to it, and nothing sent on it is used, so a process that reaches it can at most make the app
/// say that an agent asked (THREATS.md T-11). The bridge hangs up as soon as it has connected; the app hangs
/// up at once, or on Windows once the bridge has, ending a connection still open after 100 ms.
/// </para>
/// <para>
/// Each side refuses a peer running as another user, as on the approver's endpoint (T-10). Another user who
/// takes the name first stops only the notice: the bridge's connection is refused, and its agent is told
/// what it would be told with no app open.
/// </para>
/// </remarks>
public sealed class LockedAppEndpoint : IDisposable
{
    /// <summary>What every locked app endpoint's name starts with.</summary>
    public const string Prefix = "keypaste-locked-";

    private static readonly TimeSpan _connectBound = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan _hangUpBound = TimeSpan.FromMilliseconds(100);

    private readonly KnockListener _listener;
    private int _requests;

    private LockedAppEndpoint(string name, Action asked, Action<string> unreachable)
    {
        Name = name;
        // A bridge hangs up as soon as it connects, so one that lands before the app waits again is still a knock (F.66).
        _listener = new KnockListener(name, (pipe, stop) => HearAsync(pipe, asked, stop), unreachable, () => Heard(asked));
    }

    /// <summary>The pipe this listens on.</summary>
    public string Name { get; }

    /// <summary>How many bridges have connected. A status, not a decision input.</summary>
    internal int Requests => Volatile.Read(ref _requests);

    /// <summary>The endpoint a locked app listens on for <paramref name="vault"/>.</summary>
    /// <param name="vault">The vault, as every keypaste process names it.</param>
    /// <returns><see cref="Prefix"/> and the vault's key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="vault"/> is null.</exception>
    /// <remarks>As long as the launch endpoint's name, which keeps the socket's path, under the temporary folder, inside macOS's 104 bytes.</remarks>
    public static string NameFor(VaultIdentity vault)
    {
        ArgumentNullException.ThrowIfNull(vault);

        return Prefix + vault.Key;
    }

    /// <summary>Listens on <paramref name="name"/> for bridges that find the app locked.</summary>
    /// <param name="name">From <see cref="NameFor"/>.</param>
    /// <param name="asked">Told each time a bridge connects; called on a pool thread.</param>
    /// <param name="unreachable">Told why, when the endpoint cannot be bound and no bridge can reach this app.</param>
    /// <returns>The endpoint, bound before this returns; disposing it stops listening.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException">A delegate is null.</exception>
    public static LockedAppEndpoint Listen(string name, Action asked, Action<string> unreachable)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(asked);
        ArgumentNullException.ThrowIfNull(unreachable);

        return new LockedAppEndpoint(name, asked, unreachable);
    }

    /// <summary>Tells the app listening on <paramref name="name"/> that an agent asked.</summary>
    /// <param name="name">From <see cref="NameFor"/>.</param>
    /// <param name="cancellationToken">Cancelled before the connection, it stops the call from being made.</param>
    /// <returns>
    /// Whether an app was reached. False, and never an exception, when nothing listens, the runtime refuses
    /// the endpoint, or <paramref name="cancellationToken"/> was cancelled first.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or empty.</exception>
    /// <remarks>
    /// Connecting is given 200 ms, and the bridge hangs up as soon as it has connected. The connection is made on a thread of its own, never the pool, for the reason
    /// <see cref="Ipc.ApproverClient.TryConnectAsync"/> gives (F.9). The pipe is opened at the identification
    /// level, so whoever made it cannot act as this user.
    /// </remarks>
    public static Task<bool> TellAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return Task.Factory.StartNew(
            () => Tell(name, cancellationToken),
            CancellationToken.None,
            TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
            TaskScheduler.Default);
    }

    /// <summary>Stops listening.</summary>
    public void Dispose() => _listener.Dispose();

    private static bool Tell(string name, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        using var pipe = new NamedPipeClientStream(
            ".",
            name,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            TokenImpersonationLevel.Identification);

        try
        {
            pipe.Connect(_connectBound);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException or SocketException)
        {
            return false;
        }

        return true;
    }

    private void Heard(Action asked)
    {
        Interlocked.Increment(ref _requests);
        asked();
    }

    private async Task HearAsync(NamedPipeServerStream pipe, Action asked, CancellationToken stop)
    {
        Heard(asked);

        // On Windows the bridge hangs up first: ending the connection under it fails its runtime's check of who
        // made the pipe. Elsewhere the app hangs up at once, since a bridge queued behind a peer that holds on is
        // dropped when the endpoint is made anew.
        if (OperatingSystem.IsWindows())
        {
            await AwaitHangUpAsync(pipe, stop).ConfigureAwait(false);
        }

        try
        {
            pipe.Disconnect();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // The bridge already hung up.
        }
    }

    private static async Task AwaitHangUpAsync(NamedPipeServerStream pipe, CancellationToken stop)
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(stop);
        bound.CancelAfter(_hangUpBound);
        var discarded = new byte[256];

        try
        {
            while (await pipe.ReadAsync(discarded, bound.Token).ConfigureAwait(false) > 0)
            {
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
            // Still connected at the bound, stopping, or gone.
        }
    }
}
