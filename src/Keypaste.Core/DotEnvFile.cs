namespace Keypaste.Core;

/// <summary>Why a <c>.env</c> or reference file was not read.</summary>
public enum DotEnvFileFailure
{
    /// <summary>It was read.</summary>
    None = 0,

    /// <summary>No file is at the path, or a directory is.</summary>
    Missing = 1,

    /// <summary>It is larger than <see cref="DotEnv.MaximumBytes"/>.</summary>
    TooLarge = 2,

    /// <summary>The system would not read it.</summary>
    Unreadable = 3,
}

/// <summary>Reads and writes the bytes of a <c>.env</c> file or a reference file, the one way every front end does.</summary>
public static class DotEnvFile
{
    /// <summary>Reads a file of at most <see cref="DotEnv.MaximumBytes"/>, refusing a larger one before reading it.</summary>
    /// <param name="path">The file.</param>
    /// <param name="bytes">Its contents when read, otherwise empty.</param>
    /// <param name="failure">Why it was not read, or <see cref="DotEnvFileFailure.None"/>.</param>
    /// <param name="error">What the system said when it would not read the file, the size rule when the file is too large, otherwise empty.</param>
    /// <returns><see langword="true"/> if the file was read.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <remarks>
    /// The size is asked of the open file before a byte is read, and a file that grows while it is read,
    /// or reports no size, is refused once it passes the limit, so a wrong path costs at most the limit.
    /// </remarks>
    public static bool TryRead(string path, out byte[] bytes, out DotEnvFileFailure failure, out string error)
    {
        ArgumentNullException.ThrowIfNull(path);

        bytes = [];
        error = string.Empty;

        if (Directory.Exists(path))
        {
            failure = DotEnvFileFailure.Missing;
            return false;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

            if ((stream.CanSeek && stream.Length > DotEnv.MaximumBytes) || !TryReadBounded(stream, out bytes))
            {
                failure = DotEnvFileFailure.TooLarge;
                error = DotEnv.TooLargeError;
                return false;
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            failure = DotEnvFileFailure.Missing;
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failure = DotEnvFileFailure.Unreadable;
            error = ex.Message;
            return false;
        }

        failure = DotEnvFileFailure.None;
        return true;
    }

    /// <summary>Writes a new file only its owner can read, never following or truncating what is already there.</summary>
    /// <param name="path">The file.</param>
    /// <param name="bytes">Its contents.</param>
    /// <param name="replace">Whether a file already at the path is deleted first; otherwise one there fails the write.</param>
    /// <param name="error">What the system said when the write failed, otherwise empty.</param>
    /// <returns><see langword="true"/> if the file was written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <remarks>
    /// <see cref="FileMode.CreateNew"/> rather than <see cref="FileMode.Create"/> even when replacing, with an
    /// explicit delete first: truncating in place would keep the old file's permissions, so the mode below
    /// would apply on a fresh write and quietly not apply on a repeat. <see cref="FileStreamOptions.UnixCreateMode"/>
    /// throws on Windows, which has no equivalent — SECURITY.md states that gap rather than implying a mode
    /// that was never set.
    /// </remarks>
    public static bool TryWrite(string path, ReadOnlySpan<byte> bytes, bool replace, out string error)
    {
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            if (replace && File.Exists(path))
            {
                File.Delete(path);
            }

            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
            };

            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using var stream = new FileStream(path, options);
            stream.Write(bytes);

            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryReadBounded(Stream stream, out byte[] bytes)
    {
        bytes = [];
        var chunk = new byte[81920];
        using var read = new MemoryStream();

        try
        {
            int count;
            while ((count = stream.Read(chunk)) > 0)
            {
                if (read.Length + count > DotEnv.MaximumBytes)
                {
                    return false;
                }

                read.Write(chunk.AsSpan(0, count));
            }

            bytes = read.ToArray();
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(chunk);
            CryptographicOperations.ZeroMemory(read.GetBuffer());
        }
    }
}
