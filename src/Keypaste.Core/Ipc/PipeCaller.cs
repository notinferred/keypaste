using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Keypaste.Core.Audit;
using Microsoft.Win32.SafeHandles;

namespace Keypaste.Core.Ipc;

/// <summary>Which program is on the other end of an owner's pipe, read once as its connection is accepted.</summary>
/// <remarks>
/// <para>
/// The process id is the operating system's: <c>GetNamedPipeClientProcessId</c> on Windows, and on the Unix
/// socket .NET's pipe is built on, <c>SO_PEERCRED</c> on Linux and <c>LOCAL_PEERPID</c> on macOS. The path is
/// read from that id afterwards, so a process that exits and has its id reused in between is misnamed; the
/// audit token macOS also offers would close that gap but is not read here.
/// </para>
/// <para>
/// <c>DllImport</c> rather than <c>LibraryImport</c> for the reason <c>NativeSignals</c> gives: every
/// signature is blittable, and the generator would need unsafe code project-wide.
/// </para>
/// </remarks>
internal static class PipeCaller
{
    private const int _solSocket = 1;
    private const int _soPeerCredentials = 17;
    private const int _solLocal = 0;
    private const int _localPeerPid = 2;
    private const int _macPathBytes = 4096;
    private const uint _queryLimitedInformation = 0x1000;

    private static readonly AsyncLocal<AuditCaller?> _current = new();

    private static readonly ICodeSignatures? _signatures =
        OperatingSystem.IsWindows() ? new WindowsCodeSignatures()
        : OperatingSystem.IsMacOS() ? new MacCodeSignatures()
        : null;

    /// <summary>The program on the connection whose request is being answered, which <see cref="ApproverListener"/> sets for all its requests run; null outside one.</summary>
    internal static AuditCaller? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }

    /// <summary>The program connected to <paramref name="pipe"/>, as far as the system says; never throws.</summary>
    /// <param name="pipe">A connected server end.</param>
    /// <returns>The caller, its signature <see cref="CallerSignature.Unknown"/> where it could not be checked.</returns>
    internal static AuditCaller Of(NamedPipeServerStream pipe) => Of(pipe, _signatures);

    /// <summary>The same, checking signatures with <paramref name="signatures"/>, or with none where the system has no code signing.</summary>
    internal static AuditCaller Of(NamedPipeServerStream pipe, ICodeSignatures? signatures)
    {
        ArgumentNullException.ThrowIfNull(pipe);

        int? pid = null;
        string? path = null;

        try
        {
            pid = PidOf(pipe.SafePipeHandle);
            path = pid is { } known ? PathOf(known) : null;
        }
        catch (Exception)
        {
            // Naming the caller is advice, so a failure leaves what was read and never ends the connection.
        }

        if (signatures is null)
        {
            return AuditCaller.Of(pid, path, null, null);
        }

        if (path is null)
        {
            return AuditCaller.Of(pid, null, CallerSignature.Unknown, null);
        }

        try
        {
            var (state, signer) = signatures.Check(path);
            return AuditCaller.Of(pid, path, state, signer);
        }
        catch (Exception)
        {
            return AuditCaller.Of(pid, path, CallerSignature.Unknown, null);
        }
    }

    private static int? PidOf(SafePipeHandle handle)
    {
        if (OperatingSystem.IsWindows())
        {
            return GetNamedPipeClientProcessId(handle, out var pid) ? (int)pid : null;
        }

        var socket = (int)handle.DangerousGetHandle();

        if (OperatingSystem.IsLinux())
        {
            var length = (uint)Marshal.SizeOf<PeerCredentials>();
            return getsockopt(socket, _solSocket, _soPeerCredentials, out PeerCredentials credentials, ref length) == 0 ? credentials.Pid : null;
        }

        if (OperatingSystem.IsMacOS())
        {
            var length = (uint)sizeof(int);
            return getsockopt(socket, _solLocal, _localPeerPid, out int pid, ref length) == 0 ? pid : null;
        }

        return null;
    }

    private static string? PathOf(int pid)
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsPathOf(pid);
        }

        if (OperatingSystem.IsLinux())
        {
            return new FileInfo(Invariant($"/proc/{pid}/exe")).LinkTarget;
        }

        if (OperatingSystem.IsMacOS())
        {
            var buffer = new byte[_macPathBytes];
            var length = proc_pidpath(pid, buffer, (uint)buffer.Length);
            return length > 0 ? System.Text.Encoding.UTF8.GetString(buffer, 0, length) : null;
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static string? WindowsPathOf(int pid)
    {
        var process = OpenProcess(_queryLimitedInformation, inheritHandle: false, (uint)pid);

        if (process == 0)
        {
            return null;
        }

        try
        {
            var buffer = new char[32768];
            var length = buffer.Length;
            return QueryFullProcessImageNameW(process, 0, buffer, ref length) ? new string(buffer, 0, length) : null;
        }
        finally
        {
            _ = CloseHandle(process);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PeerCredentials
    {
        public int Pid;
        public uint Uid;
        public uint Gid;
    }

    [DllImport("libc", EntryPoint = "getsockopt", SetLastError = true)]
    private static extern int getsockopt(int socket, int level, int option, out PeerCredentials value, ref uint length);

    [DllImport("libc", EntryPoint = "getsockopt", SetLastError = true)]
    private static extern int getsockopt(int socket, int level, int option, out int value, ref uint length);

    [DllImport("libc", EntryPoint = "proc_pidpath", SetLastError = true)]
    private static extern int proc_pidpath(int pid, [Out] byte[] buffer, uint size);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(nint process, uint flags, [Out] char[] name, ref int size);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
