using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Keypaste.Cli.Clipboard;

/// <summary>
/// Waits in the foreground, then clears the clipboard if it still holds our secret.
/// </summary>
/// <remarks>
/// <para>
/// The known cost, stated here rather than discovered later: <b>this holds the terminal for the
/// whole delay</b>, so <c>keypaste get x | foo</c> makes <c>foo</c> wait. That is precisely the
/// complaint against <c>keepassxc-cli clip</c> (upstream issue #3855). Status goes to stderr so
/// piping still <i>works</i>; it does not make it fast. <c>--timeout 0</c> skips the wait
/// entirely, and <see cref="IClipboardClearStrategy"/> is where a detached implementation goes.
/// </para>
/// <para>
/// <b>What no design here survives: SIGKILL.</b> <c>kill -9</c>, End Task, a power cut or an OOM
/// kill leaves the password on the clipboard until something overwrites it. Two further gaps
/// belong in the same breath: on X11 and Wayland the secret also lives in the forked
/// <c>wl-copy</c>/<c>xclip</c> daemon, because those clipboards are owner-served; and Windows
/// clipboard history keeps a copy that clearing does not remove.
/// </para>
/// </remarks>
internal sealed class BlockingClearStrategy : IClipboardClearStrategy
{
    private readonly TimeProvider _clock;

    internal BlockingClearStrategy(TimeProvider clock)
    {
        _clock = clock;
    }

    /// <inheritdoc/>
    public void ClearAfter(
        IClipboard clipboard,
        byte[] expectedHash,
        TimeSpan delay,
        TextWriter status)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(expectedHash);
        ArgumentNullException.ThrowIfNull(status);

        using var interrupted = new ManualResetEventSlim(false);

        // Both routes are registered because neither covers everything: CancelKeyPress catches
        // Ctrl+C and Ctrl+Break, and the POSIX registrations add kill, container stop, systemd,
        // the terminal window closing, and an SSH session dropping. Handlers do nothing but set
        // the event — the clear runs once, on this thread, because spawning a subprocess from a
        // signal handler races a teardown keypaste does not control.
        void OnCancel(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            interrupted.Set();
        }

        Console.CancelKeyPress += OnCancel;
        var signals = RegisterSignals(interrupted);

        try
        {
            status.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "Copied to clipboard. Clearing in {0}s (Ctrl+C to clear now).",
                (int)delay.TotalSeconds));

            Wait(delay, interrupted);

            // On the interrupt path the read-back is skipped and the clear is unconditional: a
            // Windows console control handler gets only a couple of seconds before the process
            // is killed anyway, and "cleared something the user copied since" beats "left a
            // password on the clipboard".
            var verify = !interrupted.IsSet;
            Clear(clipboard, expectedHash, verify, status);
        }
        finally
        {
            Console.CancelKeyPress -= OnCancel;
            foreach (var registration in signals)
            {
                registration.Dispose();
            }
        }
    }

    private void Wait(TimeSpan delay, ManualResetEventSlim interrupted)
    {
        // TimeProvider rather than Thread.Sleep so a fake clock makes the whole wait instant in
        // tests. Without this seam none of the auto-clear behaviour would be testable at all.
        using var timer = _clock.CreateTimer(
            _ => interrupted.Set(),
            null,
            delay,
            System.Threading.Timeout.InfiniteTimeSpan);

        interrupted.Wait();
    }

    private static void Clear(
        IClipboard clipboard,
        byte[] expectedHash,
        bool verify,
        TextWriter status)
    {
        if (verify)
        {
            var read = clipboard.TryReadHash(out var current, out _);

            // Failing to read back means clear anyway. Skipping the clear because verification
            // failed would leave a password on the clipboard indefinitely, which is the worst
            // outcome available here — fail closed, CORE.md law 3.7.
            if (read == ClipboardStatus.Ok
                && !CryptographicOperations.FixedTimeEquals(current, expectedHash))
            {
                status.WriteLine("Clipboard changed since the copy; leaving it alone.");
                return;
            }
        }

        var cleared = clipboard.TryClear(out var error);
        status.WriteLine(cleared == ClipboardStatus.Ok
            ? "Clipboard cleared."
            : $"Could not clear the clipboard: {error}");
    }

    private static List<PosixSignalRegistration> RegisterSignals(ManualResetEventSlim interrupted)
    {
        List<PosixSignalRegistration> registrations = [];

        foreach (var signal in new[] { PosixSignal.SIGINT, PosixSignal.SIGTERM, PosixSignal.SIGHUP })
        {
            try
            {
                registrations.Add(PosixSignalRegistration.Create(signal, context =>
                {
                    context.Cancel = true;
                    interrupted.Set();
                }));
            }
            catch (PlatformNotSupportedException)
            {
                // One fewer route to the same handler is not an error.
            }
        }

        return registrations;
    }
}
