using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Keypaste.Core.Launch;
using Keypaste.Core.Processes;
using Xunit;

namespace Keypaste.App.Tests.Session;

/// <summary>One request the app left for Terminal's launcher, as it was when Terminal was asked to open.</summary>
/// <param name="Name">Its file name.</param>
/// <param name="Mode">Its permissions.</param>
/// <param name="Text">What it holds.</param>
internal sealed record WaitingRequest(string Name, UnixFileMode Mode, string Text);

/// <summary>One call to <c>open</c>.</summary>
/// <param name="FileName">The program the app ran.</param>
/// <param name="Arguments">Its arguments.</param>
/// <param name="Environment">The environment it ran with, which a Terminal it started would keep for every window.</param>
/// <param name="Waiting">The requests waiting in the launcher's folder at that moment.</param>
internal sealed record Opening(
    string FileName,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyList<WaitingRequest> Waiting);

/// <summary>
/// Terminal as the app meets it on macOS, on a runner whose Terminal a test cannot watch: an assembled
/// <c>keypaste.app</c> carrying the real launcher and a <c>keypaste</c> that starts the built CLI, and an
/// <c>open</c> that runs that launcher as a Terminal window does (E.1d).
/// </summary>
/// <remarks>
/// A window is a login <c>sh</c> given the line Terminal types, the launcher's path and then <c>exit</c>,
/// with none of the test's environment, which is how a Terminal already running opens one, whatever
/// <c>open</c> was given. Its input is closed, so the shell a run ends in exits, and what it prints
/// goes to a file.
/// </remarks>
internal sealed class TerminalStandIn : IDisposable
{
    private const UnixFileMode _executable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly string _root;
    private readonly string _home;
    private readonly Lock _gate = new();
    private readonly List<Opening> _openings = [];
    private readonly List<TerminalWindow> _windows = [];

    /// <summary>Assembles the bundle under <paramref name="root"/>.</summary>
    /// <param name="root">A folder the test owns, holding no single quote.</param>
    [SupportedOSPlatform("macos")]
    internal TerminalStandIn(string root)
    {
        Assert.DoesNotContain("'", root, StringComparison.Ordinal);

        _root = root;
        _home = Directory.CreateDirectory(Path.Combine(root, "home")).FullName;
        Bundle = Path.Combine(root, "keypaste.app");

        var program = Path.Combine(Directory.CreateDirectory(Path.Combine(Bundle, "Contents", "MacOS")).FullName, "keypaste");
        var cli = Built("Keypaste.Cli", "keypaste");
        var runtime = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        Assert.True(File.Exists(cli), $"build Keypaste.Cli first: {cli}");
        Assert.DoesNotContain("'", cli + runtime, StringComparison.Ordinal);

        // The build is framework-dependent, so it runs where it was built, on the runtime this test runs on.
        File.WriteAllText(program, $"#!/bin/sh\nDOTNET_ROOT='{runtime}'\nexport DOTNET_ROOT\nexec '{cli}' \"$@\"\n");
        File.SetUnixFileMode(program, _executable);

        Directory.CreateDirectory(Path.GetDirectoryName(Launcher)!);
        File.Copy(Path.Combine(RepoRoot(), "packaging", "macos", TerminalLaunch.LauncherName), Launcher);
        File.SetUnixFileMode(Launcher, _executable);
    }

    /// <summary>The bundle, ending in <c>keypaste.app</c>.</summary>
    internal string Bundle { get; }

    /// <summary>The bundle's launcher, which <c>open</c> is asked to open.</summary>
    internal string Launcher => Path.Combine(Bundle, "Contents", "Resources", TerminalLaunch.LauncherName);

    /// <summary>The person's shell, which a run ends in and Open terminal starts.</summary>
    internal string Shell { get; set; } = "/bin/sh";

    /// <summary>Runs after <c>open</c> is asked and before the window starts, as something the person does meanwhile.</summary>
    internal Action? BeforeWindow { get; set; }

    /// <summary>What <c>open</c> says when it fails, or null to open the window.</summary>
    internal string? Refusal { get; set; }

    /// <summary>Every call to <c>open</c>.</summary>
    internal IReadOnlyList<Opening> Openings
    {
        get
        {
            lock (_gate)
            {
                return [.. _openings];
            }
        }
    }

    /// <summary>Every window opened.</summary>
    internal IReadOnlyList<TerminalWindow> Windows
    {
        get
        {
            lock (_gate)
            {
                return [.. _windows];
            }
        }
    }

    /// <summary>The requests waiting for a launcher in the person's own folder, the one the app writes and a window reads.</summary>
    [SupportedOSPlatform("macos")]
    internal static IReadOnlyList<WaitingRequest> Waiting()
    {
        var folder = TerminalRequests.ForThisUser()!.Folder;
        List<WaitingRequest> waiting = [];

        string[] paths = Directory.Exists(folder) ? Directory.GetFiles(folder) : [];

        foreach (var path in paths.Order(StringComparer.Ordinal))
        {
            try
            {
                waiting.Add(new WaitingRequest(Path.GetFileName(path), File.GetUnixFileMode(path), File.ReadAllText(path)));
            }
            catch (FileNotFoundException)
            {
                // A window's launcher claimed it meanwhile.
            }
        }

        return waiting;
    }

    /// <summary>Deletes whatever an earlier test left in the person's folder.</summary>
    [SupportedOSPlatform("macos")]
    internal static void EmptyFolder()
    {
        var folder = TerminalRequests.ForThisUser()!.Folder;

        if (Directory.Exists(folder))
        {
            foreach (var path in Directory.GetFiles(folder))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>Puts a request back where the app wrote it.</summary>
    [SupportedOSPlatform("macos")]
    internal static void PutBack(WaitingRequest request) =>
        File.WriteAllText(Path.Combine(TerminalRequests.ForThisUser()!.Folder, request.Name), request.Text);

    /// <summary>Runs <c>open</c> as the app does, opening a window on the plan's launcher.</summary>
    internal ProcessResult Open(TerminalOpen plan, TimeSpan _)
    {
        lock (_gate)
        {
            _openings.Add(new Opening(
                plan.Opener,
                [.. plan.OpenerArguments],
                new Dictionary<string, string>(plan.OpenerEnvironment),
                OperatingSystem.IsMacOS() ? Waiting() : []));
        }

        if (Refusal is { } refusal)
        {
            return new ProcessResult(ToolFound: true, ExitCode: 1, string.Empty, refusal);
        }

        BeforeWindow?.Invoke();
        Open(plan.OpenerArguments[^1]);
        return new ProcessResult(ToolFound: true, ExitCode: 0, string.Empty, string.Empty);
    }

    /// <summary>Opens a window on <paramref name="document"/>, as Terminal does for a double-clicked <c>.command</c>.</summary>
    internal TerminalWindow Open(string document)
    {
        lock (_gate)
        {
            var output = Path.Combine(_root, $"window-{_windows.Count}.log");
            var start = new ProcessStartInfo("/bin/sh")
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                WorkingDirectory = _home,
            };

            foreach (var argument in new[] { "-l", "-c", "\"$0\" >\"$1\" 2>&1; exit", document, output })
            {
                start.ArgumentList.Add(argument);
            }

            start.Environment.Clear();
            start.Environment["HOME"] = _home;
            start.Environment["USER"] = Environment.UserName;
            start.Environment["LOGNAME"] = Environment.UserName;
            start.Environment["SHELL"] = Shell;
            start.Environment["PATH"] = "/usr/bin:/bin:/usr/sbin:/sbin";

            var window = new TerminalWindow(start, output);
            _windows.Add(window);
            return window;
        }
    }

    /// <summary>Ends every window and what it started.</summary>
    public void Dispose()
    {
        foreach (var window in Windows)
        {
            window.Dispose();
        }
    }

    /// <summary>A program the build put under <c>artifacts/bin</c>.</summary>
    private static string Built(string project, string file)
    {
        var configuration = AppContext.BaseDirectory.Contains("debug", StringComparison.OrdinalIgnoreCase) ? "debug" : "release";
        return Path.Combine(RepoRoot(), "artifacts", "bin", project, configuration, file);
    }

    private static string RepoRoot()
    {
        var directory = AppContext.BaseDirectory;

        while (!File.Exists(Path.Combine(directory, "keypaste.app.slnx")))
        {
            directory = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(directory))
                ?? throw new InvalidOperationException("Could not locate keypaste.app.slnx above " + AppContext.BaseDirectory);
        }

        return directory;
    }
}

/// <summary>One window the stand-in opened: its shell, with nothing on its input.</summary>
internal sealed class TerminalWindow : IDisposable
{
    private readonly Process _shell;
    private readonly string _output;

    internal TerminalWindow(ProcessStartInfo start, string output)
    {
        _shell = Process.Start(start) ?? throw new InvalidOperationException("the window's shell did not start");
        _shell.StandardInput.Close();
        _output = output;
    }

    /// <summary>Waits for the window's shell to end.</summary>
    /// <returns>Its exit code and everything the window printed.</returns>
    internal (int Exit, string Output) Ended()
    {
        Assert.True(_shell.WaitForExit(TimeSpan.FromSeconds(60)), "the window's shell did not exit");
        return (_shell.ExitCode, File.Exists(_output) ? File.ReadAllText(_output) : string.Empty);
    }

    public void Dispose()
    {
        try
        {
            _shell.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException)
        {
            // It, or something it started, was already exiting.
        }

        _shell.WaitForExit(TimeSpan.FromSeconds(30));
        _shell.Dispose();
    }
}

// The app writes, and the launcher reads, the one per-user folder Terminal's windows read.
[CollectionDefinition(nameof(TerminalRequestFolder), DisableParallelization = true)]
public sealed class TerminalRequestFolder;
