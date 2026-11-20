using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Keypaste.Core.Internal;

/// <summary>Every write, copy, rename and removal of a vault file, its exports and its backups, each on the disk when it returns.</summary>
/// <remarks>
/// The one exception is the save's own replacement, which KeePassLib's file transaction makes inside <c>KeePassInterop.Replace</c>,
/// the only file that may reach KeePassLib; <c>VaultFilesSourceRulesTests</c> holds both rules.
/// </remarks>
internal static class VaultFiles
{
    private const uint _moveReplaceExisting = 0x1;
    private const uint _moveWriteThrough = 0x8;

    /// <summary>Copies <paramref name="source"/> to a new owner-only file at <paramref name="destination"/>, flushed to disk.</summary>
    /// <exception cref="IOException">The destination exists, or either file could not be used.</exception>
    internal static void Copy(string source, string destination)
    {
        // Shares as File.Copy did, so a vault another program holds open can still be backed up.
        using var from = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var to = new FileStream(destination, NewOwnerOnlyFile());
        from.CopyTo(to);
        to.Flush(flushToDisk: true);
    }

    /// <summary>Writes <paramref name="bytes"/> to a new owner-only file, flushed to disk; a file it created and could not finish is removed.</summary>
    /// <exception cref="IOException">The file exists or could not be written.</exception>
    internal static void WriteNew(string destination, byte[] bytes)
    {
        var created = false;

        try
        {
            using var stream = new FileStream(destination, NewOwnerOnlyFile());
            created = true;
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        catch (Exception ex) when (created && ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            try
            {
                File.Delete(destination);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
            }

            throw;
        }
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

    /// <summary>Removes a backup.</summary>
    /// <exception cref="IOException">It could not be removed.</exception>
    internal static void Delete(string path) => File.Delete(path);

    /// <summary>Removes a temporary file at exactly this path, unless another handle holds it; any failure leaves it.</summary>
    internal static void Discard(string path)
    {
        try
        {
            using var _ = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
        }
    }

    /// <summary>Creates a directory for a vault or its backups, reachable by its owner alone when <paramref name="ownerOnly"/> and the platform says so.</summary>
    /// <exception cref="IOException">It could not be created.</exception>
    internal static void CreateDirectory(string directory, bool ownerOnly)
    {
        Directory.CreateDirectory(directory);

        if (ownerOnly && !OperatingSystem.IsWindows())
        {
            Restrict(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>Makes a backup readable by its owner alone, where the platform says so.</summary>
    internal static void RestrictToOwner(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            Restrict(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    /// <summary>The file a save or restore replaces: the final target when <paramref name="path"/> is a symbolic link, so the link stays one.</summary>
    /// <returns><paramref name="path"/> itself when it is not a link or nothing is there yet.</returns>
    /// <exception cref="IOException">The links loop.</exception>
    internal static string WriteTarget(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return path;
        }
    }

    /// <summary>Gives the file about to replace a vault the vault's own permissions, which the rename would otherwise lose.</summary>
    internal static void KeepMode(FileStream replacement, string vaultPath)
    {
        if (!OperatingSystem.IsWindows() && File.Exists(vaultPath))
        {
            File.SetUnixFileMode(replacement.SafeFileHandle, File.GetUnixFileMode(vaultPath));
        }
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

    private static FileStreamOptions NewOwnerOnlyFile()
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return options;
    }

    [UnsupportedOSPlatform("windows")]
    private static void Restrict(string path, UnixFileMode mode)
    {
        try
        {
            File.SetUnixFileMode(path, mode);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file is encrypted either way; a mode this file system will not hold does not make it worthless (T-24).
        }
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
