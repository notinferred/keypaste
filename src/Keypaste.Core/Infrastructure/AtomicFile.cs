namespace Keypaste.Core.Infrastructure;

/// <summary>
/// Replaces a file in one rename, so whoever reads it finds the old contents or the new and never part of either.
/// </summary>
/// <remarks>
/// The contents go to a new file beside the target, are flushed to disk, and are moved over the target; a staged file
/// that is never committed is deleted. The target's directory must exist.
/// </remarks>
public sealed class AtomicFile : IDisposable
{
    /// <summary>Read and write for the owner and nothing for anyone else.</summary>
    public const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private readonly string _target;
    private string? _staged;

    private AtomicFile(string target, string staged)
    {
        _target = target;
        _staged = staged;
    }

    /// <summary>Writes <paramref name="contents"/> over <paramref name="path"/>.</summary>
    /// <param name="path">The file to replace or create.</param>
    /// <param name="contents">Its new contents.</param>
    /// <param name="mode">Its permissions on Unix; ignored on Windows, where it inherits its directory's.</param>
    /// <exception cref="IOException">The file could not be staged or moved into place; the target is unchanged.</exception>
    /// <exception cref="UnauthorizedAccessException">The same, for want of permission.</exception>
    public static void Write(string path, ReadOnlySpan<byte> contents, UnixFileMode mode = OwnerOnly)
    {
        using var staged = Stage(path, contents, mode);
        staged.Commit();
    }

    /// <summary>Writes <paramref name="contents"/> to a new file beside <paramref name="path"/>, which is unchanged until <see cref="Commit"/>.</summary>
    /// <param name="path">The file to replace or create.</param>
    /// <param name="contents">Its new contents.</param>
    /// <param name="mode">Its permissions on Unix; ignored on Windows, where it inherits its directory's.</param>
    /// <returns>The staged file, deleted on disposal unless committed.</returns>
    /// <exception cref="IOException">The file could not be staged.</exception>
    /// <exception cref="UnauthorizedAccessException">The same, for want of permission.</exception>
    public static AtomicFile Stage(string path, ReadOnlySpan<byte> contents, UnixFileMode mode = OwnerOnly)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var target = Path.GetFullPath(path);
        var staged = Path.Combine(
            Path.GetDirectoryName(target) ?? throw new ArgumentException("the path has no directory", nameof(path)),
            $".{Path.GetFileName(target)}.{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8))}.tmp");

        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = mode;
        }

        try
        {
            using var stream = new FileStream(staged, options);

            if (!OperatingSystem.IsWindows())
            {
                // The creation mode is narrowed by the umask; the file is given exactly the mode asked for.
                File.SetUnixFileMode(stream.SafeFileHandle, mode);
            }

            stream.Write(contents);
            stream.Flush(flushToDisk: true);
        }
        catch
        {
            Discard(staged);
            throw;
        }

        return new AtomicFile(target, staged);
    }

    /// <summary>Moves the staged file over the target.</summary>
    /// <exception cref="InvalidOperationException">It was already committed or disposed.</exception>
    /// <exception cref="IOException">The move failed; the target is unchanged.</exception>
    /// <exception cref="UnauthorizedAccessException">The same, for want of permission.</exception>
    public void Commit() => Move(overwrite: true);

    /// <summary>Moves the staged file to the target, which must not exist yet.</summary>
    /// <exception cref="InvalidOperationException">It was already committed or disposed.</exception>
    /// <exception cref="IOException">Something is already at the target, or the move failed; the target is unchanged.</exception>
    /// <exception cref="UnauthorizedAccessException">The same, for want of permission.</exception>
    public void CommitWithoutReplacing() => Move(overwrite: false);

    /// <summary>Deletes the staged file unless it was committed.</summary>
    public void Dispose()
    {
        if (_staged is { } staged)
        {
            _staged = null;
            Discard(staged);
        }
    }

    private void Move(bool overwrite)
    {
        var staged = _staged ?? throw new InvalidOperationException("the file was already committed or discarded");

        File.Move(staged, _target, overwrite);
        _staged = null;
    }

    private static void Discard(string staged)
    {
        try
        {
            File.Delete(staged);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A staged file is only ever a whole copy of what failed to land; leaving it costs nothing.
        }
    }
}
