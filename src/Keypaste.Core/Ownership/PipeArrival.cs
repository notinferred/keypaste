using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Keypaste.Core.Ownership;

/// <summary>Waits for a peer to reach a pipe instance on the calling thread.</summary>
/// <remarks>.NET completes a wait for a connection on the thread pool, which may have no thread for the arrival (F.100).</remarks>
[SupportedOSPlatform("windows")]
internal static class PipeArrival
{
    private const int _pipeConnected = 535;
    private const int _noData = 232;
    private const int _ioPending = 997;

    // Bit 0 of the event keeps the completion off the port .NET bound the handle to.
    private const nint _skipCompletionPort = 1;

    /// <summary>Returns once a peer has reached <paramref name="pipe"/>, whether or not it is still connected.</summary>
    /// <exception cref="OperationCanceledException"><paramref name="stop"/> was cancelled first.</exception>
    /// <exception cref="IOException">The instance cannot be waited on.</exception>
    internal static void Wait(NamedPipeServerStream pipe, CancellationToken stop)
    {
        using var arrived = new ManualResetEvent(initialState: false);
        var handle = pipe.SafePipeHandle.DangerousGetHandle();
        var overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());

        try
        {
            Marshal.StructureToPtr(new NativeOverlapped { EventHandle = arrived.SafeWaitHandle.DangerousGetHandle() | _skipCompletionPort }, overlapped, fDeleteOld: false);

            var alreadyReached = ConnectNamedPipe(handle, overlapped);
            var error = Marshal.GetLastPInvokeError();
            if (alreadyReached || error is _pipeConnected or _noData)
            {
                return;
            }

            if (error != _ioPending)
            {
                throw new IOException(Marshal.GetPInvokeErrorMessage(error));
            }

            if (WaitHandle.WaitAny([arrived, stop.WaitHandle]) == 1)
            {
                _ = CancelIoEx(handle, overlapped);
                arrived.WaitOne();
                stop.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            Marshal.FreeHGlobal(overlapped);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConnectNamedPipe(nint handle, nint overlapped);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CancelIoEx(nint handle, nint overlapped);
}
