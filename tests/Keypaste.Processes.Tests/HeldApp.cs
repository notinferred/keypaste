using System.Text.RegularExpressions;
using Xunit;

namespace Keypaste.Processes.Tests;

/// <summary>tests/Keypaste.AppDriver holding a vault unlocked and served as the app does, driven a line of standard input at a time.</summary>
internal sealed class HeldApp : IDisposable
{
    private readonly RunningProcess _driver;
    private int _sessions;

    private HeldApp(RunningProcess driver)
    {
        _driver = driver;
        Session = NextSession();
    }

    /// <summary>The session of the latest unlock.</summary>
    internal string Session { get; private set; }

    /// <summary>The app's process, as a refusal names it.</summary>
    internal int Process { get; private set; }

    internal RunningProcess Driver => _driver;

    internal static HeldApp Hold(Workspace workspace, params string[] options) => Hold(workspace, newPassword: null, options);

    /// <summary>Holds the vault, with <paramref name="newPassword"/> as the value an <c>edit</c> types, when one is given.</summary>
    internal static HeldApp Hold(Workspace workspace, string? newPassword, params string[] options)
    {
        var environment = workspace.Environment;
        environment["KEYPASTE_DRIVER_PASSWORD"] = Workspace.Master;
        if (newPassword is not null)
        {
            environment["KEYPASTE_DRIVER_NEW_PASSWORD"] = newPassword;
        }

        var held = new HeldApp(RunningProcess.Start(Built.AppDriver, environment, ["hold", workspace.Vault, .. options]));
        Assert.DoesNotContain(held._driver.Output, line => line.Contains("not served", StringComparison.Ordinal));
        return held;
    }

    /// <summary>Sends one of hold's commands, such as lock, unlock or deny.</summary>
    internal void Send(string command) => _driver.Send(command);

    /// <summary>Sends a command and returns the first line the app prints after it.</summary>
    internal string Act(string command)
    {
        var before = _driver.Output.Count;
        Send(command);
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (deadline.Elapsed < RunningProcess.Wait)
        {
            if (_driver.Output.Count > before)
            {
                return _driver.Output[before];
            }

            Thread.Sleep(50);
        }

        Assert.Fail($"the app never answered '{command}'");
        return "";
    }

    internal string WaitFor(string pattern, int count = 1) => _driver.WaitForOutput(pattern, count);

    /// <summary>Unlocks again and waits for the new session.</summary>
    internal void Unlock()
    {
        Send("unlock");
        Session = NextSession();
    }

    /// <summary>Quits the app as a person does, by closing what drives it.</summary>
    internal void Quit() => _driver.CloseInput();

    internal void Kill() => _driver.Kill();

    public void Dispose() => _driver.Dispose();

    private string NextSession()
    {
        var line = _driver.WaitForOutput("^holding session", ++_sessions);
        var held = Regex.Match(line, "^holding session ([0-9a-f]+).* as process ([0-9]+)");
        Assert.True(held.Success, $"the app printed no session: {line}");
        Process = int.Parse(held.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        return held.Groups[1].Value;
    }
}
