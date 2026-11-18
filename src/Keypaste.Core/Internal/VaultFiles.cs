using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Keypaste.Core.Internal;

/// <summary>Copies and renames of vault files and their backups that are on the disk, not only in its cache, when they return.</summary>
internal static class VaultFiles
{
    private const uint _moveReplaceExisting = 0x1;
    private const uint _moveWriteThrough = 0x8;

    /// <summary>Copies <paramref name="source"/> to a new owner-only file at <paramref name="destination"/>, flushed to disk.</summary>
    /// <exception cref="IOException">The destination exists, or either file could not be used.</exception>
    internal static void Copy(string source, string destination)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using var from = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var to = new FileStream(destination, options);
        from.CopyTo(to);
        to.Flush(flushToDisk: true);
    }

    /// <summary>Renames <paramref name="source"/> to <paramref name="destination"/>, returning once the rename is on disk.</summary>
    /// <exception cref="IOException">The rename was refused; the subclasses and <see cref="UnauthorizedAccessException"/> are <see cref="File.Move(string, string, bool)"/>'s.</exception>
    internal static void Move(string source, string destination, bool overwrite)
    {
        if (OperatingSystem.IsWindows())
        {
            MoveWritingThrough(source, destination, overwrite);
            return;
        }

        File.Move(source, destination, overwrite);
        _ = SyncDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
    }

    /// <summary>Flushes a directory's entries, so a rename into it survives a power loss; nothing on Windows, whose renames write through.</summary>
    /// <returns>Whether the directory was flushed; false on Windows.</returns>
    /// <remarks>Best effort: it follows a rename that has already happened, and some file systems refuse to flush a directory.</remarks>
    internal static bool SyncDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        var descriptor = Open(System.Text.Encoding.UTF8.GetBytes(directory + '\0'), 0);

        if (descriptor < 0)
        {
            return false;
        }

        var flushed = Fsync(descriptor) == 0;
        _ = Close(descriptor);
        return flushed;
    }

    [SupportedOSPlatform("windows")]
    private static void MoveWritingThrough(string source, string destination, bool overwrite)
    {
        if (MoveFileEx(source, destination, _moveWriteThrough | (overwrite ? _moveReplaceExisting : 0)))
        {
            return;
        }

        var error = Marshal.GetLastPInvokeError();
        var message = new Win32Exception(error).Message;

        throw error switch
        {
            2 => new FileNotFoundException(message, source),
            3 => new DirectoryNotFoundException(message),
            5 => new UnauthorizedAccessException(message),
            206 => new PathTooLongException(message),
            _ => new IOException(message, unchecked((int)0x80070000) | error),
        };
    }

    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existingFileName, string newFileName, uint flags);

    [DllImport("libc", EntryPoint = "open", ExactSpelling = true, SetLastError = true)]
    private static extern int Open(byte[] nulTerminatedPath, int flags);

    [DllImport("libc", EntryPoint = "fsync", ExactSpelling = true, SetLastError = true)]
    private static extern int Fsync(int descriptor);

    [DllImport("libc", EntryPoint = "close", ExactSpelling = true, SetLastError = true)]
    private static extern int Close(int descriptor);
}
