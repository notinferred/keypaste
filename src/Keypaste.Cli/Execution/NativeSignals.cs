using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Keypaste.Cli.Execution;

/// <summary>Sends a signal to a child process on Unix.</summary>
/// <remarks>
/// <para>
/// The only native interop in keypaste, and the reason it exists is that .NET has no managed way
/// to send a specific signal: <see cref="Process.Kill()"/> sends SIGKILL, which is not a signal
/// keypaste is entitled to send on the user's behalf. Wrapping <c>npm start</c> or a database and
/// then hard-killing it on <c>docker stop</c> would lose work the child was in the middle of
/// flushing. So keypaste relays what it was sent, and never escalates.
/// </para>
/// <para>
/// This adds no dependency in the sense CORE.md law 3.9 means: no package, nothing to pin, nothing
/// new on the supply chain. <c>src/</c> still carries zero <c>PackageReference</c> entries
/// (DECISIONS.md D-0004). <see cref="LibraryImportAttribute"/> is source-generated over a fully
/// blittable signature, so there is no reflection and no marshalling stub, and it stays
/// AOT-compatible. The four signal numbers are identical on Linux, macOS and the BSDs; only
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
            // Narrows the window in which the runtime has already reaped the child and the pid has
            // been handed to somebody else. It does not close it — this is the same race that
            // `kill $!` has in every shell script ever written.
            if (child.HasExited)
            {
                return;
            }

            // The result is deliberately discarded. Every way this fails — the child exited a
            // moment ago, the pid is no longer ours — is a reason to carry on waiting for its exit
            // status, not to start reporting errors in the middle of a shutdown.
            _ = Kill(child.Id, number);
        }
        catch (InvalidOperationException)
        {
            // The process was never started, or has already been cleaned up.
        }
    }

    private static int? Number(PosixSignal signal) => signal switch
    {
        PosixSignal.SIGHUP => SigHup,
        PosixSignal.SIGINT => SigInt,
        PosixSignal.SIGQUIT => SigQuit,
        PosixSignal.SIGTERM => SigTerm,
        _ => null,
    };

    // DllImport rather than LibraryImport, which SYSLIB1054 would otherwise prefer: the source
    // generator emits an unsafe stub, and turning <AllowUnsafeBlocks> on for the whole CLI to
    // obtain one two-integer call is a much wider change than the call itself. The signature is
    // fully blittable, so there is no marshalling to generate and nothing here that NativeAOT
    // cannot compile ahead of time.
#pragma warning disable SYSLIB1054
    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int pid, int signal);
#pragma warning restore SYSLIB1054
}
