using Keypaste.Core.Infrastructure;

namespace Keypaste.Core.Login;

/// <summary>The per-user entry that starts the desktop app, in the background, when the person logs in.</summary>
public interface ILoginItem
{
    /// <summary>Gets whether the entry is there and starts this copy of the app.</summary>
    bool IsEnabled { get; }

    /// <summary>Writes the entry.</summary>
    /// <returns><see langword="false"/> when it could not be written.</returns>
    bool Enable();

    /// <summary>Removes the entry.</summary>
    /// <returns><see langword="false"/> when it could not be removed.</returns>
    bool Disable();
}

/// <summary>Chooses the login mechanism for this platform and the program it starts.</summary>
public static class LoginItems
{
    /// <summary>The argument a start at login carries, which keeps the window closed.</summary>
    public const string BackgroundFlag = "--background";

    /// <summary>The login entry for the running app.</summary>
    /// <returns>The entry, or null where there is no per-user mechanism or no program path.</returns>
    public static ILoginItem? ForThisProcess()
    {
        var program = ProgramPath(
            Environment.ProcessPath,
            AppContext.BaseDirectory,
            Environment.GetEnvironmentVariable("APPIMAGE"),
            Environment.GetEnvironmentVariable("APPDIR"));

        if (program is null)
        {
            return null;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsWindows())
        {
            return new WindowsRunKey(new RegistryRunKey(), new RegistryStartupApproval(), program);
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacLaunchAgent(home, program);
        }

        if (OperatingSystem.IsLinux())
        {
            return new XdgAutostart(XdgAutostart.DirectoryFor(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"), home), program);
        }

        return null;
    }

    /// <summary>The file a login should start: the AppImage when the app runs from inside one, else this process.</summary>
    /// <param name="processPath">This process's executable.</param>
    /// <param name="appDirectory">The app's own directory.</param>
    /// <param name="appImage">The <c>APPIMAGE</c> environment variable.</param>
    /// <param name="appDir">The <c>APPDIR</c> environment variable.</param>
    /// <returns>The program, or null when there is none.</returns>
    /// <remarks>An AppImage is mounted at a new path on every launch, so its own process path does not outlive it.</remarks>
    public static string? ProgramPath(string? processPath, string appDirectory, string? appImage, string? appDir)
    {
        if (appImage is { Length: > 0 } && appDir is { Length: > 0 } && IsWithin(appDirectory, appDir))
        {
            return Path.GetFullPath(appImage);
        }

        return processPath is { Length: > 0 } ? processPath : null;
    }

    /// <summary>Whether <paramref name="args"/> ask for a start with no window.</summary>
    /// <param name="args">The process's arguments.</param>
    /// <returns><see langword="true"/> when <see cref="BackgroundFlag"/> is among them.</returns>
    public static bool StartsInBackground(IEnumerable<string> args) =>
        args.Any(arg => string.Equals(arg, BackgroundFlag, StringComparison.Ordinal));

    internal static bool WriteFile(string path, string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // launchd ignores an agent that others can write.
            AtomicFile.Write(
                path,
                Encoding.UTF8.GetBytes(text),
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static bool DeleteFile(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static string? ReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsWithin(string directory, string root)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;

        return full.StartsWith(prefix, StringComparison.Ordinal);
    }
}
