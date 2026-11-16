using Xunit;
using static System.FormattableString;

namespace Keypaste.Processes.Tests;

/// <summary>The Windows app runs with no .NET diagnostics port, which would let any process of its user read the unlocked vault.</summary>
public sealed class DiagnosticsPortTests
{
    [Fact]
    public void The_Windows_app_relaunches_itself_with_no_diagnostics_port()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The macOS bundle and the AppImage set the variable as they start the app.");

        using var workspace = new Workspace();
        using var first = RunningProcess.Start(Built.App, workspace.Environment, "--background");
        WaitFor(() => File.Exists(Path.Combine(workspace.Home, "sessions", "app.lock")), "the app to claim its home");
        var app = ChildOf(first.Id);

        var pipes = Directory.GetFiles(@"\\.\pipe\").Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Invariant($"dotnet-diagnostic-{first.Id}"), pipes);
        Assert.DoesNotContain(Invariant($"dotnet-diagnostic-{app}"), pipes);
    }

    private static int ChildOf(int parent)
    {
        using var query = RunningProcess.Start("powershell.exe", new Dictionary<string, string>(), "-NoProfile", "-Command", Invariant($"(Get-CimInstance Win32_Process -Filter 'ParentProcessId={parent}').ProcessId"));
        Assert.Equal(0, query.WaitForExit());
        return int.Parse(Assert.Single(query.Output, line => line.Length > 0), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void WaitFor(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + RunningProcess.Wait;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, Invariant($"waited {RunningProcess.Wait} for {what}"));
            Thread.Sleep(100);
        }
    }
}
