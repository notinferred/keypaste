namespace Keypaste.Core.Clients;

/// <summary>The operating system whose tools, files and folders a machine has.</summary>
public enum McpClientPlatform
{
    /// <summary>Windows.</summary>
    Windows,

    /// <summary>macOS.</summary>
    MacOS,

    /// <summary>Linux.</summary>
    Linux,
}

/// <summary>What a machine says about where the AI tools keypaste connects live.</summary>
/// <param name="Platform">The operating system.</param>
/// <param name="Home">The home the tools read their files under: <c>USERPROFILE</c> on Windows, <c>HOME</c> elsewhere.</param>
/// <param name="AppData"><c>APPDATA</c>, on Windows.</param>
/// <param name="LocalAppData"><c>LOCALAPPDATA</c>, on Windows.</param>
/// <param name="PathVariable"><c>PATH</c>.</param>
/// <param name="PathExtensions"><c>PATHEXT</c>, on Windows.</param>
/// <param name="Variable">Answers the variables a tool documents as moving its files, such as <c>CODEX_HOME</c>.</param>
/// <param name="LooksInInstallFolders">
/// Whether a tool's documented install folders are searched after <c>PATH</c>, for the macOS app, which launchd starts
/// from Finder with a <c>PATH</c> that holds none of them.
/// </param>
/// <remarks>
/// A directory that is not absolute is taken as unset, so no tool's file is ever looked for against the directory
/// keypaste runs in.
/// </remarks>
public sealed record McpClientMachine(
    McpClientPlatform Platform,
    string? Home,
    string? AppData,
    string? LocalAppData,
    string? PathVariable,
    string? PathExtensions,
    Func<string, string?> Variable,
    bool LooksInInstallFolders)
{
    /// <summary>The platform this process runs on.</summary>
    public static McpClientPlatform CurrentPlatform =>
        OperatingSystem.IsWindows() ? McpClientPlatform.Windows
            : OperatingSystem.IsMacOS() ? McpClientPlatform.MacOS
            : McpClientPlatform.Linux;

    /// <summary>The desktop app's machine: its own environment, the profile's folders where a variable is unset, and on macOS the tools' install folders.</summary>
    public static McpClientMachine ForTheApp()
    {
        var platform = CurrentPlatform;
        var machine = From(Environment.GetEnvironmentVariable, platform);

        return platform == McpClientPlatform.Windows
            ? machine with
            {
                Home = machine.Home ?? Folder(Environment.SpecialFolder.UserProfile),
                AppData = machine.AppData ?? Folder(Environment.SpecialFolder.ApplicationData),
                LocalAppData = machine.LocalAppData ?? Folder(Environment.SpecialFolder.LocalApplicationData),
            }
            : machine with
            {
                Home = machine.Home ?? Folder(Environment.SpecialFolder.UserProfile),
                LooksInInstallFolders = platform == McpClientPlatform.MacOS,
            };
    }

    /// <summary>A machine read only from <paramref name="variable"/>, never from the real profile, and never searching install folders.</summary>
    /// <param name="variable">The environment, such as the CLI's probe.</param>
    /// <param name="platform">The platform whose variables to read.</param>
    /// <remarks>So a test that sets no home finds no tool's file, and cannot read or write a developer's configuration.</remarks>
    public static McpClientMachine From(Func<string, string?> variable, McpClientPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(variable);

        string? Text(string name) => variable(name) is { Length: > 0 } value ? value : null;
        string? Absolute(string name) => Text(name) is { } value && IsAbsolute(value, platform) ? value : null;

        return platform == McpClientPlatform.Windows
            ? new McpClientMachine(
                platform,
                Absolute("USERPROFILE"),
                Absolute("APPDATA"),
                Absolute("LOCALAPPDATA"),
                Text("PATH"),
                Text("PATHEXT"),
                variable,
                LooksInInstallFolders: false)
            : new McpClientMachine(
                platform,
                Absolute("HOME"),
                AppData: null,
                LocalAppData: null,
                Text("PATH"),
                PathExtensions: null,
                variable,
                LooksInInstallFolders: false);
    }

    // The platform's own rule rather than this process's, so a Windows machine can be described on Linux and the other way round.
    internal static bool IsAbsolute(string path, McpClientPlatform platform) =>
        platform == McpClientPlatform.Windows
            ? (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && IsSeparator(path[2]))
                || (path.Length >= 2 && IsSeparator(path[0]) && IsSeparator(path[1]))
            : path.StartsWith('/');

    private static bool IsSeparator(char c) => c is '\\' or '/';

    private static string? Folder(Environment.SpecialFolder folder) =>
        Environment.GetFolderPath(folder) is { Length: > 0 } path && Path.IsPathFullyQualified(path) ? path : null;
}
