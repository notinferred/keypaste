using System.Runtime.InteropServices;
using Keypaste.Core.Ownership;

namespace Keypaste.App;

/// <summary>What a start on macOS asks of this Mac's login session before it initializes Avalonia.</summary>
internal interface IMacSession
{
    /// <summary>Gets whether a display is awake, so that Avalonia's render timer can start.</summary>
    bool DisplayIsAwake { get; }

    /// <summary>Calls <paramref name="ending"/> if the session asks this process to end, until the returned object is disposed.</summary>
    /// <param name="ending">Told once a logout, a shutdown or a terminal asks the process to end.</param>
    /// <returns>What stops listening and gives the signals back to the runtime.</returns>
    IDisposable OnEnding(Action ending);
}

/// <summary>
/// Holds a start on macOS, showing nothing, until a display is awake, because Avalonia cannot initialize without one (F.96).
/// </summary>
/// <remarks>
/// The wait has no bound: a Mac can sit with its display asleep for days, and a start that gave up would leave a login with no app.
/// It runs under the home's claim, so a start handed to the running copy never reaches it, and before the crash reports start, so a logout during it leaves no run marker.
/// </remarks>
internal sealed class DisplayWait
{
    // Avalonia.Native's wording; AppBuilder allows one setup per process, so this failure cannot be retried in place.
    private const string _renderTimerFailure = "Avalonia.Native was not able to start the RenderTimer";

    private readonly IMacSession _session;
    private readonly TextWriter _stderr;
    private readonly TimeSpan _poll;

    /// <summary>Initializes the wait.</summary>
    /// <param name="session">The login session asked.</param>
    /// <param name="stderr">Where a start that waits or fails says so.</param>
    /// <param name="poll">How long to wait between looks at the display.</param>
    internal DisplayWait(IMacSession session, TextWriter stderr, TimeSpan poll)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(stderr);

        _session = session;
        _stderr = stderr;
        _poll = poll;
    }

    /// <summary>Puts this wait before <paramref name="start"/>.</summary>
    /// <param name="start">Runs the app under its claim and returns its exit code.</param>
    /// <returns>A start that runs the app once a display is awake, returns 0 when the session ends first and 1 when the display went away as Avalonia initialized.</returns>
    internal Func<AppClaim, int> Before(Func<AppClaim, int> start)
    {
        ArgumentNullException.ThrowIfNull(start);

        return claim =>
        {
            if (!_session.DisplayIsAwake && !WaitForDisplay())
            {
                return 0;
            }

            try
            {
                return start(claim);
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith(_renderTimerFailure, StringComparison.Ordinal))
            {
                _stderr.WriteLine("keypaste could not start because this Mac's display went to sleep as it started; open it again.");
                return 1;
            }
        };
    }

    private bool WaitForDisplay()
    {
        _stderr.WriteLine("keypaste will start once this Mac's display wakes.");

        var ended = new TaskCompletionSource();
        using var listening = _session.OnEnding(() => ended.TrySetResult());

        while (!ended.Task.Wait(_poll))
        {
            if (_session.DisplayIsAwake)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>This Mac's login session, through the call Avalonia's render timer makes and the signals that end a process.</summary>
internal sealed class MacSession : IMacSession
{
    private const string _coreVideo = "/System/Library/Frameworks/CoreVideo.framework/CoreVideo";

    /// <inheritdoc/>
    /// <remarks>Asks the very call that fails with -6661 while every display sleeps, rather than a display state that might disagree with it.</remarks>
    public bool DisplayIsAwake
    {
        get
        {
            var made = CreateLinkForActiveDisplays(out var link) == 0;

            if (made)
            {
                ReleaseLink(link);
            }

            return made;
        }
    }

    /// <inheritdoc/>
    public IDisposable OnEnding(Action ending) => new EndingSignals(ending);

    [DllImport(_coreVideo, EntryPoint = "CVDisplayLinkCreateWithActiveCGDisplays")]
    private static extern int CreateLinkForActiveDisplays(out IntPtr link);

    [DllImport(_coreVideo, EntryPoint = "CVDisplayLinkRelease")]
    private static extern void ReleaseLink(IntPtr link);

    private sealed class EndingSignals : IDisposable
    {
        private readonly PosixSignalRegistration[] _registrations;

        internal EndingSignals(Action ending)
        {
            _registrations = Array.ConvertAll(
                [PosixSignal.SIGTERM, PosixSignal.SIGINT, PosixSignal.SIGHUP],
                signal => PosixSignalRegistration.Create(signal, context =>
                {
                    context.Cancel = true;
                    ending();
                }));
        }

        public void Dispose()
        {
            foreach (var registration in _registrations)
            {
                registration.Dispose();
            }
        }
    }
}
