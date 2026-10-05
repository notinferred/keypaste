namespace Keypaste.Core.Clients;

/// <summary>Finds the <c>keypaste</c> binary the desktop tells a client to start as <c>keypaste mcp</c>.</summary>
/// <remarks>
/// Beside the app first: the packages carry the two together, and a mismatched pair is a class of bug nobody
/// would enjoy diagnosing. PATH is the fallback. <c>keypaste setup</c> needs none of this: it registers itself.
/// </remarks>
public static class McpServerLocator
{
    /// <summary>The CLI binary's file name, without the platform's extension.</summary>
    public const string FileName = "keypaste";

    /// <summary>What selects the bridge: <c>keypaste mcp</c>, which the desktop AppImage's <c>AppRun</c> passes through.</summary>
    public const string BridgeArgument = "mcp";

    /// <summary>The CLI binary's file name on this platform.</summary>
    public static string ExecutableName => OperatingSystem.IsWindows() ? FileName + ".exe" : FileName;

    /// <summary>Finds <c>keypaste</c> beside <paramref name="besideDirectory"/>, then on PATH.</summary>
    /// <param name="besideDirectory">The running program's directory.</param>
    /// <param name="pathVariable">The PATH environment variable.</param>
    /// <returns>The bridge, or null when neither place has it.</returns>
    public static McpServerCommand? Find(string? besideDirectory, string? pathVariable)
    {
        if (besideDirectory is { Length: > 0 }
            && Path.Combine(besideDirectory, ExecutableName) is var beside
            && File.Exists(beside))
        {
            return new McpServerCommand(Path.GetFullPath(beside), [BridgeArgument]);
        }

        foreach (var directory in (pathVariable ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim(), ExecutableName);
            if (File.Exists(candidate))
            {
                return new McpServerCommand(Path.GetFullPath(candidate), [BridgeArgument]);
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
    /// file itself is what a client is told to start, with <see cref="BridgeArgument"/> selecting
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
            return new McpServerCommand(Path.GetFullPath(appImage), [BridgeArgument]);
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
