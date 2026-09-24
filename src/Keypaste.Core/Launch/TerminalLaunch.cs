namespace Keypaste.Core.Launch;

/// <summary>The desktops a terminal can be opened on.</summary>
public enum TerminalPlatform
{
    /// <summary>Windows, where the terminal is <c>%ComSpec%</c>.</summary>
    Windows = 0,

    /// <summary>Linux, where the terminal is the first emulator found on <c>PATH</c>.</summary>
    Linux = 1,

    /// <summary>Anything else, where the app opens no terminal.</summary>
    Other = 2,
}

/// <summary>
/// Plans the platform's terminal in a project's directory, either at a prompt or running the
/// project's command and staying open after it (D-0340).
/// </summary>
/// <remarks>
/// <para>
/// The command is the line a person would type, so it is handed to the platform's shell: on
/// Windows <c>cmd.exe /s /k</c>, which takes the rest of its command line verbatim, and on Linux
/// <c>sh -c 'eval "$1"; ...'</c> with the command as <c>$1</c>, so it is never spliced into script
/// text. Afterwards the person's own shell carries on, so what the command printed stays readable.
/// </para>
/// <para>
/// A terminal started this way hands the set to everything run in it (PRODUCT §2).
/// </para>
/// </remarks>
public sealed class TerminalLaunch
{
    /// <summary>The emulators tried on Linux, in order.</summary>
    /// <remarks>
    /// <c>x-terminal-emulator</c> is the one the distribution chose, and its contract is
    /// <c>-e command args</c>; the rest cover desktops without Debian's alternatives.
    /// </remarks>
    public static readonly IReadOnlyList<string> LinuxTerminals = ["x-terminal-emulator", "gnome-terminal", "konsole", "xterm"];

    /// <summary>The script that runs the command as <c>$1</c> and then the person's shell.</summary>
    internal const string RunScript = "eval \"$1\"; exec \"${SHELL:-/bin/sh}\"";

    private readonly TerminalPlatform _platform;
    private readonly string _comSpec;
    private readonly Func<string, string?> _find;

    /// <summary>Plans terminals for one platform.</summary>
    /// <param name="platform">The platform.</param>
    /// <param name="comSpec">Windows' command interpreter.</param>
    /// <param name="find">Finds an executable by name on <c>PATH</c>, or returns null.</param>
    public TerminalLaunch(TerminalPlatform platform, string comSpec, Func<string, string?> find)
    {
        ArgumentNullException.ThrowIfNull(comSpec);
        ArgumentNullException.ThrowIfNull(find);

        _platform = platform;
        _comSpec = comSpec;
        _find = find;
    }

    /// <summary>Whether this platform can open a terminal at all.</summary>
    public bool IsSupported => _platform != TerminalPlatform.Other;

    /// <summary>Plans terminals for the machine this runs on.</summary>
    /// <returns>The planner.</returns>
    public static TerminalLaunch ForThisMachine()
    {
        var platform = OperatingSystem.IsWindows() ? TerminalPlatform.Windows
            : OperatingSystem.IsLinux() ? TerminalPlatform.Linux
            : TerminalPlatform.Other;
        var comSpec = Environment.GetEnvironmentVariable("ComSpec") is { Length: > 0 } set
            ? set
            : Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var path = Environment.GetEnvironmentVariable("PATH");

        return new TerminalLaunch(platform, comSpec, name => OnPath(name, path));
    }

    /// <summary>Finds an executable by name in a <c>PATH</c> value.</summary>
    /// <param name="name">The executable's name.</param>
    /// <param name="pathVariable">The <c>PATH</c> to search.</param>
    /// <returns>Its full path, or null.</returns>
    public static string? OnPath(string name, string? pathVariable)
    {
        foreach (var directory in (pathVariable ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim(), name);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    /// <summary>Plans the terminal.</summary>
    /// <param name="directory">The project's directory, which must exist.</param>
    /// <param name="command">The command to run in it, or null for a prompt.</param>
    /// <param name="target">What to start, when this returns true.</param>
    /// <param name="refusal">Why nothing can be started, when this returns false.</param>
    /// <returns>Whether there is something to start.</returns>
    public bool TryPlan(string directory, string? command, out LaunchTarget? target, out string refusal)
    {
        ArgumentNullException.ThrowIfNull(directory);

        target = null;
        refusal = string.Empty;

        if (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory))
        {
            refusal = $"the directory '{directory}' does not exist";
            return false;
        }

        if (command is not null && string.IsNullOrWhiteSpace(command))
        {
            refusal = "the project has no command to run";
            return false;
        }

        switch (_platform)
        {
            case TerminalPlatform.Windows:
                target = new LaunchTarget(_comSpec, [], directory, command is null ? null : $"/s /k \"{command}\"");
                return true;

            case TerminalPlatform.Linux:
                foreach (var name in LinuxTerminals)
                {
                    if (_find(name) is { } found)
                    {
                        target = new LaunchTarget(found, Arguments(name, directory, command), directory);
                        return true;
                    }
                }

                refusal = $"no terminal was found on PATH; install one of {string.Join(", ", LinuxTerminals)}";
                return false;

            default:
                refusal = "the app opens terminals on Windows and Linux only; copy the run command instead";
                return false;
        }
    }

    private static List<string> Arguments(string terminal, string directory, string? command)
    {
        List<string> arguments = terminal switch
        {
            // A gnome-terminal server started earlier would not share this process's directory.
            "gnome-terminal" => [$"--working-directory={directory}"],
            "konsole" => ["--workdir", directory],
            _ => [],
        };

        if (command is not null)
        {
            arguments.Add(terminal == "gnome-terminal" ? "--" : "-e");
            arguments.AddRange(["/bin/sh", "-c", RunScript, "keypaste", command]);
        }

        return arguments;
    }
}
