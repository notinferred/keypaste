using System.Diagnostics;
using System.Globalization;
using System.Text;
using Keypaste.Core.Launch;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// An agent's command runs with exactly the environment it is given, reads nothing, has its output
/// captured and bounded, and never outlives the run (D-0358, THREATS.md T-35).
/// </summary>
public sealed class CapturedLaunchTests : IDisposable
{
    private static readonly TimeSpan _generous = TimeSpan.FromSeconds(60);

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-capture-").FullName;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private ChildStart Reporter(IReadOnlyDictionary<string, string>? environment, params string[] steps) =>
        new(Tests.Reporter.Path, steps, environment ?? Environment(("A", "1")), _directory);

    /// <summary>What a .NET child needs to start at all, plus the given variables.</summary>
    private static Dictionary<string, string> Environment(params (string Name, string Value)[] variables)
    {
        var environment = new Dictionary<string, string>(EnvironmentMerge.Comparer);

        foreach (var name in new[] { "SystemRoot", "PATH", "HOME", "DOTNET_ROOT" })
        {
            if (System.Environment.GetEnvironmentVariable(name) is { } value)
            {
                environment[name] = value;
            }
        }

        foreach (var (name, value) in variables)
        {
            environment[name] = value;
        }

        return environment;
    }

    private static string Text(CapturedOutput output) => Encoding.UTF8.GetString(output.Bytes).ReplaceLineEndings("\n");

    [Fact]
    public async Task StdoutAndStderr_AreSeparate()
    {
        var result = await CapturedLaunch.RunAsync(Reporter(null, "--print", "to-out", "--stderr", "to-err"), CaptureLimits.Default, _generous, Token);

        Assert.Equal(ChildOutcome.Exited, result.Outcome);
        Assert.Equal("to-out\n", Text(result.Stdout));
        Assert.Equal("to-err\n", Text(result.Stderr));
    }

    [Fact]
    public async Task TheExitCode_IsReported()
    {
        var result = await CapturedLaunch.RunAsync(Reporter(null, "--exit", "7"), CaptureLimits.Default, _generous, Token);

        Assert.Equal(7, result.ExitCode);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task Stdin_IsClosed()
    {
        var result = await CapturedLaunch.RunAsync(Reporter(null, "--read-stdin"), CaptureLimits.Default, TimeSpan.FromSeconds(20), Token);

        Assert.False(result.TimedOut);
        Assert.Equal("stdin=0\n", Text(result.Stdout));
    }

    [Fact]
    public async Task TheEnvironment_IsExactlyTheOneGiven()
    {
        var given = Environment(("RUN_ONLY", "yes"), ("SECOND", "two"));

        var result = await CapturedLaunch.RunAsync(Reporter(given, "--env"), CaptureLimits.Default, _generous, Token);

        var seen = Text(result.Stdout)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line[..line.IndexOf('=', StringComparison.Ordinal)])
            .ToHashSet(EnvironmentMerge.Comparer);

        Assert.Equal(given.Keys.ToHashSet(EnvironmentMerge.Comparer), seen);
        Assert.DoesNotContain("KEYPASTE_TOKEN", seen);
    }

    [Fact]
    public async Task NotFound_IsAnOutcome_NotAThrow()
    {
        var missing = Path.Combine(_directory, OperatingSystem.IsWindows() ? "missing.exe" : "missing");

        var result = await CapturedLaunch.RunAsync(new ChildStart(missing, [], Environment(), _directory), CaptureLimits.Default, _generous, Token);

        Assert.Equal(ChildOutcome.NotFound, result.Outcome);
        Assert.Null(result.ExitCode);
        Assert.Contains("no such command", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Timeout_KillsTheTree_AndSaysTimedOut()
    {
        var pidFile = Path.Combine(_directory, "pid");
        var clock = Stopwatch.StartNew();

        var result = await CapturedLaunch.RunAsync(
            Reporter(null, "--pid-file", pidFile, "--sleep", "60"), CaptureLimits.Default, TimeSpan.FromSeconds(2), Token);

        Assert.True(result.TimedOut);
        Assert.Null(result.ExitCode);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"the run took {clock.Elapsed}");
        AssertGone(ReadPid(pidFile));
    }

    [Fact]
    public async Task Cancellation_KillsTheChild_ThenThrows()
    {
        var pidFile = Path.Combine(_directory, "pid");
        using var giveUp = CancellationTokenSource.CreateLinkedTokenSource(Token);

        var running = CapturedLaunch.RunAsync(Reporter(null, "--pid-file", pidFile, "--sleep", "60"), CaptureLimits.Default, _generous, giveUp.Token);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!File.Exists(pidFile) || new FileInfo(pidFile).Length == 0)
        {
            Assert.True(DateTime.UtcNow < deadline, "the child never started");
            await Task.Delay(50, Token);
        }

        await giveUp.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        AssertGone(ReadPid(pidFile));
    }

    [Fact]
    public async Task OnlyTheLastBytes_AreKept_AndHeadCutSaysSo()
    {
        var result = await CapturedLaunch.RunAsync(
            Reporter(null, "--bytes", "100000"), new CaptureLimits(1024, TimeSpan.FromSeconds(2)), _generous, Token);

        Assert.Equal(100000, result.Stdout.TotalBytes);
        Assert.Equal(1024, result.Stdout.Bytes.Length);
        Assert.True(result.Stdout.HeadCut);
        Assert.False(result.Stderr.HeadCut);
    }

    [Fact]
    public async Task ADaemonHoldingThePipe_DoesNotHangTheRun_AndDoesNotSurviveIt()
    {
        var clock = Stopwatch.StartNew();

        var result = await CapturedLaunch.RunAsync(Reporter(null, "--spawn-sleeper"), CaptureLimits.Default, _generous, Token);

        Assert.Equal(0, result.ExitCode);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), $"the run took {clock.Elapsed}");

        var sleeper = int.Parse(Text(result.Stdout).Trim().Split('=')[1], CultureInfo.InvariantCulture);

        if (OperatingSystem.IsMacOS())
        {
            // Best effort there, stated in THREATS.md T-35: nothing finds a descendant once its parent has gone.
            TryKill(sleeper);
            return;
        }

        AssertGone(sleeper);
    }

    private static int ReadPid(string path) => int.Parse(File.ReadAllText(path), CultureInfo.InvariantCulture);

    private static void AssertGone(int pid)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var process = Process.GetProcessById(pid);

                if (process.HasExited)
                {
                    return;
                }
            }
            catch (ArgumentException)
            {
                return;
            }

            Thread.Sleep(100);
        }

        TryKill(pid);
        Assert.Fail($"process {pid} outlived the run");
    }

    private static void TryKill(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone.
        }
    }
}
