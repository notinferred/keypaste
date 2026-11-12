using System.Diagnostics;
using System.Runtime.Versioning;
using Xunit;

namespace Keypaste.Processes.Tests;

/// <summary>
/// On macOS the app's own Run opens Terminal (E.1d): a keypaste.app laid out as a release lays it out carries the real launcher and a
/// keypaste that starts the built CLI, the held app presses Run in a terminal and Start, real Terminal runs the launcher, and the window's
/// <c>keypaste run --session</c> presents the launch pass to the app, which asks nothing more.
/// </summary>
/// <remarks>
/// It opens Terminal and ends it with pkill, so it runs only on a CI runner, and refuses to start while a Terminal is open there. The run
/// asked about in the app's prompt, everywhere, is <c>RunSessionTests</c>'.
/// </remarks>
[SupportedOSPlatform("macos")]
public sealed class MacTerminalRunTests
{
    private const string _deploy = "SENTINEL-RUN-SESSION-DEPLOY-5e2b90";
    private const string _database = "SENTINEL-RUN-SESSION-DB-a17c3d";
    private const string _confirmed = "keypaste run: starting with ci (dev) as you confirmed in the keypaste app";
    private const string _reports = "printf \"deploy=%s database=%s launch=%s driver=%s home=%s\" \"${DEPLOY_KEY-unset}\" \"${DB_URL-unset}\" \"${KEYPASTE_LAUNCH-unset}\" \"${KEYPASTE_DRIVER_PASSWORD-unset}\" \"${KEYPASTE_HOME-unset}\" >terminal.tmp && mv terminal.tmp terminal.txt";

    [Fact]
    public void Run_and_Open_terminal_start_in_Terminal_with_the_set_and_one_prompt_line_and_a_lock_before_the_window_asks_starts_nothing()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Only macOS has Terminal.");
        Assert.SkipUnless(Environment.GetEnvironmentVariable("CI") == "true", "This opens Terminal and ends every Terminal with pkill, so it runs only on a CI runner.");
        Assert.False(Running("Terminal"), "Terminal is running: this test opens Terminal and ends it with pkill, so quit it first");

        var userTemp = Command("/usr/bin/getconf", ["DARWIN_USER_TEMP_DIR"]).Trim().TrimEnd('/');
        var requests = Path.Combine(userTemp, "keypaste-launch");
        Assert.True(!Directory.Exists(requests) || Directory.GetFileSystemEntries(requests).Length == 0, $"a run from the app is already waiting in {requests}");

        using var workspace = new Workspace();
        var root = Path.GetDirectoryName(workspace.Vault)!;
        var project = Path.Combine(root, "project");
        Directory.CreateDirectory(project);
        workspace.Keypaste("env", Workspace.Master, _deploy, "set", "ci", "DEPLOY_KEY", "-p", "dev", "--vault", workspace.Vault);
        workspace.Keypaste("env", Workspace.Master, _database, "set", "ci", "DB_URL", "-p", "dev", "--vault", workspace.Vault);

        var held = Path.Combine(root, "terminal-held");
        var windows = Path.Combine(root, "terminal-windows.txt");
        var exits = Path.Combine(root, "terminal-exits.txt");
        var reported = Path.Combine(project, "terminal.txt");
        var bundle = Bundle(root, held, windows, exits);
        File.WriteAllText(windows, "");
        File.WriteAllText(exits, "");

        try
        {
            var environment = workspace.Environment;
            environment["KEYPASTE_DRIVER_PASSWORD"] = Workspace.Master;
            environment["TMPDIR"] = userTemp;
            using var driver = RunningProcess.Start(Built.AppDriver, environment, "hold", workspace.Vault, "--terminal-bundle", bundle);
            driver.WaitForOutput("^holding session");

            // Run in a terminal: what Terminal runs has the set and the app's home, and the app's card was the only question.
            var audited = Workspace.Count(workspace.OwnerLog);
            driver.Send($"terminal run ci {project} {_reports}");
            driver.WaitForOutput("^terminal [a-z]*:");
            Assert.Contains(driver.Output, line => line.StartsWith("terminal launched: ", StringComparison.Ordinal));
            Assert.StartsWith(
                $"terminal-card title=Run this in a terminal with 2 variables from ci? starts={_reports} directory={project} keys=DB_URL, DEPLOY_KEY sources=",
                driver.Output.Last(line => line.StartsWith("terminal-card ", StringComparison.Ordinal)),
                StringComparison.Ordinal);
            Assert.EndsWith("env/ci/.env", driver.Output.Last(line => line.StartsWith("terminal-card ", StringComparison.Ordinal)), StringComparison.Ordinal);
            WaitForText(reported, "deploy=");
            Assert.Equal($"deploy={_deploy} database={_database} launch=unset driver=unset home={workspace.Home}", File.ReadAllText(reported));
            WaitForText(windows, _confirmed);
            AssertOneLine(workspace, audited, "granted", "prompt");
            Assert.Equal(["env/ci/.env"], Workspace.Lines(workspace.OwnerLog)[^1].GetProperty("entries").EnumerateArray().Select(e => e.GetString()));
            AssertNoRequestLeft(requests);

            // A later window gets Terminal's own environment, which is what the app gave open, holding no keypaste setting.
            var later = Path.Combine(root, "later-window-environment.txt");
            var command = Path.Combine(root, "later-window.command");
            File.WriteAllText(command, $"#!/bin/sh\n/usr/bin/env >'{later}.tmp' && mv '{later}.tmp' '{later}'\n");
            File.SetUnixFileMode(command, (UnixFileMode)0b111_101_101);
            Command("/usr/bin/open", ["-b", "com.apple.Terminal", command]);
            WaitForText(later, "");
            var laterEnvironment = File.ReadAllText(later);
            Assert.DoesNotMatch("(?im)^KEYPASTE_", laterEnvironment);
            Assert.DoesNotContain(Workspace.Master, laterEnvironment, StringComparison.Ordinal);

            // Open terminal: the window's shell is released the set the same way.
            audited = Workspace.Count(workspace.OwnerLog);
            driver.Send($"terminal open ci {project}");
            driver.WaitForOutput("^terminal [a-z]*:", 2);
            Assert.Equal(2, driver.Output.Count(line => line.StartsWith("terminal launched: ", StringComparison.Ordinal)));
            Assert.StartsWith(
                $"terminal-card title=Open a terminal with 2 variables from ci? starts=Terminal directory={project} keys=DB_URL, DEPLOY_KEY sources=",
                driver.Output.Last(line => line.StartsWith("terminal-card ", StringComparison.Ordinal)),
                StringComparison.Ordinal);
            WaitForCount(windows, _confirmed, 2);
            AssertOneLine(workspace, audited, "granted", "prompt");
            AssertNoRequestLeft(requests);

            // A lock and unlock between Start and the window's request: the unlock that confirmed it held its pass.
            audited = Workspace.Count(workspace.OwnerLog);
            File.WriteAllText(held, "");
            driver.Send($"terminal run ci {project} touch ran-after-a-lock");
            driver.WaitForOutput("^terminal [a-z]*:", 3);
            Assert.Equal(3, driver.Output.Count(line => line.StartsWith("terminal launched: ", StringComparison.Ordinal)));
            driver.Send("lock");
            driver.WaitForOutput("^locked");
            driver.Send("unlock");
            driver.WaitForOutput("^holding session", 2);
            File.Delete(held);
            WaitForText(exits, "");
            Assert.NotEqual("0", File.ReadAllText(exits).Trim());
            Assert.Contains("expired or was already used", File.ReadAllText(windows), StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(project, "ran-after-a-lock")), "the command ran after a lock");
            AssertOneLine(workspace, audited, "denied", "invalid-request");
            AssertNoRequestLeft(requests);

            Assert.DoesNotContain(driver.Output, line => line.StartsWith("env-prompt ", StringComparison.Ordinal) || line.StartsWith("run-prompt ", StringComparison.Ordinal) || line.StartsWith("prompt ", StringComparison.Ordinal));
            var printed = File.ReadAllText(windows);
            Assert.DoesNotContain("answer in its prompt", printed, StringComparison.Ordinal);
            Assert.DoesNotContain("master password", printed, StringComparison.OrdinalIgnoreCase);
            foreach (var value in new[] { _deploy, _database })
            {
                Assert.DoesNotContain(driver.Output, line => line.Contains(value, StringComparison.Ordinal));
                Assert.DoesNotContain(value, printed, StringComparison.Ordinal);
                Assert.DoesNotContain(value, Workspace.Text(workspace.OwnerLog), StringComparison.Ordinal);
            }
        }
        finally
        {
            Command("/usr/bin/pkill", ["-x", "Terminal"], allowFailure: true);
        }

        // Ending Terminal ends every window and what its run started.
        var deadline = Stopwatch.StartNew();
        while ((Running("Terminal") || Matches($"run --session --vault {workspace.Vault}")) && deadline.Elapsed < RunningProcess.Wait)
        {
            Thread.Sleep(200);
        }

        Assert.False(Running("Terminal"), "Terminal outlived pkill");
        Assert.False(Matches($"run --session --vault {workspace.Vault}"), "a run Terminal started outlived Terminal");
    }

    /// <summary>A keypaste.app beside the real launcher, whose keypaste starts the built CLI once <paramref name="held"/> is gone, keeps what the run prints and records how it exited.</summary>
    private static string Bundle(string root, string held, string windows, string exits)
    {
        var bundle = Path.Combine(root, "keypaste.app");
        Directory.CreateDirectory(Path.Combine(bundle, "Contents", "MacOS"));
        Directory.CreateDirectory(Path.Combine(bundle, "Contents", "Resources"));
        var launcher = Path.Combine(bundle, "Contents", "Resources", "keypaste-launch.command");
        File.Copy(Path.Combine(Built.Repository, "packaging", "macos", "keypaste-launch.command"), launcher);
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } dotnetRoot ? $"DOTNET_ROOT='{dotnetRoot}'\nexport DOTNET_ROOT\n" : "";
        var keypaste = Path.Combine(bundle, "Contents", "MacOS", "keypaste");
        File.WriteAllText(keypaste, $"""
            #!/bin/sh
            {dotnet}if [ -e '{held}' ]; then
              while [ -e '{held}' ]; do sleep 0.1; done
              exec </dev/null
            fi
            '{Built.Keypaste}' "$@" 2>>'{windows}'
            status=$?
            echo "$status" >>'{exits}'
            exit "$status"

            """);
        foreach (var script in new[] { keypaste, launcher })
        {
            File.SetUnixFileMode(script, (UnixFileMode)0b111_101_101);
        }

        return bundle;
    }

    private static void AssertOneLine(Workspace workspace, int before, string decision, string method)
    {
        var lines = Workspace.Lines(workspace.OwnerLog, before);
        var line = Assert.Single(lines);
        Assert.Equal(
            ("keypaste run --session", decision, method),
            (line.GetProperty("client").GetProperty("name").GetString(), line.GetProperty("decision").GetString(), line.GetProperty("method").GetString()));
        if (decision == "granted")
        {
            Assert.Equal("2 variable(s), confirmed in the app", line.GetProperty("reason").GetString());
        }
    }

    private static void AssertNoRequestLeft(string requests) =>
        Assert.True(!Directory.Exists(requests) || Directory.GetFileSystemEntries(requests).Length == 0, $"a request was left in {requests}");

    private static void WaitForText(string path, string text) => WaitForCount(path, text, 1);

    private static void WaitForCount(string path, string text, int count)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < RunningProcess.Wait)
        {
            if (File.Exists(path) && File.ReadAllText(path) is { Length: > 0 } content
                && (text.Length == 0 || content.Split(text).Length - 1 >= count))
            {
                return;
            }

            Thread.Sleep(200);
        }

        Assert.Fail($"{path} never held '{text}' {count} time(s)");
    }

    private static bool Running(string name) => Command("/usr/bin/pgrep", ["-x", name], allowFailure: true).Length > 0;

    private static bool Matches(string commandLine) => Command("/usr/bin/pgrep", ["-f", commandLine], allowFailure: true).Length > 0;

    private static string Command(string program, string[] arguments, bool allowFailure = false)
    {
        using var run = RunningProcess.Start(program, new Dictionary<string, string>(), arguments);
        run.CloseInput();
        var exit = run.WaitForExit();
        Assert.True(allowFailure || exit == 0, $"{program} {string.Join(' ', arguments)} exited {exit}: {string.Join('\n', run.Error)}");
        return string.Join('\n', run.Output);
    }
}
