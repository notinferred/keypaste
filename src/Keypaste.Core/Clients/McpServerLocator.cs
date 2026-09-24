namespace Keypaste.Core.Clients;

/// <summary>Finds the <c>keypaste-mcp</c> a client should be told to start.</summary>
/// <remarks>
/// Beside the running program first: the two are released together, and a mismatched pair is a
/// class of bug nobody would enjoy diagnosing. PATH is the fallback.
/// </remarks>
public static class McpServerLocator
{
    /// <summary>The bridge's file name, without the platform's extension.</summary>
    public const string FileName = "keypaste-mcp";

    /// <summary>What the desktop AppImage's <c>AppRun</c> starts the bridge for.</summary>
    public const string AppImageArgument = "mcp";

    /// <summary>The bridge's file name on this platform.</summary>
    public static string ExecutableName => OperatingSystem.IsWindows() ? FileName + ".exe" : FileName;

    /// <summary>Finds the bridge beside <paramref name="besideDirectory"/>, then on PATH.</summary>
    /// <param name="besideDirectory">The running program's directory.</param>
    /// <param name="pathVariable">The PATH environment variable.</param>
    /// <returns>The bridge, or null when neither place has it.</returns>
    public static McpServerCommand? Find(string? besideDirectory, string? pathVariable)
    {
        if (besideDirectory is { Length: > 0 }
            && Path.Combine(besideDirectory, ExecutableName) is var beside
            && File.Exists(beside))
        {
            return new McpServerCommand(Path.GetFullPath(beside), []);
        }

        foreach (var directory in (pathVariable ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim(), ExecutableName);
            if (File.Exists(candidate))
            {
                return new McpServerCommand(Path.GetFullPath(candidate), []);
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the bridge for the desktop, which may be running from inside an AppImage.
    /// </summary>
    /// <remarks>
    /// An AppImage is mounted at a new temporary path on every launch, so the bridge beside the app
    /// is gone as soon as the app exits. When the app is running from inside its image, the image
    /// file itself is what a client is told to start, with <see cref="AppImageArgument"/> selecting
    /// the bridge; that path lasts as long as the person leaves the file where it is. The image's
    /// runtime sets <c>APPIMAGE</c> and <c>APPDIR</c>, and both must agree with where the app is
    /// actually running before either is believed.
    /// </remarks>
    /// <param name="appDirectory">The desktop's own directory.</param>
    /// <param name="appImage">The <c>APPIMAGE</c> environment variable.</param>
    /// <param name="appDir">The <c>APPDIR</c> environment variable.</param>
    /// <param name="pathVariable">The PATH environment variable.</param>
    public static McpServerCommand? FindForDesktop(string appDirectory, string? appImage, string? appDir, string? pathVariable)
    {
        ArgumentException.ThrowIfNullOrEmpty(appDirectory);

        if (appImage is { Length: > 0 }
            && appDir is { Length: > 0 }
            && File.Exists(appImage)
            && IsWithin(appDirectory, appDir)
            && File.Exists(Path.Combine(appDirectory, ExecutableName)))
        {
            return new McpServerCommand(Path.GetFullPath(appImage), [AppImageArgument]);
        }

        return Find(appDirectory, pathVariable);
    }

    /// <summary>Where <see cref="Find"/> looked, for a message that says so.</summary>
    public static string Places(string? besideDirectory) =>
        besideDirectory is { Length: > 0 } ? $"in {besideDirectory} and on PATH" : "on PATH";

    private static bool IsWithin(string directory, string root)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;

        return full.StartsWith(prefix, StringComparison.Ordinal);
    }
}
