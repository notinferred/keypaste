using System.Diagnostics;
using Xunit;

namespace Keypaste.Mcp.Tests;

/// <summary>
/// F.9's regressions: each runs the product in a process whose thread pool has no worker to spare,
/// and checks that it still answers the way it would with workers to spare.
/// </summary>
/// <remarks>
/// <para>
/// The shortage is arranged, not waited for. pool-probe run 34701431621 showed the bridge failing 16,
/// 8 and 0 times in 80 as the pool's floor went from 1 to 4 to 256, but a test that waited for a busy
/// runner to supply that would be red one run in ten and green the rest. The scenarios live in
/// <c>Keypaste.PoolStarver</c>, a separate process, because a pool is one per process and pinning this
/// one would run every test beside these short of workers too.
/// </para>
/// <para>
/// Every approver these talk to runs here, in the unpinned test host, so a scenario measures what the
/// product does when <em>its</em> pool is short — not an approver too starved to answer.
/// </para>
/// </remarks>
public sealed class PoolShortageTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// Class 1: a live approver must not be reported as absent because the bridge's pool could not
    /// start the connect before the connect's own deadline ran.
    /// </summary>
    /// <remarks>
    /// Asserts the classification rather than a duration. The refusal for <c>Unreachable</c> tells a
    /// person to start <c>keypaste agent</c>, and here one is running; a longer connect budget changes
    /// nothing about that, because the scenario holds both workers for three budgets whatever the
    /// budget is.
    /// </remarks>
    [Fact]
    public async Task ALiveApprover_IsNotReportedAbsent_WhenTheConnectCannotStartBeforeItsDeadline()
    {
        await using var approver = new FakeApprover();
        approver.Start();

        var (code, output) = await StarveAsync("connect-behind-its-deadline", approver.PipeName);

        Assert.True(code == 0, $"the scenario was not arranged (exit {code}):{Environment.NewLine}{output}");
        Assert.Contains("workers 2..2", output, StringComparison.Ordinal);
        Assert.Contains("free workers once arranged 0", output, StringComparison.Ordinal);
        Assert.True(
            output.Contains("outcome Answered", StringComparison.Ordinal),
            $"a running approver was not answered from a pool with no worker to spare:{Environment.NewLine}{output}");
    }

    /// <summary>
    /// Class 2: an open MCP harness must not hold the workers every other test in its process needs.
    /// </summary>
    /// <remarks>
    /// No budget is asserted, so none can be widened to buy it green: the canary only has to run at all
    /// while a harness sits open between calls, which is where forty harnesses across six test classes
    /// spend most of a suite.
    /// </remarks>
    [Fact]
    public async Task AnOpenHarness_LeavesTheProcessAWorker()
    {
        var (code, output) = await StarveAsync("an-open-harness", pipeName: "unused");

        Assert.True(code == 0, $"the scenario was not arranged (exit {code}):{Environment.NewLine}{output}");
        Assert.Contains("workers 2..2", output, StringComparison.Ordinal);
        Assert.Contains("both reads outstanding", output, StringComparison.Ordinal);
        Assert.True(
            output.Contains("canary ran", StringComparison.Ordinal),
            $"an open harness left no worker for anything else in its process:{Environment.NewLine}{output}");
    }

    private static async Task<(int Code, string Output)> StarveAsync(string scenario, string pipeName)
    {
        var info = new ProcessStartInfo
        {
            FileName = Helper("Keypaste.PoolStarver"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        info.ArgumentList.Add(scenario);
        info.ArgumentList.Add(pipeName);

        // Before the process exists: the runtime reads these once, when it builds the pool.
        info.Environment["DOTNET_ThreadPool_ForceMinWorkerThreads"] = "2";
        info.Environment["DOTNET_ThreadPool_ForceMaxWorkerThreads"] = "2";

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("the pool starver did not start");

        var stdout = process.StandardOutput.ReadToEndAsync(Token);
        var stderr = process.StandardError.ReadToEndAsync(Token);

        using var bound = CancellationTokenSource.CreateLinkedTokenSource(Token);
        bound.CancelAfter(TimeSpan.FromSeconds(90));

        try
        {
            await process.WaitForExitAsync(bound.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("the pool starver never finished");
        }

        return (process.ExitCode, ((await stdout) + (await stderr)).Trim());
    }

    private static string Helper(string name)
    {
        var directory = AppContext.BaseDirectory;

        while (!File.Exists(Path.Combine(directory, "keypaste.slnx")))
        {
            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar))
                ?? throw new InvalidOperationException("Could not locate keypaste.slnx above " + AppContext.BaseDirectory);
        }

        var configuration = AppContext.BaseDirectory.Contains("debug", StringComparison.OrdinalIgnoreCase)
            ? "debug"
            : "release";

        return Path.Combine(
            directory, "artifacts", "bin", name, configuration, OperatingSystem.IsWindows() ? name + ".exe" : name);
    }
}
