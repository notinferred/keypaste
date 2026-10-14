using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using Keypaste.Core.Launch;
using Keypaste.Core.Projects;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The launcher Terminal runs claims one request, deletes it, and starts the bundle's own <c>keypaste run --session</c>
/// with exactly that request, evaluating none of it; a request it cannot use starts nothing (E.1d, D-0451).
/// </summary>
/// <remarks>
/// The launcher reads the real per-user folder, as Terminal's window does, so these share one collection that runs alone.
/// A stub at the bundle's <c>Contents/MacOS/keypaste</c> records what it was started with instead of running anything.
/// </remarks>
[Collection(nameof(TerminalRequestFolder))]
[SupportedOSPlatform("macos")]
public sealed class MacTerminalLauncherTests : IDisposable
{
    private const string _macOS = "Terminal's launcher runs on macOS only.";
    private const string _pass = "yMnKy8zNzs_Q0dLT1NXW19jZ2tvc3d7f4OHi4-Tl5uc";
    private const string _noRun = "keypaste: no run from the app is waiting for this window, so nothing was started. Run it again from the app.";
    private const string _unusable = "keypaste: the run from the app could not be started here, so nothing was started. Run it again from the app.";
    private const UnixFileMode _executable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly string _root = Directory.CreateTempSubdirectory("keypaste-launcher-").FullName;
    private readonly ManualClock _clock = new(DateTimeOffset.UtcNow);

    public void Dispose()
    {
        if (OperatingSystem.IsMacOS())
        {
            Empty();
        }

        Directory.Delete(_root, recursive: true);
    }

    private string Contents => Path.Combine(_root, "Apps", "key paste", "keypaste.app", "Contents");

    private string Launcher => Path.Combine(Contents, "Resources", TerminalLaunch.LauncherName);

    private string Records => Path.Combine(_root, "records");

    private string Marker => Path.Combine(_root, "evaluated");

    private string ProjectDirectory => Path.Combine(_root, "web app $(touch evaluated) `x`");

    private static string Folder => TerminalRequests.ForThisUser()!.Folder;

    [Theory]
    [InlineData("echo \"$(touch {marker})\" 'single quoted' \"$HOME\"")]
    [InlineData("`touch {marker}`; ls *")]
    [InlineData("touch {marker}; echo $HOME > /dev/null")]
    [InlineData("a=1 && touch {marker} || true # comment")]
    [InlineData("npm run dev -- --port=3000 \\ 'it''s' \"{marker}\"")]
    public void A_run_reaches_the_bundles_keypaste_exactly_as_requested_and_nothing_in_it_is_evaluated(string template)
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), _macOS);

        var command = template.Replace("{marker}", Marker, StringComparison.Ordinal);
        var request = Bundle(command);

        Assert.True(Requests().TryWrite(request, out _, out var refusal), refusal);
        var (exit, output) = Launch();

        Assert.True(exit == 0, output);
        AssertStartedWith(request);
        Assert.False(File.Exists(Marker));
        Assert.False(File.Exists(Path.Combine(ProjectDirectory, "evaluated")));
    }

    /// <summary>A project whose name starts with <c>-</c> reaches the bundle's <c>keypaste</c> as <c>--project=</c>'s value before the separator, never as script text (D-0453).</summary>
    [Theory]
    [InlineData("--help")]
    [InlineData("-p")]
    [InlineData("--")]
    [InlineData("-web")]
    [InlineData("--env-file=x")]
    [InlineData("-$(touch evaluated) `touch evaluated`")]
    public void A_project_whose_name_starts_with_a_dash_reaches_the_bundles_keypaste_by_the_project_option(string project)
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), _macOS);

        var request = Bundle("make") with { Project = project };

        Assert.True(Requests().TryWrite(request, out _, out var refusal), refusal);
        var (exit, output) = Launch();

        Assert.True(exit == 0, output);
        AssertStartedWith(request);
        Assert.False(File.Exists(Marker));
        Assert.False(File.Exists(Path.Combine(ProjectDirectory, "evaluated")));
    }

    /// <summary>
    /// A project directory, vault path, keypaste home or temporary folder holding a line break, another control character, a
    /// backslash or a character outside ASCII reaches the bundle's <c>keypaste</c> byte for byte, as a launch on Linux does,
    /// and nothing in it is evaluated (D-0451).
    /// </summary>
    [Theory]
    [InlineData("\n")]
    [InlineData(" line\nbreak $(touch evaluated)\n")]
    [InlineData("\t\r\u001b[2J\u007f\u0085")]
    [InlineData("\\c \\0101 \\n \\\\ %b %s")]
    [InlineData("café 日本")]
    public void Paths_holding_control_characters_backslashes_or_non_ASCII_reach_the_bundles_keypaste_byte_for_byte(string odd)
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), _macOS);

        var directory = Path.Combine(_root, "web" + odd);
        Directory.CreateDirectory(directory);
        var request = Bundle("printf '%b' '\\c\\0101\\n' && echo é") with
        {
            Home = Path.Combine(_root, "home" + odd),
            Vault = Path.Combine(_root, "vault" + odd + ".kdbx"),
            TemporaryDirectory = Path.Combine(_root, "tmp" + odd) + "/",
            Directory = directory,
        };

        Assert.True(Requests().TryWrite(request, out _, out var refusal), refusal);
        var (exit, output) = Launch();

        Assert.True(exit == 0, output);
        AssertStartedWith(request);
        Assert.False(File.Exists(Marker));
        Assert.False(File.Exists(Path.Combine(directory, "evaluated")));
    }

    /// <summary>The longest command a mapping holds reaches the bundle's <c>keypaste</c> whole as the run's last argument, and nothing in it is evaluated (D-0452).</summary>
    [Fact]
    public void The_longest_command_a_mapping_holds_reaches_the_bundles_keypaste_byte_for_byte()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), _macOS);

        var command = string.Concat(Enumerable.Repeat($"echo \"$(touch {Marker})\" 'it''s' `touch {Marker}` ", 200))[..ProjectMappings.MaximumCommandLength];
        var request = Bundle(command);

        Assert.True(Requests().TryWrite(request, out _, out var refusal), refusal);
        var (exit, output) = Launch();

        Assert.True(exit == 0, output);
        AssertStartedWith(request);
        Assert.Equal(command, Recorded("argv")[^1]);
        Assert.False(File.Exists(Marker));
    }

    [Fact]
    public void Open_terminal_reaches_the_bundles_keypaste_with_the_fixed_shell_script()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), _macOS);

        var request = Bundle(command: null);

        Assert.True(Requests().TryWrite(request, out _, out var refusal), refusal);
        var (exit, output) = Launch();

        Assert.True(exit == 0, output);
        AssertStartedWith(request);
    }

    [Fact]
    public void Each_window_claims_the_oldest_request_once_and_a_window_with_none_left_starts_nothing()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), _macOS);

        var first = Bundle("echo first");
        var second = first with { Command = "echo second" };
        var requests = Requests();
        Assert.True(requests.TryWrite(first, out _, out _));
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(requests.TryWrite(second, out var waiting, out _));

        Assert.Equal(0, Launch().Exit);
        AssertStartedWith(first, stillWaiting: waiting);
        Assert.Equal(0, Launch().Exit);
        AssertStartedWith(second);

        File.Delete(Path.Combine(Records, "argv"));
        var (exit, output) = Launch();

        Assert.Equal(1, exit);
        Assert.Equal(_noRun, output.TrimEnd('\n'));
        Assert.False(File.Exists(Path.Combine(Records, "argv")));
    }

    [Fact]
    public void Without_a_request_folder_the_window_says_no_run_is_waiting()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), _macOS);

        _ = Bundle("make");
        Empty();
        if (Directory.Exists(Folder))
        {
            Directory.Delete(Folder);
        }

        var (exit, output) = Launch();

        Assert.Equal(1, exit);
        Assert.Equal(_noRun, output.TrimEnd('\n'));
        Assert.False(File.Exists(Path.Combine(Records, "argv")));
    }

    /// <summary>
    /// In a window someone can type in, a refusal waits for Return after giving its reason, so a Terminal profile that closes
    /// the window as its process ends still leaves the reason on screen; with nothing to type, as above, it ends at once.
    /// </summary>
    [Fact]
    public async Task A_refusal_in_a_terminal_waits_for_Return_before_it_ends()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), _macOS);

        var cancel = TestContext.Current.CancellationToken;
        _ = Bundle("make");
        Empty();

        // script(1) gives the launcher a terminal of its own, whose keys are what this test writes to script's input.
        var start = Started("/usr/bin/script", "-q", "/dev/null", Launcher);
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;

        using var process = Process.Start(start) ?? throw new InvalidOperationException("script did not start");
        var screen = new StringBuilder();

        string Shown()
        {
            lock (screen)
            {
                return screen.ToString();
            }
        }

        var reading = Task.Run(
            () =>
            {
                var buffer = new char[256];
                int read;

                while ((read = process.StandardOutput.Read(buffer)) > 0)
                {
                    lock (screen)
                    {
                        screen.Append(buffer, 0, read);
                    }
                }
            },
            cancel);

        Assert.True(SpinWait.SpinUntil(() => Shown().Contains("Press Return to finish.", StringComparison.Ordinal), TimeSpan.FromSeconds(30)), Shown());
        Assert.Contains(_noRun, Shown(), StringComparison.Ordinal);
        Assert.False(process.WaitForExit(TimeSpan.FromSeconds(1)), "the launcher ended without waiting for Return");

        await process.StandardInput.WriteAsync("\n".AsMemory(), cancel);
        await process.StandardInput.FlushAsync(cancel);

        Assert.True(process.WaitForExit(TimeSpan.FromSeconds(30)), "the launcher did not end after Return");
        await reading.WaitAsync(TimeSpan.FromSeconds(30), cancel);
        Assert.False(File.Exists(Path.Combine(Records, "argv")));
    }

    [Theory]
    [InlineData(0, "keypaste-launch 1")]
    [InlineData(1, "")]
    [InlineData(1, "yMnKy8zNzs_Q0dLT1NXW19jZ2tvc3d7f4OHi4-Tl5u")]
    [InlineData(1, "+MnKy8zNzs_Q0dLT1NXW19jZ2tvc3d7f4OHi4-Tl5uc")]
    [InlineData(1, "yMnKy8zNzs_Q0dLT1NXW19jZ2tvc3d7f4OHi4-Tl5ué")]
    [InlineData(3, "/Users/me/vault\\n.kdbx")]
    [InlineData(9, "exec")]
    [InlineData(10, "make \\c")]
    [InlineData(10, "make \\")]
    [InlineData(5, null)]
    [InlineData(8, "{gone}")]
    public void A_request_the_launcher_cannot_use_is_deleted_and_starts_nothing(int line, string? value)
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), _macOS);

        var request = Bundle("make");
        List<string> lines =
        [
            "keypaste-launch 2", _pass, request.Home, request.Vault, request.Approver, request.TemporaryDirectory,
            request.Project, request.Profile, request.Directory, "run", "make",
        ];

        if (value is null)
        {
            lines.RemoveRange(line, lines.Count - line);
        }
        else
        {
            lines[line] = value.Replace("{gone}", Path.Combine(_root, "gone"), StringComparison.Ordinal);
        }

        Directory.CreateDirectory(Folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Empty();
        var path = Path.Combine(Folder, $"{DateTimeOffset.UtcNow.UtcTicks.ToString("D19", CultureInfo.InvariantCulture)}-0123456789abcdef.request");
        File.WriteAllText(path, string.Join('\n', lines) + "\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        var (exit, output) = Launch();

        Assert.NotEqual(0, exit);
        Assert.EndsWith(_unusable, output.TrimEnd('\n'), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(Records, "argv")));
        Assert.Empty(Directory.GetFileSystemEntries(Folder));
    }

    private TerminalRequests Requests()
    {
        Empty();
        return new TerminalRequests(Folder, _clock);
    }

    /// <summary>Lays out a bundle with the real launcher and a recording stub, and the request a Run in it would write.</summary>
    private TerminalRequest Bundle(string? command)
    {
        Assert.DoesNotContain("'", _root, StringComparison.Ordinal);
        Assert.DoesNotContain("'", Folder, StringComparison.Ordinal);

        Directory.CreateDirectory(Path.Combine(Contents, "MacOS"));
        Directory.CreateDirectory(Path.Combine(Contents, "Resources"));
        Directory.CreateDirectory(Records);
        Directory.CreateDirectory(ProjectDirectory);

        File.Copy(Path.Combine(RepoRoot(), "packaging", "macos", TerminalLaunch.LauncherName), Launcher, overwrite: true);
        File.SetUnixFileMode(Launcher, _executable);

        var stub = Path.Combine(Contents, "MacOS", "keypaste");
        File.WriteAllText(stub, $$"""
            #!/bin/sh
            for argument in "$@"; do printf '%s\0' "$argument"; done > '{{Records}}/argv.tmp'
            cwd=$(pwd -P; printf x)
            printf '%s' "${cwd%?x}" > '{{Records}}/pwd'
            printf '%s\0' "${KEYPASTE_HOME-unset}" "${TMPDIR-unset}" "${KEYPASTE_LAUNCH-unset}" > '{{Records}}/environment'
            ls -A '{{Folder}}' > '{{Records}}/left'
            mv '{{Records}}/argv.tmp' '{{Records}}/argv'
            """ + "\n");
        File.SetUnixFileMode(stub, _executable);

        return new TerminalRequest(
            _pass,
            Path.Combine(_root, "home $HOME"),
            Path.Combine(_root, "vault's \"x\".kdbx"),
            "keypaste-launcher-test",
            Path.Combine(_root, "tmp `x`") + "/",
            "web app",
            "staging",
            ProjectDirectory,
            command);
    }

    /// <summary>Runs the launcher as Terminal's login shell does, with nothing to type on its input.</summary>
    /// <returns>Its exit code and what it wrote to standard error, where every refusal goes.</returns>
    private (int Exit, string Output) Launch()
    {
        var start = Started(Launcher);
        start.RedirectStandardInput = true;
        start.RedirectStandardError = true;

        using var process = Process.Start(start) ?? throw new InvalidOperationException("the launcher did not start");
        process.StandardInput.Close();
        var error = process.StandardError.ReadToEnd();

        Assert.True(process.WaitForExit(TimeSpan.FromSeconds(30)), "the launcher did not exit");
        return (process.ExitCode, error);
    }

    /// <summary>How <paramref name="program"/> starts the launcher: from a decoy environment the request must override.</summary>
    private ProcessStartInfo Started(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            WorkingDirectory = _root,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment.Clear();
        start.Environment["HOME"] = _root;
        start.Environment["PATH"] = "/usr/bin:/bin";
        start.Environment["SHELL"] = "/bin/sh";
        start.Environment["KEYPASTE_HOME"] = "/decoy/home";
        start.Environment["TMPDIR"] = "/decoy/tmp/";
        start.Environment["KEYPASTE_LAUNCH"] = "decoy";
        return start;
    }

    /// <summary>Requires the stub to have been started with exactly the request, after the launcher deleted it.</summary>
    private void AssertStartedWith(TerminalRequest request, string? stillWaiting = null)
    {
        Assert.True(new TerminalLaunch(Launcher).TryPlanThroughSession(request.Directory, request.Command, new Dictionary<string, string>(), out var plan, out var refusal), refusal);

        Assert.Equal(
            ["run", "--session", "--vault", request.Vault, "--approver", request.Approver, "-p", request.Profile, $"--project={request.Project}", "--", .. plan.Arguments],
            Recorded("argv"));
        Assert.True(PathIdentity.SameFile(request.Directory, File.ReadAllText(Path.Combine(Records, "pwd"))));
        Assert.Equal([request.Home, request.TemporaryDirectory, request.Pass], Recorded("environment"));
        Assert.Equal(stillWaiting is null ? string.Empty : Path.GetFileName(stillWaiting) + "\n", File.ReadAllText(Path.Combine(Records, "left")));
    }

    private string[] Recorded(string name) =>
        File.ReadAllText(Path.Combine(Records, name)).Split('\0')[..^1];

    private static void Empty()
    {
        if (TerminalRequests.ForThisUser() is { } requests && Directory.Exists(requests.Folder))
        {
            foreach (var entry in Directory.GetFiles(requests.Folder))
            {
                File.Delete(entry);
            }
        }
    }

    private static string RepoRoot()
    {
        var directory = AppContext.BaseDirectory;

        while (!File.Exists(Path.Combine(directory, "keypaste.slnx")))
        {
            directory = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(directory))
                ?? throw new Xunit.Sdk.XunitException("keypaste.slnx not found above the test's base directory");
        }

        return directory;
    }
}

// The launcher reads the one per-user folder Terminal's window reads.
[CollectionDefinition(nameof(TerminalRequestFolder), DisableParallelization = true)]
public sealed class TerminalRequestFolder;
