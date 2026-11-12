using System.Text.Json.Nodes;
using Xunit;

namespace Keypaste.Processes.Tests;

/// <summary>
/// Agents shows what the app's session holds and History what agents asked for (4.3b, N.3), when the asking is a built keypaste mcp in
/// another process: tests/Keypaste.AppDriver's <c>activity</c> prints what the screens' own view models read from the authority and the log.
/// </summary>
/// <remarks>How the rows, the prompt and the settings behave is App.Tests' <c>AgentActivityViewModelTests</c> and <c>DesktopApprovalTests</c>.</remarks>
public sealed class AgentActivityTests
{
    private const string _entry = "env/ci/DEPLOY_KEY";
    private const string _label = "ci-activity-probe";
    private const string _secret = "SENTINEL-ACTIVITY-PASSWORD-2d7a51";

    [Fact]
    public void A_request_waiting_from_another_process_is_listed_its_prompt_brought_forward_disarmed_and_its_release_is_in_History()
    {
        using var workspace = Seeded();
        using var app = HeldApp.Hold(workspace);
        var shown = new Shown(app);

        var before = shown.Activity();
        Assert.Contains(before, line => line.StartsWith("history-message Nothing has asked", StringComparison.Ordinal));
        Assert.DoesNotContain(before, line => line.StartsWith("waiting ", StringComparison.Ordinal) || line.StartsWith("history ", StringComparison.Ordinal));

        using var bridge = McpBridge.Start(workspace, label: _label);
        var asked = bridge.Send("request_credential", Request("password"));
        app.WaitFor("^prompt client");
        Assert.Contains(shown.Activity(), line => line.StartsWith($"waiting n=1 client=ci-probe label={_label} entry={_entry} field=password left=answered for you in ", StringComparison.Ordinal));

        app.Send("show-prompt");
        Assert.Equal("prompt raised armed=false focus=Deny", app.WaitFor("^prompt raised "));
        app.Send("once");
        Assert.Contains(_secret, bridge.Reply(asked).ToString(), StringComparison.Ordinal);

        var after = shown.Activity();
        Assert.DoesNotContain(after, line => line.StartsWith("waiting ", StringComparison.Ordinal));
        Assert.Contains(after, line => line.StartsWith("history ", StringComparison.Ordinal) && line.Contains("DEPLOY_KEY", StringComparison.Ordinal) && line.Contains("Approved", StringComparison.Ordinal));
        Assert.DoesNotContain(app.Driver.Output, line => line.Contains(_secret, StringComparison.Ordinal));
    }

    [Fact]
    public void A_lock_answers_the_waiting_request_and_empties_the_list_and_History_reads_the_whole_file_after_the_next_unlock()
    {
        using var workspace = Seeded();
        using var app = HeldApp.Hold(workspace);
        var shown = new Shown(app);
        var first = app.Session;
        using (var bridge = McpBridge.Start(workspace, label: _label))
        {
            var allowed = bridge.Send("request_credential", Request("password"));
            app.WaitFor("^prompt client");
            app.Send("once");
            bridge.Reply(allowed);

            var waiting = bridge.Send("request_credential", Request("notes"));
            app.WaitFor("^prompt client", 2);
            Assert.Contains(shown.Activity(), line => line.StartsWith("waiting n=1 ", StringComparison.Ordinal));
            app.Send("lock");
            app.WaitFor("^locked");
            Assert.True(McpBridge.IsError(bridge.Reply(waiting)));
            var line = Workspace.Lines(workspace.OwnerLog)[^1];
            Assert.Equal(("vault-locked", first), (line.GetProperty("method").GetString(), line.GetProperty("session").GetString()));
        }

        var locked = shown.Activity();
        Assert.Contains(locked, line => line.StartsWith("unavailable ", StringComparison.Ordinal));
        Assert.DoesNotContain(locked, line => line.StartsWith("waiting ", StringComparison.Ordinal));

        app.Unlock();
        var unlocked = shown.Activity();
        Assert.DoesNotContain(unlocked, line => line.StartsWith("unavailable ", StringComparison.Ordinal) || line.StartsWith("waiting ", StringComparison.Ordinal));
        Assert.Contains(unlocked, line => line.StartsWith("history ", StringComparison.Ordinal) && line.Contains("Approved", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_turned_on_and_off_in_an_agents_settings_is_sealed_and_recorded_and_each_request_is_still_asked()
    {
        using var workspace = Seeded();
        using var app = HeldApp.Hold(workspace);
        using var bridge = McpBridge.Start(workspace, label: _label);
        var settings = 0;
        var changes = 0;
        var prompts = 0;

        // Agents lists an agent that has asked, so this one asks once first.
        Allowed();
        Assert.Contains($"agent={_label} run=off", Settings(), StringComparison.Ordinal);
        Change("run on");
        Assert.Contains("[[seal]]", File.ReadAllText(Path.Combine(workspace.Home, "agents.toml")), StringComparison.Ordinal);
        Assert.Contains("run=on", Settings(), StringComparison.Ordinal);

        Allowed();
        Change("run off");
        Assert.Contains("run=off", Settings(), StringComparison.Ordinal);

        void Allowed()
        {
            var asked = bridge.Send("request_credential", Request("password"));
            app.WaitFor("^prompt client", ++prompts);
            app.Send("once");
            Assert.False(McpBridge.IsError(bridge.Reply(asked)));
            var line = Workspace.Lines(workspace.OwnerLog)[^1];
            Assert.Equal(("request_credential", "granted", "prompt"), (line.GetProperty("tool").GetString(), line.GetProperty("decision").GetString(), line.GetProperty("method").GetString()));
        }

        string Settings()
        {
            app.Send($"settings {_label}");
            return app.WaitFor("^settings agent=", ++settings);
        }

        void Change(string setting)
        {
            app.Send($"setting {_label} {setting}");
            app.WaitFor("^setting-done ", ++changes);
            var recorded = Workspace.Lines(workspace.OwnerLog)[^1];
            Assert.Equal(
                ("access_change", "keypaste app", _label, "granted", "rules-changed"),
                (recorded.GetProperty("tool").GetString(), recorded.GetProperty("client").GetProperty("name").GetString(),
                 recorded.GetProperty("client").GetProperty("label").GetString(), recorded.GetProperty("decision").GetString(),
                 recorded.GetProperty("method").GetString()));
        }
    }

    [Fact]
    public void History_says_plainly_when_the_log_could_not_be_read_and_shows_no_record_from_it()
    {
        using var workspace = Seeded();
        Directory.CreateDirectory(workspace.OwnerLog);
        using var app = HeldApp.Hold(workspace);

        var shown = new Shown(app).Activity();

        Assert.Contains(shown, line => line.StartsWith("history-message That log couldn't be read", StringComparison.Ordinal));
        Assert.DoesNotContain(shown, line => line.StartsWith("history ", StringComparison.Ordinal));
    }

    private static Workspace Seeded()
    {
        var workspace = new Workspace();
        workspace.Add(_entry, _secret);
        return workspace;
    }

    private static JsonObject Request(string field) => new() { ["entry"] = _entry, ["field"] = field, ["reason"] = "ci activity probe" };

    /// <summary>What the driver's <c>activity</c> prints, one block per call.</summary>
    private sealed class Shown(HeldApp app)
    {
        private int _blocks;

        internal IReadOnlyList<string> Activity()
        {
            app.Send("activity");
            app.WaitFor("^activity end", ++_blocks);
            var output = app.Driver.Output;
            var ends = output.Select((line, index) => (line, index)).Where(pair => pair.line.StartsWith("activity end", StringComparison.Ordinal)).Select(pair => pair.index).ToList();
            var start = _blocks > 1 ? ends[_blocks - 2] + 1 : 0;
            return [.. output.Skip(start).Take(ends[_blocks - 1] - start)
                .Where(line => line.StartsWith("unavailable", StringComparison.Ordinal) || line.StartsWith("waiting", StringComparison.Ordinal) || line.StartsWith("history", StringComparison.Ordinal))];
        }
    }
}
