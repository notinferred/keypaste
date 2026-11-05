namespace Keypaste.Core.Ownership;

/// <summary>
/// A path the app did not ask for: a start's argument, a second start's request or the system's open. It is untrusted
/// input that may only ever select what the unlock screen offers (THREATS.md T-29).
/// </summary>
public static class HandedFile
{
    /// <summary>The most bytes a second start's request may hold, its delimiter included.</summary>
    public const int MaximumRequestBytes = 8 * 1024;

    private static readonly UTF8Encoding _strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The file a start names: its first argument that is not a flag, against the directory it started in.</summary>
    /// <param name="args">The start's arguments.</param>
    /// <param name="workingDirectory">The directory it started in, which only it knows.</param>
    /// <returns>The full path, or null when it names none or one that is not well formed.</returns>
    public static string? FromArguments(IReadOnlyList<string> args, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentException.ThrowIfNullOrEmpty(workingDirectory);

        if (args.FirstOrDefault(arg => !arg.StartsWith('-')) is not { Length: > 0 } named || named.Contains('\0', StringComparison.Ordinal))
        {
            return null;
        }

        var full = Path.GetFullPath(named, workingDirectory);
        return IsWellFormed(full) ? full : null;
    }

    /// <summary>Whether <paramref name="path"/> can be a request: fully qualified, with no control character to hide what it names.</summary>
    public static bool IsWellFormed(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return Path.IsPathFullyQualified(path) && !path.Any(char.IsControl);
    }

    /// <summary>
    /// Whether reading <paramref name="path"/> stays on this computer. On Windows, touching a UNC path sends the person's
    /// credentials to the server it names, which a link to a file type the app handles could choose.
    /// </summary>
    public static bool IsLocal(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return !OperatingSystem.IsWindows() || IsDriveRooted(path);
    }

    /// <summary>
    /// Whether a Windows path starts at a drive letter, itself or after <c>\\?\</c>. Windows has more spellings of a
    /// server than can be listed, <c>//server</c>, <c>\\?\UNC</c>, <c>\\?\GLOBALROOT</c> and <c>\??\UNC</c> among them, so only this is let through.
    /// </summary>
    internal static bool IsDriveRooted(string path)
    {
        var spelled = path.Replace('/', '\\');

        if (spelled.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            spelled = spelled[4..];
        }

        return spelled is [var drive, ':', '\\', ..] && char.IsAsciiLetter(drive);
    }

    /// <summary>
    /// Whether opening <paramref name="path"/> reads a file with something in it. A FIFO, a socket or a device, itself or
    /// behind a link, reports no length, and opening one could wait forever on the thread that draws the window.
    /// </summary>
    public static bool IsWholeFile(string path) => LengthOf(path) > 0;

    /// <summary>What <paramref name="path"/> holds, behind any link, or null where it is not a file that can be looked at.</summary>
    public static long? LengthOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            var final = new FileInfo(File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path);
            return final.Exists ? final.Length : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>A second start's request for <paramref name="path"/>, or the bare request to show the window for none.</summary>
    internal static byte[] Request(string? path)
    {
        var frame = path is not null && IsWellFormed(path) ? Encoding.UTF8.GetBytes(path + '\n') : [];
        return frame.Length is > 0 and <= MaximumRequestBytes ? frame : [(byte)'\n'];
    }

    /// <summary>The path a second start's request names, or null for a bare request and for anything but a well-formed path.</summary>
    internal static string? FromRequest(ReadOnlySpan<byte> request)
    {
        if (request.IsEmpty)
        {
            return null;
        }

        try
        {
            var path = _strict.GetString(request);
            return IsWellFormed(path) ? path : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
