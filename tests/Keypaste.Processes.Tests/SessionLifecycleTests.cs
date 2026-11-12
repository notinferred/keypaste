using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace Keypaste.Processes.Tests;

/// <summary>
/// The app starts, holds and ends the session authority without a terminal (4.4b), across real processes: a built keypaste mcp asks
/// tests/Keypaste.AppDriver composing the app's authority as its launch does, and keypaste-app itself runs once per home (F.38).
/// </summary>
public sealed partial class SessionLifecycleTests
{
    private const string _entry = "env/ci/DEPLOY_KEY";
    private const string _secret = "SENTINEL-LIFECYCLE-PASSWORD-5c20d4";

    [Fact]
    public void Launched_locked_the_app_refuses_until_unlocked_and_locking_and_quitting_each_leave_nobody_holding_the_vault()
    {
        using var workspace = Seeded();
        using var app = Hold(workspace, "--locked", "--held-prompt");
        app.WaitForOutput("^status locked");
        AssertRefusedByNobody(workspace);

        app.Send("unlock");
        var first = Serving(app, 1);
        AssertListedBy(workspace, first.Session);

        app.Send("lock");
        app.WaitForOutput("^status locked", 2);
        AssertRefusedByNobody(workspace);
        AssertNobodyHolds(workspace);

        app.Send("unlock");
        var second = Serving(app, 2);
        Assert.NotEqual(first.Session, second.Session);
        using (var bridge = McpBridge.Start(workspace))
        {
            var waiting = bridge.Send("request_credential", Request());
            app.WaitForOutput("^asking");
            app.CloseInput();
            app.WaitForOutput("^shut down");
            Assert.True(McpBridge.IsError(bridge.Reply(waiting)));
            var line = Workspace.Lines(workspace.OwnerLog)[^1];
            Assert.Equal(("vault-locked", second.Session), (line.GetProperty("method").GetString(), line.GetProperty("session").GetString()));
        }

        AssertRefusedByNobody(workspace);
        AssertNobodyHolds(workspace);
    }

    [Fact]
    public void A_killed_app_answers_nothing_and_a_relaunch_refuses_until_it_unlocks_a_new_session_on_the_same_endpoint()
    {
        using var workspace = Seeded();
        Served killed;
        using (var app = Hold(workspace, "--held-prompt"))
        {
            killed = Serving(app, 1);
            using var bridge = McpBridge.Start(workspace);
            var waiting = bridge.Send("request_credential", Request());
            app.WaitForOutput("^asking");
            app.Kill();
            var reply = bridge.Reply(waiting);
            Assert.True(McpBridge.IsError(reply));
            Assert.DoesNotContain(_secret, reply.ToString(), StringComparison.Ordinal);
            var line = Workspace.Lines(workspace.BridgeLog)[^1];
            Assert.Equal(("request_credential", "denied"), (line.GetProperty("tool").GetString(), line.GetProperty("decision").GetString()));
        }

        if (!OperatingSystem.IsWindows())
        {
            // A socket left behind is what makes the relaunch's takeover a takeover.
            Assert.True(File.Exists(Path.Combine(Path.GetTempPath(), $"CoreFxPipe_{killed.Endpoint}")), "the killed app left no socket");
        }

        AssertRefusedByNobody(workspace);
        using var relaunched = Hold(workspace, "--locked");
        relaunched.WaitForOutput("^status locked");
        AssertRefusedByNobody(workspace);
        relaunched.Send("unlock");
        var served = Serving(relaunched, 1);
        Assert.NotEqual(killed.Session, served.Session);
        Assert.Equal(killed.Endpoint, served.Endpoint);
        AssertListedBy(workspace, served.Session);
    }

    [Fact]
    public void With_keypaste_approve_holding_the_vault_the_apps_unlock_is_refused_naming_it_and_the_agent_keeps_answering()
    {
        using var workspace = Seeded();
        using var approve = RunningProcess.Start(Built.Keypaste, workspace.Environment, "approve", "--vault", workspace.Vault);
        approve.Send(Workspace.Master);
        var session = UnlockRegex().Match(approve.WaitForError("listening on")).Groups[1].Value;

        using var app = Hold(workspace);
        app.CloseInput();
        Assert.Equal(1, app.WaitForExit());
        var holder = $"keypaste approve (process {approve.Id})";
        Assert.Contains(app.Output, line => line.StartsWith("refused: ", StringComparison.Ordinal) && line.Contains(holder, StringComparison.Ordinal));
        Assert.Contains($"owner: Keypaste approve (process {approve.Id}) holds this vault", string.Join('\n', app.Output), StringComparison.Ordinal);
        Assert.Equal($"status held by {holder}", app.Output.Last(line => line.StartsWith("status ", StringComparison.Ordinal)));
        Assert.DoesNotContain(app.Output, line => line.StartsWith("status serving", StringComparison.Ordinal));

        AssertListedBy(workspace, session);
    }

    [Fact]
    public void One_app_runs_per_home_and_a_killed_one_leaves_its_home_to_the_next_start()
    {
        using var workspace = new Workspace();
        File.WriteAllText(Path.Combine(workspace.Home, "app.toml"), "[[settings]]\nstay_in_tray = 1\ncrash_reports = 0\n");

        using (var first = Launch(workspace))
        {
            first.WaitForOutput("^window hidden");
            Assert.Equal(0, StartAgain(workspace, "--background"));
            Thread.Sleep(TimeSpan.FromSeconds(2));
            Assert.DoesNotContain(first.Output, line => line == "window shown");

            AssertReopens(workspace, first);
            first.Kill();
        }

        using var next = Launch(workspace);
        next.WaitForOutput("^window hidden");
        AssertReopens(workspace, next);
        next.CloseInput();
        next.WaitForOutput("^shut down");
    }

    [Fact]
    public void Agents_asking_the_locked_app_are_told_it_is_locked_on_their_vault_and_one_notice_names_it_with_no_window()
    {
        const string before = "nothing holds this vault unlocked";
        const string locked = "keypaste app is open on this computer";
        using var workspace = Seeded();
        File.WriteAllText(Path.Combine(workspace.Home, "app.toml"), "[[settings]]\nstay_in_tray = 1\ncrash_reports = 0\n");
        var other = Path.Combine(Path.GetDirectoryName(workspace.Vault)!, "other.kdbx");
        var chosen = Path.Combine(Path.GetDirectoryName(workspace.Vault)!, "chosen.kdbx");
        workspace.Keypaste("init", Workspace.Master, Workspace.Master, other);
        workspace.Keypaste("init", Workspace.Master, Workspace.Master, chosen);
        Assert.Equal(0, Open(workspace));
        using (var use = RunningProcess.Start(Built.Keypaste, workspace.Environment, "use", chosen))
        {
            use.CloseInput();
            Assert.Equal(0, use.WaitForExit());
        }

        using var app = Launch(workspace);
        app.WaitForOutput("^window hidden");
        AssertRefusedSaying(workspace, other, before);
        AssertRefusedSaying(workspace, chosen, locked);
        app.WaitForOutput(@"^locked-notice shown .*chosen\.kdbx");
        AssertRefusedSaying(workspace, workspace.Vault, locked);
        AssertRefusedSaying(workspace, chosen, locked);
        Assert.DoesNotContain(app.Output, line => line == "window shown");

        app.Send("notice open");
        app.WaitForOutput("^locked-notice closed");
        app.WaitForOutput("^window shown");
        Assert.Equal("screen unlock", app.Output.Last(line => line.StartsWith("screen ", StringComparison.Ordinal)));
        AssertRefusedSaying(workspace, chosen, locked);
        AssertRefusedSaying(workspace, workspace.Vault, before);
        app.CloseInput();
        app.WaitForOutput("^shut down");

        Assert.Single(app.Output, line => line.StartsWith("locked-notice shown", StringComparison.Ordinal));
        AssertRefusedSaying(workspace, chosen, before);
    }

    private sealed record Served(string Session, string Endpoint);

    private static Workspace Seeded()
    {
        var workspace = new Workspace();
        workspace.Add(_entry, _secret);
        return workspace;
    }

    private static RunningProcess Hold(Workspace workspace, params string[] options)
    {
        var environment = workspace.Environment;
        environment["KEYPASTE_DRIVER_PASSWORD"] = Workspace.Master;
        return RunningProcess.Start(Built.AppDriver, environment, ["hold", workspace.Vault, .. options]);
    }

    private static RunningProcess Launch(Workspace workspace)
    {
        var app = RunningProcess.Start(Built.AppDriver, workspace.Environment, "launch", "--background");
        app.WaitForOutput("^running as process");
        return app;
    }

    private static int StartAgain(Workspace workspace, params string[] arguments)
    {
        using var again = RunningProcess.Start(Built.App, workspace.Environment, arguments);
        again.CloseInput();
        return again.WaitForExit();
    }

    // A person's second start hands itself to the running app, which shows its window before the second exits.
    private static void AssertReopens(Workspace workspace, RunningProcess running)
    {
        Assert.Equal(0, StartAgain(workspace));
        Assert.Contains(running.Output, line => line == "window shown");
        Assert.DoesNotContain(running.Output, line => line.StartsWith("shut down", StringComparison.Ordinal));
    }

    private static int Open(Workspace workspace)
    {
        var environment = workspace.Environment;
        environment["KEYPASTE_DRIVER_PASSWORD"] = Workspace.Master;
        using var open = RunningProcess.Start(Built.AppDriver, environment, "open", workspace.Vault);
        open.CloseInput();
        return open.WaitForExit();
    }

    private static Served Serving(RunningProcess app, int count)
    {
        var line = app.WaitForOutput("^status serving", count);
        var serving = ServingRegex().Match(line);
        Assert.True(serving.Success, line);
        return new(serving.Groups[1].Value, serving.Groups[2].Value);
    }

    private static void AssertRefusedByNobody(Workspace workspace) => AssertRefusedByNobody(workspace, workspace.Vault);

    private static void AssertRefusedByNobody(Workspace workspace, string vault, string? saying = null)
    {
        var replies = Ask(workspace, vault);
        Assert.All(replies, reply => Assert.True(McpBridge.IsError(reply)));
        Assert.All(replies, reply => Assert.DoesNotContain(_secret, reply.ToString(), StringComparison.Ordinal));
        if (saying is not null)
        {
            Assert.All(replies, reply => Assert.Contains(saying, Text(reply), StringComparison.Ordinal));
        }

        var lines = Workspace.Lines(workspace.BridgeLog).TakeLast(2).ToList();
        Assert.All(lines, line => Assert.Equal("denied", line.GetProperty("decision").GetString()));
        Assert.All(lines, line => Assert.False(line.TryGetProperty("session", out _)));
    }

    private static void AssertRefusedSaying(Workspace workspace, string vault, string text)
    {
        AssertRefusedByNobody(workspace, vault, text);
        Assert.All(Workspace.Lines(workspace.BridgeLog).TakeLast(2).ToList(), line => Assert.Equal("no-approver", line.GetProperty("method").GetString()));
    }

    private static void AssertListedBy(Workspace workspace, string session)
    {
        using var bridge = McpBridge.Start(workspace);
        var listing = bridge.Call("list_entry_names");
        Assert.False(McpBridge.IsError(listing));
        Assert.Contains("DEPLOY_KEY", listing.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, bridge.Close());
        var line = Workspace.Lines(workspace.OwnerLog).Last(l => l.GetProperty("tool").GetString() == "list_entry_names");
        Assert.Equal(("granted", session), (line.GetProperty("decision").GetString(), line.GetProperty("session").GetString()));
    }

    private static void AssertNobodyHolds(Workspace workspace) => Assert.Equal(0, Open(workspace));

    private static List<JsonElement> Ask(Workspace workspace, string vault)
    {
        using var bridge = McpBridge.Start(workspace, vault: vault);
        List<JsonElement> replies = [bridge.Call("list_entry_names"), bridge.Call("request_credential", Request())];
        Assert.Equal(0, bridge.Close());
        return replies;
    }

    private static string Text(JsonElement reply) =>
        string.Join(' ', reply.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!.Split('\n'));

    private static JsonObject Request() => new() { ["entry"] = _entry, ["field"] = "password", ["reason"] = "ci lifecycle probe" };

    [GeneratedRegex("^status serving ([0-9a-f]+) as process [0-9]+ on (.*)$")]
    private static partial Regex ServingRegex();

    [GeneratedRegex(" for unlock ([0-9a-f]+),")]
    private static partial Regex UnlockRegex();
}
