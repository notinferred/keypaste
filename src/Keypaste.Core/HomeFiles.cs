using Keypaste.Infrastructure;

namespace Keypaste.Core;

/// <summary>
/// Every write keypaste makes under its home (<see cref="Audit.KeypasteHome"/>): files replaced whole in one rename, the locks and owner
/// files that order its processes, the audit log's appends, and the moves and deletions among them, so what the home holds in the
/// clear is decided in the classes that call these and no other file can add to it.
/// </summary>
internal static class HomeFiles
{
    private const UnixFileMode _ownerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode _ownerOnlyFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>Writes a file in place of what it holds, in one rename, after creating its folder.</summary>
    /// <param name="path">The file.</param>
    /// <param name="contents">What it holds.</param>
    internal static void Replace(string path, ReadOnlySpan<byte> contents)
    {
        CreateFolder(Path.GetDirectoryName(Path.GetFullPath(path))!);
        AtomicFile.Write(path, contents);
    }

    /// <summary>Stages a file's replacement beside it, which a commit renames over it.</summary>
    /// <param name="path">The file.</param>
    /// <param name="contents">What it will hold.</param>
    /// <returns>The staged file.</returns>
    internal static AtomicFile Stage(string path, ReadOnlySpan<byte> contents) => AtomicFile.Stage(path, contents);

    /// <summary>Creates a folder with the user's default permissions.</summary>
    /// <param name="directory">The folder.</param>
    internal static void CreateFolder(string directory) => Directory.CreateDirectory(directory);

    /// <summary>Creates a folder only its owner can open, on Linux and macOS; on Windows it inherits its parent's.</summary>
    /// <param name="directory">The folder.</param>
    internal static void CreateOwnerOnlyFolder(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            Directory.CreateDirectory(directory, _ownerOnlyFolder);
        }
    }

    /// <summary>Holds a lock file, created owner-only if it is new, that no other handle can open while this one is open.</summary>
    /// <param name="path">The lock file.</param>
    /// <returns>The held handle.</returns>
    internal static FileStream Hold(string path) => new(path, Options(FileMode.OpenOrCreate, FileAccess.Write, FileShare.None));

    /// <summary>Opens a file to write anew, created owner-only if it is new, that others may still read.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The writer.</returns>
    internal static FileStream Create(string path) => new(path, Options(FileMode.Create, FileAccess.Write, FileShare.Read));

    /// <summary>Opens a file to append to, created owner-only if it is new, that others may read and delete while it is open.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The writer.</returns>
    internal static FileStream Append(string path) => new(path, Options(FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete));

    /// <summary>Narrows a file to owner-only on Linux and macOS.</summary>
    /// <param name="path">The file.</param>
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    internal static void MakeOwnerOnly(string path) => File.SetUnixFileMode(path, _ownerOnlyFile);

    /// <summary>Moves a file to where none is yet, after creating that folder.</summary>
    /// <param name="source">The file.</param>
    /// <param name="destination">Where it goes.</param>
    internal static void MoveWithoutReplacing(string source, string destination)
    {
        CreateFolder(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        File.Move(source, destination, overwrite: false);
    }

    /// <summary>Deletes a file, if it is there.</summary>
    /// <param name="path">The file.</param>
    internal static void Delete(string path) => File.Delete(path);

    /// <summary>Deletes a folder and everything in it.</summary>
    /// <param name="directory">The folder.</param>
    internal static void DeleteFolder(string directory) => Directory.Delete(directory, recursive: true);

    private static FileStreamOptions Options(FileMode mode, FileAccess access, FileShare share)
    {
        var options = new FileStreamOptions { Mode = mode, Access = access, Share = share };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = _ownerOnlyFile;
        }

        return options;
    }
}
