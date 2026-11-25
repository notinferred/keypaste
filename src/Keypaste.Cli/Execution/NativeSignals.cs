using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Keypaste.Cli.Execution;

/// <summary>Sends a signal to a child process on Unix.</summary>
/// <remarks>
/// <para>
/// It exists because .NET has no managed way to send a specific signal: <see cref="Process.Kill()"/> sends SIGKILL, which is not a signal
/// keypaste is entitled to send on the user's behalf. Wrapping <c>npm start</c> or a database and
/// then hard-killing it on <c>docker stop</c> would lose work the child was in the middle of
/// flushing. So keypaste relays what it was sent, and never escalates.
/// </para>
/// <para>
/// This adds no dependency in the sense SECURITY.md law 9 means: no package, nothing to pin, nothing
/// new on the supply chain. The four signal numbers are identical on Linux, macOS and the BSDs; only
/// <c>SIGUSR*</c> and the realtime signals diverge, and none of those are relayed.
/// </para>
/// </remarks>
internal static class NativeSignals
{
    internal const int SigHup = 1;
    internal const int SigInt = 2;
    internal const int SigQuit = 3;
    internal const int SigTerm = 15;

    /// <summary>Delivers <paramref name="signal"/> to <paramref name="child"/>, best effort.</summary>
    /// <remarks>
    /// Failure is ignored on purpose. Every reason this can fail — the child exited a moment ago,
    /// it is not ours any more, the platform has no such call — is a reason to carry on waiting
    /// for its exit status rather than to start reporting errors during a shutdown.
    /// </remarks>
    internal static void TryRaise(Process child, PosixSignal signal)
    {
        if (OperatingSystem.IsWindows() || Number(signal) is not { } number)
        {
            return;
        }

        try
        {
            // Narrows, but cannot close, the window in which a reaped child's pid is reused: the
            // race `kill $!` has too.
            if (child.HasExited)
            {
                return;
            }

            _ = Kill(child.Id, number);
        }
        catch (InvalidOperationException)
        {
            // The process was never started, or has already been cleaned up.
        }
    }

    /// <summary>The Unix number of <paramref name="signal"/>, or null for one keypaste never relays.</summary>
    /// <param name="signal">The signal.</param>
    /// <returns>Its number.</returns>
    internal static int? Number(PosixSignal signal) => signal switch
    {
        PosixSignal.SIGHUP => SigHup,
        PosixSignal.SIGINT => SigInt,
        PosixSignal.SIGQUIT => SigQuit,
        PosixSignal.SIGTERM => SigTerm,
        _ => null,
    };

    // DllImport rather than LibraryImport, which SYSLIB1054 prefers: its generator emits an unsafe
    // stub, and turning <AllowUnsafeBlocks> on for the whole CLI to obtain one two-integer call is a
    // wider change than the call. The signature is blittable, so NativeAOT needs no marshalling.
#pragma warning disable SYSLIB1054
    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int pid, int signal);
#pragma warning restore SYSLIB1054
}
