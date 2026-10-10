using Keypaste.Core.Clients;
using Keypaste.Core.Infrastructure;
using Keypaste.Core.Processes;

namespace Keypaste.Core.Linking;

/// <summary>The program a Linux link starts.</summary>
/// <param name="Program">Its absolute path.</param>
/// <param name="ThroughAppImage">Whether it is an AppImage, which starts the <c>keypaste</c> it carries when given <c>cli</c>.</param>
public sealed record CliTarget(string Program, bool ThroughAppImage);

/// <summary>Chooses how this platform puts the app's <c>keypaste</c> where a terminal finds it.</summary>
/// <remarks>Windows has no link: the MSI puts its folder on the per-user <c>PATH</c>.</remarks>
public static class CliLinks
{
    /// <summary>What a replacement appends to the link path to keep what was there.</summary>
    internal const string BackupSuffix = ".keypaste-backup";

    /// <summary>The link for the running app.</summary>
    /// <returns>The link, or null on Windows and wherever no <c>keypaste</c> is carried beside the app.</returns>
    public static ICliLink? ForThisProcess()
    {
        var appDirectory = AppContext.BaseDirectory;

        if (OperatingSystem.IsLinux())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            return home is { Length: > 0 }
                && SemanticVersion.TryParse(CoreInfo.Version, out var version)
                && TargetFor(home, appDirectory, Environment.GetEnvironmentVariable("APPIMAGE"), Environment.GetEnvironmentVariable("APPDIR")) is { } target
                ? new LinuxCliLink(home, target, version, Environment.GetEnvironmentVariable("PATH"))
                : null;
        }

        if (OperatingSystem.IsMacOS())
        {
            return BundledProgram(appDirectory) is { } program && (File.GetUnixFileMode(program) & UnixFileMode.UserExecute) != 0
                ? new MacCliLink(program, new SystemProcessRunner())
                : null;
        }

        return null;
    }

    /// <summary>The program the Linux link starts: the AppImage when the app runs from inside one, else the <c>keypaste</c> beside the app.</summary>
    /// <param name="home">The person's home directory, which holds the link.</param>
    /// <param name="appDirectory">The app's own directory.</param>
    /// <param name="appImage">The <c>APPIMAGE</c> environment variable.</param>
    /// <param name="appDir">The <c>APPDIR</c> environment variable.</param>
    /// <returns>The program, or null when there is none or it is at the link's own path.</returns>
    /// <remarks>
    /// An AppImage is mounted at a new path on every launch, so the link names the image, and both variables must agree with
    /// where the app runs before either is believed, as <see cref="McpServerLocator.FindForDesktop"/> requires.
    /// </remarks>
    public static CliTarget? TargetFor(string home, string appDirectory, string? appImage, string? appDir)
    {
        ArgumentException.ThrowIfNullOrEmpty(home);
        ArgumentException.ThrowIfNullOrEmpty(appDirectory);

        var beside = Path.Combine(appDirectory, McpServerLocator.FileName);

        if (!File.Exists(beside))
        {
            return null;
        }

        var target = appImage is { Length: > 0 } && appDir is { Length: > 0 } && File.Exists(appImage) && IsWithin(appDirectory, appDir)
            ? new CliTarget(Path.GetFullPath(appImage), ThroughAppImage: true)
            : new CliTarget(Path.GetFullPath(beside), ThroughAppImage: false);

        return IsAt(target.Program, LinuxCliLink.LinkPathFor(home)) ? null : target;
    }

    /// <summary>The <c>keypaste</c> inside the bundle the app runs from.</summary>
    /// <param name="appDirectory">The app's own directory.</param>
    /// <returns>Its path, or null when the app is not in a bundle's <c>Contents/MacOS</c> or carries no <c>keypaste</c> there.</returns>
    internal static string? BundledProgram(string appDirectory)
    {
        var macOS = new DirectoryInfo(Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDirectory)));
        var program = Path.Combine(macOS.FullName, McpServerLocator.FileName);

        return macOS is { Name: "MacOS", Parent: { Name: "Contents", Parent.Name: var bundle } }
            && bundle.EndsWith(".app", StringComparison.Ordinal)
            && File.Exists(program)
            ? program
            : null;
    }

    /// <summary>Whether anything, a dangling symbolic link included, is at <paramref name="path"/>.</summary>
    internal static bool Occupied(string path) =>
        File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget is not null;

    // The link's own name is not followed: a symbolic link the person put there is replaced, never the file it names.
    private static bool IsAt(string program, string linkPath) =>
        string.Equals(
            PathIdentity.Canonical(program),
            Path.Combine(PathIdentity.Canonical(Path.GetDirectoryName(linkPath)!), Path.GetFileName(linkPath)),
            PathIdentity.Comparison);

    private static bool IsWithin(string directory, string root)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;

        return full.StartsWith(prefix, StringComparison.Ordinal);
    }
}
