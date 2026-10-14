using Keypaste.Core.Approval;
using Keypaste.Core.Launch;
using Keypaste.Core.Projects;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Only a released set reaches a launcher, its values go into the child's environment over the
/// parent's and nowhere else, and the terminal the app opens takes the command as an argument
/// rather than as script text (E.1b, D-0340); on macOS Terminal is opened on the bundle's launcher
/// with nothing of the run in its arguments (E.1d, D-0451).
/// </summary>
public sealed class EnvLaunchTests : IDisposable
{
    private const string _secret = "sk_live_launch_value_7c1e";
    private const string _launcher = "/Applications/keypaste.app/Contents/Resources/keypaste-launch.command";
    private const string _bundleOnly = "This copy of keypaste opens Terminal only when it runs as keypaste.app. Copy the run command instead.";
    private const UnixFileMode _executable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private static readonly Dictionary<string, string> _noParent = new();

    /// <summary>The longest command a mapping holds, full of quotes and spaces.</summary>
    private static readonly string _longest = string.Concat(Enumerable.Repeat("say \"it's\" 'a \"b\"' ", 300))[..ProjectMappings.MaximumCommandLength];

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-env-launch-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_released_set_goes_into_the_environment_over_the_parent_and_nowhere_else()
    {
        var launcher = new RecordingLauncher();
        var resolved = EnvResolved.Released("dev", [new EnvVariable("API_KEY", _secret), new EnvVariable("SHARED", "from-vault")]);
        var parent = new Dictionary<string, string> { ["SHARED"] = "from-parent", ["UNRELATED"] = "kept" };

        EnvLaunch.Start(resolved, new LaunchTarget("tool", ["--flag"], _directory), parent, launcher);

        var start = Assert.Single(launcher.Started);
        Assert.Equal("tool", start.FileName);
        Assert.Equal(["--flag"], start.Arguments);
        Assert.Equal(_directory, start.WorkingDirectory);
        Assert.Equal(_secret, start.Environment["API_KEY"]);
        Assert.Equal("from-vault", start.Environment["SHARED"]);
        Assert.Equal("kept", start.Environment["UNRELATED"]);
        Assert.DoesNotContain(start.Arguments, argument => argument.Contains(_secret, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(EnvOutcome.Unusable)]
    [InlineData(EnvOutcome.Locked)]
    [InlineData(EnvOutcome.Declined)]
    [InlineData(EnvOutcome.ChangedWhileAsked)]
    public void A_set_that_was_not_released_never_reaches_the_launcher(EnvOutcome outcome)
    {
        var launcher = new RecordingLauncher();

        Assert.Throws<ArgumentException>(() => EnvLaunch.Start(
            EnvResolved.Refused("dev", outcome),
            new LaunchTarget("tool", []),
            new Dictionary<string, string>(),
            launcher));

        Assert.Empty(launcher.Started);
    }

    [Fact]
    public void Windows_runs_the_command_through_cmd_verbatim_and_opens_it_bare()
    {
        var terminal = new TerminalLaunch(TerminalPlatform.Windows, @"C:\Windows\system32\cmd.exe", _ => null);

        Assert.True(terminal.TryPlan(_directory, "npm run \"dev server\"", out var run, out _));
        Assert.Equal(@"C:\Windows\system32\cmd.exe", run!.FileName);
        Assert.Equal("/s /k \"npm run \"dev server\"\"", run.CommandLine);
        Assert.Empty(run.Arguments);
        Assert.Equal(_directory, run.WorkingDirectory);

        Assert.True(terminal.TryPlan(_directory, null, out var open, out _));
        Assert.Null(open!.CommandLine);
        Assert.Empty(open.Arguments);
    }

    [Theory]
    [InlineData("x-terminal-emulator", "-e")]
    [InlineData("xterm", "-e")]
    [InlineData("konsole", "-e")]
    [InlineData("gnome-terminal", "--")]
    public void Linux_passes_the_command_as_the_last_argument_of_a_fixed_script(string found, string exec)
    {
        var terminal = new TerminalLaunch(TerminalPlatform.Linux, "unused", name => name == found ? "/usr/bin/" + name : null);

        Assert.True(terminal.TryPlan(_directory, "echo $HOME; npm start", out var run, out _));

        Assert.Equal("/usr/bin/" + found, run!.FileName);
        Assert.Equal(_directory, run.WorkingDirectory);
        Assert.Null(run.CommandLine);
        Assert.Equal([exec, "/bin/sh", "-c", TerminalLaunch.RunScript, "keypaste", "echo $HOME; npm start"], run.Arguments.Skip(run.Arguments.Count - 6));
    }

    [Fact]
    public void Linux_takes_the_first_terminal_in_order_and_opens_it_without_a_command()
    {
        var terminal = new TerminalLaunch(TerminalPlatform.Linux, "unused", name => name is "xterm" or "konsole" ? "/usr/bin/" + name : null);

        Assert.True(terminal.TryPlan(_directory, null, out var open, out _));

        Assert.Equal("/usr/bin/konsole", open!.FileName);
        Assert.Equal(["--workdir", _directory], open.Arguments);
    }

    [Fact]
    public void Nothing_is_planned_without_a_terminal_a_directory_or_a_supported_platform()
    {
        var none = new TerminalLaunch(TerminalPlatform.Linux, "unused", _ => null);
        Assert.False(none.TryPlan(_directory, "make", out var target, out var refusal));
        Assert.Null(target);
        Assert.Contains("no terminal was found", refusal, StringComparison.Ordinal);

        var windows = new TerminalLaunch(TerminalPlatform.Windows, "cmd.exe", _ => null);
        Assert.False(windows.TryPlan(Path.Combine(_directory, "missing"), "make", out _, out refusal));
        Assert.Contains("does not exist", refusal, StringComparison.Ordinal);
        Assert.False(windows.TryPlan("relative", "make", out _, out _));
        Assert.False(windows.TryPlan(_directory, "  ", out _, out _));

        var other = new TerminalLaunch(TerminalPlatform.Other, "unused", _ => "/usr/bin/xterm");
        Assert.False(other.IsSupported);
        Assert.Equal("This app opens terminals on Windows, Linux and macOS only. Copy the run command instead.", other.UnsupportedReason);
        Assert.False(other.TryPlan(_directory, "make", out _, out refusal));
        Assert.Equal("the app opens terminals on Windows, Linux and macOS only; copy the run command instead", refusal);
        Assert.Null(windows.UnsupportedReason);
    }

    [Fact]
    public void On_macOS_Terminal_opens_the_launcher_and_the_run_carries_the_command_as_the_last_argument_of_a_fixed_script()
    {
        const string Command = "API=$(cat key) npm run \"dev server\"; echo `id` $HOME *";
        var terminal = new TerminalLaunch(_launcher);

        Assert.True(terminal.IsSupported);
        Assert.Null(terminal.UnsupportedReason);
        Assert.True(terminal.TryPlanThroughSession(_directory, Command, _noParent, out var run, out _));

        Assert.Equal("/usr/bin/open", run.Opener);
        Assert.Equal(["-b", "com.apple.Terminal", _launcher], run.OpenerArguments);
        Assert.Equal(_directory, run.Directory);
        Assert.Equal(Command, run.Command);
        Assert.Equal(["/bin/sh", "-c", TerminalLaunch.RunScript, "keypaste", Command], run.Arguments);

        Assert.True(terminal.TryPlanThroughSession(_directory, null, _noParent, out var open, out _));

        Assert.Equal(run.OpenerArguments, open.OpenerArguments);
        Assert.Null(open.Command);
        Assert.Equal(["/bin/sh", "-c", TerminalLaunch.OpenScript, "keypaste"], open.Arguments);
        Assert.Equal("Terminal", TerminalOpen.ShownName);
    }

    /// <summary>A Terminal that <c>open</c> starts keeps its environment for every window, so no keypaste setting the app was started with is in it (T-42).</summary>
    [Fact]
    public void On_macOS_open_is_given_the_apps_environment_less_every_KEYPASTE_variable()
    {
        var parent = new Dictionary<string, string>
        {
            ["PATH"] = "/usr/bin:/bin:/usr/sbin:/sbin",
            ["HOME"] = "/Users/person",
            ["SSH_AUTH_SOCK"] = "/private/tmp/com.apple.launchd.x/Listeners",
            ["TMPDIR"] = "/var/folders/xy/abc/T/",
            ["KEYPASTE_HOME"] = "/Users/person/.keypaste-work",
            ["KEYPASTE_APPROVER"] = "kp-work",
            ["KEYPASTE_TOKEN"] = _secret,
            ["KEYPASTE_DRIVER_PASSWORD"] = _secret,
            ["keypaste_vault"] = "/Users/person/work.kdbx",
            ["KEYPASTEX"] = "not a keypaste setting",
            ["MY_KEYPASTE_HOME"] = "not one either",
        };
        string[] kept = ["PATH", "HOME", "SSH_AUTH_SOCK", "TMPDIR", "KEYPASTEX", "MY_KEYPASTE_HOME"];
        var terminal = new TerminalLaunch(_launcher);

        Assert.True(terminal.TryPlanThroughSession(_directory, "make", parent, out var run, out _));
        Assert.True(terminal.TryPlanThroughSession(_directory, null, parent, out var open, out _));

        foreach (var plan in new TerminalOpen[] { run, open })
        {
            Assert.Equal(kept.Order(StringComparer.Ordinal), plan.OpenerEnvironment.Keys.Order(StringComparer.Ordinal));
            Assert.All(kept, name => Assert.Equal(parent[name], plan.OpenerEnvironment[name]));
            Assert.Equal(["-b", "com.apple.Terminal", _launcher], plan.OpenerArguments);
        }
    }

    [Fact]
    public void On_macOS_nothing_is_planned_that_the_set_could_be_handed_to_and_only_macOS_plans_through_the_session()
    {
        var mac = new TerminalLaunch(_launcher);

        Assert.False(mac.TryPlan(_directory, "make", out var target, out _));
        Assert.Null(target);
        Assert.False(mac.TryPlan(_directory, null, out _, out _));

        var linux = new TerminalLaunch(TerminalPlatform.Linux, "unused", _ => "/usr/bin/xterm");
        Assert.False(linux.TryPlanThroughSession(_directory, "make", _noParent, out var plan, out _));
        Assert.Null(plan);
        Assert.False(new TerminalLaunch(TerminalPlatform.Windows, "cmd.exe", _ => null).TryPlanThroughSession(_directory, "make", _noParent, out _, out _));

        Assert.Throws<ArgumentException>(() => new TerminalLaunch(TerminalPlatform.MacOS, "unused", _ => null));
    }

    /// <summary>A mapping holds a command of up to 4096 characters, and Terminal's run carries it whole, though that run's command line is longer than the owner's prompt shows (D-0452).</summary>
    [Fact]
    public void On_macOS_the_longest_command_a_mapping_holds_is_planned_whole_as_the_runs_last_argument()
    {
        var terminal = new TerminalLaunch(_launcher);

        Assert.True(terminal.TryPlanThroughSession(_directory, _longest, _noParent, out var plan, out var refusal), refusal);

        Assert.Equal(_longest, plan.Command);
        Assert.Equal(["/bin/sh", "-c", TerminalLaunch.RunScript, "keypaste", _longest], plan.Arguments);
        Assert.True(EnvReleasePrompt.CommandLine(plan.Arguments).Length > EnvReleasePrompt.MaximumCommandLength);
        Assert.Null(TerminalLaunch.ClaimProblem(Claim(plan.Arguments)));
    }

    /// <summary>The owner issues a pass only for Terminal's run or open script around one command a mapping could hold, in a request its prompt could have asked about (D-0452).</summary>
    [Fact]
    public void A_pass_is_claimed_only_for_Terminals_run_or_open_around_one_command_a_mapping_holds()
    {
        string[] open = ["/bin/sh", "-c", TerminalLaunch.OpenScript, "keypaste"];
        const string NotTerminals = "the command is not Terminal's run of one line of at most 4096 characters, or its open terminal";

        Assert.Null(TerminalLaunch.ClaimProblem(Claim(open)));
        Assert.Null(TerminalLaunch.ClaimProblem(Claim(Ran("make"))));
        Assert.Null(TerminalLaunch.ClaimProblem(Claim(Ran(_longest))));

        IReadOnlyList<string>[] refused =
        [
            Ran(_longest + "a"),
            Ran("make\nrm -rf ~"),
            Ran("make\u001b[2J"),
            Ran("  "),
            ["deploy", "--to", "staging"],
            ["/bin/sh", "-c", "eval \"$1\"", "keypaste", "make"],
            ["/bin/bash", "-c", TerminalLaunch.RunScript, "keypaste", "make"],
            ["/bin/sh", "-c", TerminalLaunch.RunScript, "sh", "make"],
            ["/bin/sh", "-c", TerminalLaunch.RunScript, "keypaste"],
            [.. Ran("make"), "more"],
            [.. open, "make"],
        ];

        Assert.All(refused, command => Assert.Equal(NotTerminals, TerminalLaunch.ClaimProblem(Claim(command))));

        Assert.Equal("the request names no command", TerminalLaunch.ClaimProblem(Claim([])));
        Assert.NotNull(TerminalLaunch.ClaimProblem(Claim(open) with { Project = "web/app" }));
        Assert.NotNull(TerminalLaunch.ClaimProblem(Claim(open) with { Profile = "Staging" }));
        Assert.NotNull(TerminalLaunch.ClaimProblem(Claim(open) with { Directory = string.Empty }));
        Assert.NotNull(TerminalLaunch.ClaimProblem(Claim(open) with { Directory = "/" + new string('d', EnvReleasePrompt.MaximumDirectoryLength) }));
    }

    [Fact]
    public void On_macOS_a_directory_that_does_not_exist_or_a_blank_command_is_refused_before_anyone_is_asked()
    {
        var terminal = new TerminalLaunch(_launcher);

        Assert.False(terminal.TryPlanThroughSession(Path.Combine(_directory, "missing"), "make", _noParent, out var plan, out var refusal));
        Assert.Null(plan);
        Assert.Contains("does not exist", refusal, StringComparison.Ordinal);
        Assert.False(terminal.TryPlanThroughSession("relative", "make", _noParent, out _, out _));
        Assert.False(terminal.TryPlanThroughSession(_directory, "  ", _noParent, out _, out _));
    }

    /// <summary>The request carries any path, so a directory holding a line break or another control character is planned on macOS as on Linux, and the owner issues its pass (D-0451).</summary>
    [Fact]
    public void On_macOS_a_directory_holding_a_line_break_or_another_control_character_is_planned_as_on_Linux()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows forbids a control character in a name.");

        var directory = Directory.CreateDirectory(Path.Combine(_directory, "web\nrm -rf ~\t\u001b[2J")).FullName;
        var terminal = new TerminalLaunch(_launcher);
        var linux = new TerminalLaunch(TerminalPlatform.Linux, "unused", _ => "/usr/bin/xterm");

        Assert.True(terminal.TryPlanThroughSession(directory, "make", _noParent, out var run, out var refusal), refusal);
        Assert.True(terminal.TryPlanThroughSession(directory, null, _noParent, out var open, out refusal), refusal);
        Assert.True(linux.TryPlan(directory, "make", out var target, out refusal), refusal);

        Assert.Equal(directory, run.Directory);
        Assert.Equal(directory, open.Directory);
        Assert.Equal(directory, target!.WorkingDirectory);
        Assert.Null(TerminalLaunch.ClaimProblem(Claim(run.Arguments) with { Directory = directory }));
        Assert.Null(TerminalLaunch.ClaimProblem(Claim(open.Arguments) with { Directory = directory }));
    }

    [Fact]
    public void On_macOS_nothing_opens_unless_the_app_runs_from_a_bundle_carrying_an_executable_keypaste_and_launcher()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("A bundle's executables are told apart by their Unix mode.");
            return;
        }

        var contents = Path.Combine(_directory, "Apps", "key paste", "keypaste.app", "Contents");
        var macOS = Path.Combine(contents, "MacOS");
        var program = Path.Combine(macOS, "keypaste");
        var launcher = Path.Combine(contents, "Resources", TerminalLaunch.LauncherName);
        var plain = Path.Combine(_directory, "bin");
        Directory.CreateDirectory(macOS);
        Directory.CreateDirectory(Path.GetDirectoryName(launcher)!);
        Directory.CreateDirectory(plain);
        File.WriteAllText(Path.Combine(plain, "keypaste"), string.Empty);
        File.SetUnixFileMode(Path.Combine(plain, "keypaste"), _executable);

        AssertUnsupported(TerminalLaunch.ForMacApp(plain));
        AssertUnsupported(TerminalLaunch.ForMacApp(macOS));

        File.WriteAllText(program, string.Empty);
        File.WriteAllText(launcher, string.Empty);
        File.SetUnixFileMode(program, _executable);
        AssertUnsupported(TerminalLaunch.ForMacApp(macOS));

        File.SetUnixFileMode(launcher, _executable);
        var terminal = TerminalLaunch.ForMacApp(macOS);
        Assert.True(terminal.IsSupported);
        Assert.True(terminal.TryPlanThroughSession(_directory, "make", _noParent, out var plan, out _));
        Assert.Equal(launcher, plan.OpenerArguments[^1]);

        File.SetUnixFileMode(program, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        AssertUnsupported(TerminalLaunch.ForMacApp(macOS));
    }

    /// <summary><c>open</c> is given nothing on its input, not even an encoding's preamble, whose write fails the start once <c>open</c> has closed it.</summary>
    [Fact]
    public void Open_writes_nothing_to_the_openers_input_not_even_a_preamble()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("The opener is stood in for by /bin/sh.");
            return;
        }

        var counter = new TerminalOpen("/bin/sh", ["-c", "wc -c"], new Dictionary<string, string> { ["PATH"] = "/usr/bin:/bin" }, _directory, null, []);

        var counted = TerminalLaunch.Open(counter, TimeSpan.FromSeconds(30));

        Assert.True(counted.Succeeded, counted.StandardError);
        Assert.Equal("0", counted.StandardOutput.Trim());
    }

    private static string[] Ran(string command) => ["/bin/sh", "-c", TerminalLaunch.RunScript, "keypaste", command];

    private LaunchClaim Claim(IReadOnlyList<string> command) =>
        new("web", "staging", _directory, command, new EnvPreview("web", ["TOKEN"]) { Profile = "staging" });

    private void AssertUnsupported(TerminalLaunch terminal)
    {
        Assert.False(terminal.IsSupported);
        Assert.Equal(_bundleOnly, terminal.UnsupportedReason);
        Assert.False(terminal.TryPlanThroughSession(_directory, "make", _noParent, out _, out var refusal));
        Assert.Equal("this copy of keypaste opens Terminal only when it runs as keypaste.app; copy the run command instead", refusal);
    }

    private sealed class RecordingLauncher : IProcessLauncher
    {
        internal List<ChildStart> Started { get; } = [];

        public ChildResult Run(ChildStart start)
        {
            Started.Add(start);
            return new ChildResult(ChildOutcome.Started, 0, string.Empty, 1);
        }
    }
}
