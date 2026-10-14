using System.Diagnostics.CodeAnalysis;
using Keypaste.Core.Approval;
using Keypaste.Core.Linking;
using Keypaste.Core.Processes;
using Keypaste.Core.Projects;

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

    /// <summary>macOS, where Terminal opens the bundle's launcher and the set reaches the run through <c>keypaste run --session</c>.</summary>
    MacOS = 3,
}

/// <summary>
/// How the app opens Terminal on macOS for one Run or Open terminal, and the request the window's
/// <c>keypaste run --session</c> then sends (D-0451).
/// </summary>
/// <param name="Opener">The program that opens Terminal.</param>
/// <param name="OpenerArguments">Its arguments, which name the bundle's launcher and no command, directory, project or value.</param>
/// <param name="OpenerEnvironment">Exactly the environment it runs with: the app's, less every <c>KEYPASTE_</c> variable.</param>
/// <param name="Directory">Where the run starts.</param>
/// <param name="Command">The person's command, or null to open their shell.</param>
/// <param name="Arguments">What the run starts, and so the command its request names: a fixed script with <paramref name="Command"/> as its last argument.</param>
public sealed record TerminalOpen(
    string Opener,
    IReadOnlyList<string> OpenerArguments,
    IReadOnlyDictionary<string, string> OpenerEnvironment,
    string Directory,
    string? Command,
    IReadOnlyList<string> Arguments)
{
    /// <summary>What the confirmation names as starting for Open terminal.</summary>
    public const string ShownName = "Terminal";
}

/// <summary>
/// Plans the platform's terminal in a project's directory, either at a prompt or running the
/// project's command and staying open after it (D-0340, D-0451).
/// </summary>
/// <remarks>
/// <para>
/// The command is the line a person would type, so it is handed to the platform's shell: on
/// Windows <c>cmd.exe /s /k</c>, which takes the rest of its command line verbatim, and on Linux
/// <c>sh -c 'eval "$1"; ...'</c> with the command as <c>$1</c>, so it is never spliced into script
/// text. Afterwards the person's own shell carries on, so what the command printed stays readable.
/// </para>
/// <para>
/// Terminal on macOS cannot be given an environment for one window, so there the app never hands
/// it the set: it opens Terminal on the bundle's fixed launcher, and the window's
/// <c>keypaste run --session</c> runs the same fixed script and asks the app for the set.
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

    /// <summary>The launcher's name in a bundle's <c>Contents/Resources</c>.</summary>
    public const string LauncherName = "keypaste-launch.command";

    /// <summary>What opens Terminal on macOS: <c>open</c> on a document, the double-click path, which needs no Automation permission.</summary>
    internal const string MacOpener = "/usr/bin/open";

    /// <summary>The script that starts the person's shell.</summary>
    internal const string OpenScript = "exec \"${SHELL:-/bin/sh}\"";

    /// <summary>The script that runs the command as <c>$1</c> and then the person's shell.</summary>
    internal const string RunScript = "eval \"$1\"; " + OpenScript;

    private static readonly Encoding _noPreamble = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly TerminalPlatform _platform;
    private readonly string _comSpec;
    private readonly Func<string, string?> _find;
    private readonly string? _launcher;

    /// <summary>Plans terminals for one platform.</summary>
    /// <param name="platform">The platform.</param>
    /// <param name="comSpec">Windows' command interpreter.</param>
    /// <param name="find">Finds an executable by name on <c>PATH</c>, or returns null.</param>
    /// <exception cref="ArgumentException"><paramref name="platform"/> is macOS, which plans through its launcher.</exception>
    public TerminalLaunch(TerminalPlatform platform, string comSpec, Func<string, string?> find)
    {
        ArgumentNullException.ThrowIfNull(comSpec);
        ArgumentNullException.ThrowIfNull(find);

        if (platform == TerminalPlatform.MacOS)
        {
            throw new ArgumentException("macOS opens Terminal through the bundle's launcher", nameof(platform));
        }

        _platform = platform;
        _comSpec = comSpec;
        _find = find;
    }

    /// <summary>Plans Terminal on macOS through the bundle's launcher.</summary>
    /// <param name="launcher">The bundle's executable <see cref="LauncherName"/>, or null when the app runs without one.</param>
    public TerminalLaunch(string? launcher)
    {
        _platform = TerminalPlatform.MacOS;
        _comSpec = string.Empty;
        _find = _ => null;
        _launcher = launcher;
    }

    /// <summary>The platform planned for: macOS plans with <see cref="TryPlanThroughSession"/>, the others with <see cref="TryPlan"/>.</summary>
    public TerminalPlatform Platform => _platform;

    /// <summary>Whether this platform can open a terminal at all.</summary>
    public bool IsSupported => UnsupportedReason is null;

    /// <summary>Why the app opens no terminal here, as the Env Sets screen says it, or null when it opens one.</summary>
    public string? UnsupportedReason => _platform switch
    {
        TerminalPlatform.Other => "This app opens terminals on Windows, Linux and macOS only. Copy the run command instead.",
        TerminalPlatform.MacOS when _launcher is null => "This copy of keypaste opens Terminal only when it runs as keypaste.app. Copy the run command instead.",
        _ => null,
    };

    /// <summary>Plans terminals for the machine this runs on.</summary>
    /// <returns>The planner.</returns>
    public static TerminalLaunch ForThisMachine()
    {
        if (OperatingSystem.IsMacOS())
        {
            return ForMacApp(AppContext.BaseDirectory);
        }

        var platform = OperatingSystem.IsWindows() ? TerminalPlatform.Windows
            : OperatingSystem.IsLinux() ? TerminalPlatform.Linux
            : TerminalPlatform.Other;
        var comSpec = Environment.GetEnvironmentVariable("ComSpec") is { Length: > 0 } set
            ? set
            : Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var path = Environment.GetEnvironmentVariable("PATH");

        return new TerminalLaunch(platform, comSpec, name => OnPath(name, path));
    }

    /// <summary>Plans Terminal for an app running from <paramref name="appDirectory"/> on macOS.</summary>
    /// <param name="appDirectory">The app's own directory: a bundle's <c>Contents/MacOS</c> when it runs as one.</param>
    /// <returns>The planner, which opens nothing unless the bundle carries an executable <c>keypaste</c> and launcher.</returns>
    public static TerminalLaunch ForMacApp(string appDirectory)
    {
        ArgumentNullException.ThrowIfNull(appDirectory);

        if (CliLinks.BundledProgram(appDirectory) is not { } program || !IsExecutable(program))
        {
            return new TerminalLaunch(launcher: null);
        }

        // Terminal types the launcher's path into the person's login shell as one line.
        var launcher = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(program))!, "Resources", LauncherName);

        return new TerminalLaunch(IsExecutable(launcher) && !launcher.Any(char.IsControl) ? launcher : null);
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

        if (!IsRunnable(directory, command, out refusal))
        {
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

            case TerminalPlatform.MacOS:
                refusal = "Terminal gets the variables from keypaste run, never from the app";
                return false;

            default:
                refusal = "the app opens terminals on Windows, Linux and macOS only; copy the run command instead";
                return false;
        }
    }

    /// <summary>Plans Terminal on macOS, where the window's <c>keypaste run --session</c> asks the app for the set.</summary>
    /// <param name="directory">The project's directory, which must exist.</param>
    /// <param name="command">The command to run in it, or null for a prompt.</param>
    /// <param name="parent">The app's environment, which <c>open</c> is given less every <c>KEYPASTE_</c> variable.</param>
    /// <param name="plan">What opens Terminal and what its run sends, when this returns true.</param>
    /// <param name="refusal">Why nothing can be started, when this returns false.</param>
    /// <returns>Whether there is something to open.</returns>
    /// <remarks>
    /// <para>
    /// The request carries any path, so a directory is refused here only as on Linux; the command is
    /// held to what a mapping holds where its claim is made (<see cref="ClaimProblem"/>, D-0451).
    /// </para>
    /// <para>
    /// A Terminal that <c>open</c> starts keeps <c>open</c>'s environment for every window until it
    /// quits, so no keypaste setting the app was started with may reach it; the run's own window gets
    /// its keypaste home from the launcher (T-42).
    /// </para>
    /// </remarks>
    public bool TryPlanThroughSession(
        string directory,
        string? command,
        IReadOnlyDictionary<string, string> parent,
        [NotNullWhen(true)] out TerminalOpen? plan,
        out string refusal)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(parent);

        plan = null;

        if (_platform != TerminalPlatform.MacOS)
        {
            refusal = "only macOS opens Terminal through keypaste run";
            return false;
        }

        if (_launcher is null)
        {
            refusal = "this copy of keypaste opens Terminal only when it runs as keypaste.app; copy the run command instead";
            return false;
        }

        if (!IsRunnable(directory, command, out refusal))
        {
            return false;
        }

        IReadOnlyList<string> arguments = command is null
            ? ["/bin/sh", "-c", OpenScript, "keypaste"]
            : ["/bin/sh", "-c", RunScript, "keypaste", command];

        var environment = new Dictionary<string, string>(EnvironmentMerge.Comparer);

        foreach (var (name, value) in parent)
        {
            if (!name.StartsWith("KEYPASTE_", StringComparison.OrdinalIgnoreCase))
            {
                environment[name] = value;
            }
        }

        plan = new TerminalOpen(MacOpener, ["-b", "com.apple.Terminal", _launcher], environment, directory, command, arguments);
        return true;
    }

    /// <summary>Why the owner issues no pass for a claim, or null when it issues one (D-0452).</summary>
    /// <param name="claim">The request the app's card named.</param>
    /// <returns>keypaste's own words, or null.</returns>
    /// <remarks>
    /// The request carrying the pass is held to this claim and never shown on the owner's prompt, so
    /// its command is bounded here instead of by the prompt's command line: it must be Terminal's open
    /// script, or its run script around one command a project's mapping could hold.
    /// </remarks>
    public static string? ClaimProblem(LaunchClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);

        if (EnvReleasePrompt.Problem(claim.Project, claim.Command, claim.Directory, boundCommandLine: false) is { } problem)
        {
            return problem;
        }

        if (!EnvProfileNames.IsValid(claim.Profile, out var invalid))
        {
            return invalid;
        }

        return claim.Command switch
        {
            ["/bin/sh", "-c", OpenScript, "keypaste"] => null,
            ["/bin/sh", "-c", RunScript, "keypaste", var command] when IsOneMappableLine(command) => null,
            _ => $"the command is not Terminal's run of one line of at most {ProjectMappings.MaximumCommandLength} characters, or its open terminal",
        };
    }

    /// <summary>Runs <c>open</c> as a plan says, with exactly the plan's environment.</summary>
    /// <param name="plan">The plan <see cref="TryPlanThroughSession"/> made.</param>
    /// <param name="timeout">How long <c>open</c> may take to return.</param>
    /// <returns>What <c>open</c> did.</returns>
    public static ProcessResult Open(TerminalOpen plan, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new SystemProcessRunner(plan.OpenerEnvironment).Run(plan.Opener, plan.OpenerArguments, stdin: null, _noPreamble, timeout);
    }

    private static bool IsRunnable(string directory, string? command, out string refusal)
    {
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

        return true;
    }

    /// <summary>Whether a command is one a project's mapping could hold: one line, with something to run, of at most <see cref="ProjectMappings.MaximumCommandLength"/> characters.</summary>
    private static bool IsOneMappableLine(string command) =>
        !string.IsNullOrWhiteSpace(command) && command.Length <= ProjectMappings.MaximumCommandLength && !command.Any(char.IsControl);

    private static bool IsExecutable(string path) =>
        !OperatingSystem.IsWindows()
        && File.Exists(path)
        && (File.GetUnixFileMode(path) & UnixFileMode.UserExecute) != 0;

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
