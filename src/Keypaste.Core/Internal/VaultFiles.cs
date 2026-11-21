using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

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
    private const int _fullFsync = 51;
    private const uint _renameExclusive = 0x4;
    private const uint _renameNoReplace = 0x1;
    private const int _atCurrentDirectory = -100;
    private const int _invalidArgument = 22;

    /// <summary>Copies <paramref name="source"/> to a new owner-only file at <paramref name="destination"/>, flushed to disk.</summary>
    /// <exception cref="IOException">The destination exists, or either file could not be used.</exception>
    internal static void Copy(string source, string destination)
    {
        // Shares as File.Copy did, so a vault another program holds open can still be backed up.
        using var from = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var to = new FileStream(destination, NewOwnerOnlyFile());
        from.CopyTo(to);
        Flush(to);
    }

    /// <summary>Writes <paramref name="bytes"/> to a new owner-only file, flushed to disk; a file it created and could not finish is removed.</summary>
    /// <param name="destination">The file to create.</param>
    /// <param name="bytes">Its contents.</param>
    /// <param name="beforeFlush">Called with <paramref name="destination"/> after the bytes and before the flush, so a test can fail the write as a full disk does.</param>
    /// <exception cref="IOException">The file exists or could not be written.</exception>
    internal static void WriteNew(string destination, byte[] bytes, Action<string>? beforeFlush = null)
    {
        var created = false;

        try
        {
            using var stream = new FileStream(destination, NewOwnerOnlyFile());
            created = true;
            stream.Write(bytes);
            beforeFlush?.Invoke(destination);
            Flush(stream);
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

    /// <summary>Renames <paramref name="source"/> to <paramref name="destination"/> and flushes the rename to disk.</summary>
    /// <returns>Whether the rename is on disk: false when the system would not flush the destination's folder.</returns>
    /// <exception cref="IOException">The rename was refused; the subclasses and <see cref="UnauthorizedAccessException"/> are <see cref="File.Move(string, string, bool)"/>'s.</exception>
    internal static bool Move(string source, string destination, bool overwrite)
    {
        if (OperatingSystem.IsWindows())
        {
            MoveWritingThrough(source, destination, overwrite);
            return true;
        }

        if (overwrite || !RenamedWithoutReplacing(source, destination))
        {
            File.Move(source, destination, overwrite);
        }

        return SyncDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
    }

    /// <summary>Removes a stale temporary by its name, so a hard link a crash left there to the vault is never written through.</summary>
    /// <remarks>Unlinking a name leaves every other name of the file as it was; any failure leaves it.</remarks>
    internal static void Unlink(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
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
    /// <returns>Whether the directory's entries are on disk: true on Windows, false when the directory could not be opened or flushed.</returns>
    /// <remarks>It follows a rename that has already happened, so a false return reports that rename rather than undoing it.</remarks>
    internal static bool SyncDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        var descriptor = Open(System.Text.Encoding.UTF8.GetBytes(directory + '\0'), 0);

        if (descriptor < 0)
        {
            return false;
        }

        var flushed = (OperatingSystem.IsMacOS() && Fcntl(descriptor, _fullFsync) == 0) || Fsync(descriptor) == 0;
        _ = Close(descriptor);
        return flushed;
    }

    /// <summary>Flushes a file's bytes to the drive itself: on macOS by F_FULLFSYNC, since its fsync leaves them in the drive's cache, and by fsync where the file system refuses that.</summary>
    /// <exception cref="IOException">The bytes could not be flushed.</exception>
    internal static void Flush(FileStream stream) => Flush(stream, file => OperatingSystem.IsMacOS() && FullFsync(file));

    /// <summary><see cref="Flush(FileStream)"/> with the F_FULLFSYNC call given, so a test can stand in for a file system that refuses it.</summary>
    internal static void Flush(FileStream stream, Func<SafeFileHandle, bool> fullFsync)
    {
        if (OperatingSystem.IsMacOS())
        {
            stream.Flush(flushToDisk: false);

            if (fullFsync(stream.SafeFileHandle))
            {
                return;
            }
        }

        stream.Flush(flushToDisk: true);
    }

    /// <summary>Asks macOS to write the file's bytes past the drive's cache.</summary>
    /// <returns>Whether it did: false where the file system does not support F_FULLFSYNC.</returns>
    [SupportedOSPlatform("macos")]
    internal static bool FullFsync(SafeFileHandle file)
    {
        var added = false;

        try
        {
            file.DangerousAddRef(ref added);
            return Fcntl((int)file.DangerousGetHandle(), _fullFsync) == 0;
        }
        finally
        {
            if (added)
            {
                file.DangerousRelease();
            }
        }
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

    /// <summary>Renames in one step that refuses an existing destination, where .NET's no-overwrite move links and then unlinks.</summary>
    /// <returns>False when this system or file system has no such rename, so the caller moves as .NET does.</returns>
    /// <exception cref="IOException">The rename was refused, an existing destination among other reasons.</exception>
    [UnsupportedOSPlatform("windows")]
    private static bool RenamedWithoutReplacing(string source, string destination)
    {
        var from = System.Text.Encoding.UTF8.GetBytes(source + '\0');
        var to = System.Text.Encoding.UTF8.GetBytes(destination + '\0');
        int result;

        try
        {
            result = OperatingSystem.IsMacOS() ? RenameExclusive(from, to, _renameExclusive) : RenameAt2(_atCurrentDirectory, from, _atCurrentDirectory, to, _renameNoReplace);
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }

        if (result == 0)
        {
            return true;
        }

        var error = Marshal.GetLastPInvokeError();
        var (notImplemented, notSupported) = OperatingSystem.IsMacOS() ? (78, 45) : (38, 95);

        if (error == _invalidArgument || error == notImplemented || error == notSupported)
        {
            return false;
        }

        var message = Marshal.GetPInvokeErrorMessage(error);

        throw error switch
        {
            2 => new FileNotFoundException(message, source),
            1 or 13 => new UnauthorizedAccessException(message),
            _ => new IOException(message, error),
        };
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

    // Declared without fcntl's variadic tail, which F_FULLFSYNC does not take, since arm64 macOS passes variadic arguments differently.
    [DllImport("libc", EntryPoint = "fcntl", ExactSpelling = true, SetLastError = true)]
    private static extern int Fcntl(int descriptor, int command);

    [DllImport("libc", EntryPoint = "renamex_np", ExactSpelling = true, SetLastError = true)]
    private static extern int RenameExclusive(byte[] nulTerminatedFrom, byte[] nulTerminatedTo, uint flags);

    [DllImport("libc", EntryPoint = "renameat2", ExactSpelling = true, SetLastError = true)]
    private static extern int RenameAt2(int fromDirectory, byte[] nulTerminatedFrom, int toDirectory, byte[] nulTerminatedTo, uint flags);

    [DllImport("libc", EntryPoint = "close", ExactSpelling = true, SetLastError = true)]
    private static extern int Close(int descriptor);
}
