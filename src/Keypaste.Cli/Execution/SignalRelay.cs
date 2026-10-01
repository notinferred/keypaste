using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Keypaste.Cli.Execution;

/// <summary>
/// Traps SIGINT, SIGTERM, SIGQUIT and SIGHUP from before the child starts until it is reaped, and
/// relays those <see cref="SignalPolicy"/> forwards, holding any that arrive before the child exists.
/// </summary>
internal sealed class SignalRelay : IDisposable
{
    private readonly Lock _gate = new();
    private readonly List<PosixSignal> _held = [];
    private readonly List<PosixSignalRegistration> _traps = [];
    private readonly bool _stdinRedirected;
    private Process? _child;

    /// <summary>Sets the four traps.</summary>
    /// <param name="stdinRedirected">Whether keypaste's stdin is not a terminal, as <see cref="SignalPolicy.ShouldForward"/> takes it.</param>
    internal SignalRelay(bool stdinRedirected)
    {
        _stdinRedirected = stdinRedirected;
        try
        {
            foreach (var signal in new[] { PosixSignal.SIGINT, PosixSignal.SIGTERM, PosixSignal.SIGQUIT, PosixSignal.SIGHUP })
            {
                try
                {
                    _traps.Add(PosixSignalRegistration.Create(signal, context => Handle(signal, context)));
                }
                catch (PlatformNotSupportedException)
                {
                    // A signal this platform never raises needs no trap.
                }
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>The first signal held while no child was attached, or null.</summary>
    internal PosixSignal? Held
    {
        get
        {
            lock (_gate)
            {
                return _held.Count > 0 ? _held[0] : null;
            }
        }
    }

    /// <summary>Relays the held signals to <paramref name="child"/>, and every later one as it arrives.</summary>
    /// <param name="child">The started child.</param>
    internal void Attach(Process child)
    {
        PosixSignal[] held;
        lock (_gate)
        {
            _child = child;
            held = [.. _held];
            _held.Clear();
        }

        foreach (var signal in held)
        {
            NativeSignals.TryRaise(child, signal);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var trap in _traps)
        {
            trap.Dispose();
        }
    }

    /// <summary>What a trap does with <paramref name="signal"/>.</summary>
    /// <param name="signal">The signal keypaste received.</param>
    /// <param name="context">Its context, whose <see cref="PosixSignalContext.Cancel"/> keeps keypaste alive.</param>
    internal void Handle(PosixSignal signal, PosixSignalContext context)
    {
        var forward = SignalPolicy.ShouldForward(signal, _stdinRedirected);
        Process? child;
        lock (_gate)
        {
            child = _child;
            if (child is null)
            {
                // Before the child exists, a terminal's signal ends keypaste as by default, and so does any on Windows, where none could be relayed.
                if (!forward || OperatingSystem.IsWindows())
                {
                    return;
                }

                _held.Add(signal);
            }
        }

        // keypaste outlives the signal to reap the child and report its status; on Windows closing the console still ends it.
        context.Cancel = true;

        if (forward && child is not null)
        {
            NativeSignals.TryRaise(child, signal);
        }
    }
}
