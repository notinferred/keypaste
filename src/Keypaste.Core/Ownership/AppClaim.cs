using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Principal;

namespace Keypaste.Core.Ownership;

/// <summary>
/// The desktop app's hold on keypaste's home for its user, so one app process runs per user and home,
/// and the endpoint a second start asks that process on to show its window (D-0397).
/// </summary>
/// <remarks>
/// <para>
/// The open <c>sessions/app.lock</c> handle is the claim, as with <see cref="VaultClaim"/>: the
/// operating system closes it when its holder dies, so a crash leaves nothing holding the home.
/// </para>
/// <para>
/// The endpoint is a pipe restricted to the current user, as the approver's is, named from the
/// user's profile and the home. A connection is the whole request and nothing is read from it, so a
/// process that reaches it can at most make the window show (THREATS.md T-29).
/// </para>
/// </remarks>
public sealed class AppClaim : IDisposable
{
    /// <summary>The claim's file in <see cref="VaultClaim.DirectoryName"/> under keypaste's home.</summary>
    public const string FileName = "app.lock";

    /// <summary>What every endpoint name starts with.</summary>
    public const string Prefix = "keypaste-app-";

    private static readonly TimeSpan _attempt = TimeSpan.FromMilliseconds(250);

    private readonly FileStream? _held;
    private int _requests;

    private AppClaim(FileStream? held, string endpoint)
    {
        _held = held;
        Endpoint = endpoint;
    }

    /// <summary>The pipe a second start reaches this process on.</summary>
    public string Endpoint { get; }

    /// <summary>How many requests to show have arrived. A status, not a decision input.</summary>
    internal int Requests => Volatile.Read(ref _requests);

    /// <summary>Takes the one app process this user may run in <paramref name="home"/>.</summary>
    /// <param name="home">keypaste's home directory.</param>
    /// <param name="claim">The claim, when no other process holds the home.</param>
    /// <returns><see langword="false"/> when another process holds the home.</returns>
    /// <remarks>
    /// A home the claim cannot be written in at all gives a claim that holds nothing, so the app still
    /// starts there, and its unlock reports the vault's claim failing for the same reason.
    /// </remarks>
    public static bool TryAcquire(string home, [NotNullWhen(true)] out AppClaim? claim)
    {
        ArgumentException.ThrowIfNullOrEmpty(home);

        claim = null;
        var endpoint = EndpointFor(home);
        var directory = Path.Combine(home, VaultClaim.DirectoryName);
        var path = Path.Combine(directory, FileName);
        FileStream? held = null;

        try
        {
            VaultClaim.CreateDirectory(directory);
            held = new FileStream(path, VaultClaim.Options(FileMode.OpenOrCreate, FileAccess.Write, FileShare.None));
            claim = new AppClaim(held, endpoint);
            held = null;
            return true;
        }
        catch (IOException) when (File.Exists(path))
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            claim = new AppClaim(null, endpoint);
            return true;
        }
        finally
        {
            held?.Dispose();
        }
    }

    /// <summary>The endpoint of the app holding <paramref name="home"/> for this user.</summary>
    /// <param name="home">keypaste's home directory.</param>
    /// <returns>A pipe name carrying a non-secret discriminator over the user and the home.</returns>
    public static string EndpointFor(string home)
    {
        ArgumentException.ThrowIfNullOrEmpty(home);

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var material = string.Join('\n', profile, VaultIdentity.Folded(PathIdentity.Canonical(home)));

        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(material), digest);

        return Prefix + Convert.ToHexStringLower(digest[..(VaultIdentity.KeyLength / 2)]);
    }

    /// <summary>Answers each start that asks this process to show its window.</summary>
    /// <param name="show">Shows the window, completing once it is shown; called on a pool thread.</param>
    /// <param name="unreachable">Told why, when the endpoint cannot be bound and no start can reach this process.</param>
    /// <returns>What stops listening.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// The endpoint is bound before this returns. A start is answered with one byte once
    /// <paramref name="show"/> completes, so a start reaching an app too busy to show its window is
    /// not told that it was shown.
    /// </remarks>
    public IDisposable Listen(Func<Task> show, Action<string> unreachable)
    {
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(unreachable);

        return new Listener(this, show, unreachable);
    }

    /// <summary>
    /// Asks the app holding <paramref name="home"/> for this user to show its window, and takes the home
    /// instead if that app quits while this waits.
    /// </summary>
    /// <param name="home">keypaste's home directory.</param>
    /// <param name="wait">How long to wait for an answer, since the app may still be starting.</param>
    /// <param name="claim">The home's claim, when the app holding it quit meanwhile.</param>
    /// <returns>Whether the running app showed its window.</returns>
    /// <remarks>
    /// An endpoint the runtime refuses, which another user's pipe or socket file is, ends the wait at
    /// once. The pipe is opened at the identification level, so whoever made it cannot act as this user.
    /// </remarks>
    public static bool AskToShow(string home, TimeSpan wait, out AppClaim? claim)
    {
        var endpoint = EndpointFor(home);
        var asked = Stopwatch.GetTimestamp();

        while (true)
        {
            var reply = Ask(endpoint, wait - Stopwatch.GetElapsedTime(asked));

            if (reply == Reply.Shown)
            {
                claim = null;
                return true;
            }

            if (TryAcquire(home, out claim) || reply == Reply.Refused || Stopwatch.GetElapsedTime(asked) >= wait)
            {
                return false;
            }
        }
    }

    /// <summary>Releases the home for another process.</summary>
    public void Dispose() => _held?.Dispose();

    // One instance at a time, which on Windows makes each the pipe's first, so a pipe somebody else made under this name is refused rather than joined.
    private static NamedPipeServerStream Create(string endpoint) =>
        new(endpoint, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private static Reply Ask(string endpoint, TimeSpan budget)
    {
        if (budget < TimeSpan.Zero)
        {
            budget = TimeSpan.Zero;
        }

        using var pipe = new NamedPipeClientStream(
            ".",
            endpoint,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            TokenImpersonationLevel.Identification);

        try
        {
            pipe.Connect(budget < _attempt ? budget : _attempt);
        }
        catch (TimeoutException)
        {
            return Reply.NotYet;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SocketException)
        {
            return Reply.Refused;
        }

        using var answered = new CancellationTokenSource(budget);
        var shown = new byte[1];

        try
        {
            return pipe.ReadAsync(shown, answered.Token).AsTask().GetAwaiter().GetResult() == 1 ? Reply.Shown : Reply.NotYet;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException)
        {
            return Reply.NotYet;
        }
    }

    private enum Reply
    {
        Shown,
        NotYet,
        Refused,
    }

    private sealed class Listener : IDisposable
    {
        private static readonly TimeSpan _stopGrace = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan _deliveryBound = TimeSpan.FromSeconds(1);
        private static readonly byte[] _shown = [1];

        private readonly CancellationTokenSource _stop = new();
        private readonly Task _accepting;

        internal Listener(AppClaim claim, Func<Task> show, Action<string> unreachable) =>
            _accepting = AcceptAsync(claim, show, unreachable, _stop.Token);

        public void Dispose()
        {
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

        private static async Task AcceptAsync(AppClaim claim, Func<Task> show, Action<string> unreachable, CancellationToken stop)
        {
            try
            {
                while (true)
                {
                    await AnswerOneAsync(claim, show, stop).ConfigureAwait(false);
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

        private static async Task AnswerOneAsync(AppClaim claim, Func<Task> show, CancellationToken stop)
        {
            await using var pipe = Create(claim.Endpoint);

            try
            {
                await pipe.WaitForConnectionAsync(stop).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SocketException)
            {
                // Another user's connection, which the runtime refuses, or a peer gone before it was accepted.
                return;
            }

            Interlocked.Increment(ref claim._requests);
            await show().WaitAsync(stop).ConfigureAwait(false);

            // A Windows pipe write waits for its reader, so a peer that never reads is given a second, not the listener.
            using var delivery = CancellationTokenSource.CreateLinkedTokenSource(stop);
            delivery.CancelAfter(_deliveryBound);

            try
            {
                await pipe.WriteAsync(_shown, delivery.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException || (ex is OperationCanceledException && !stop.IsCancellationRequested))
            {
                // The start stopped waiting, or never read.
            }
        }
    }
}
