using Keypaste.Core.Launch;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Only a released set reaches a launcher, its values go into the child's environment over the
/// parent's and nowhere else, and the terminal the app opens takes the command as an argument
/// rather than as script text (E.1b, D-0340).
/// </summary>
public sealed class EnvLaunchTests : IDisposable
{
    private const string _secret = "sk_live_launch_value_7c1e";

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
        Assert.False(other.TryPlan(_directory, "make", out _, out _));
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
