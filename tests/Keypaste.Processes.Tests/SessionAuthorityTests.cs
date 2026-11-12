using System.Text.Json.Nodes;
using Xunit;

namespace Keypaste.Processes.Tests;

/// <summary>
/// The desktop owns the vault it unlocks (U.1), across real processes: the built keypaste and keypaste mcp, and tests/Keypaste.AppDriver
/// serving the vault as the app does.
/// </summary>
/// <remarks>A stale session and an unattached bridge are <c>SessionAuthorityTests</c>' in Core.Tests, over a real pipe in one process.</remarks>
public sealed class SessionAuthorityTests
{
    private const string _entry = "env/ci/DEPLOY_KEY";
    private const string _secret = "SENTINEL-SESSION-PASSWORD-4e81c7";

    [Fact]
    public void A_request_reaches_the_apps_session_is_put_to_the_person_and_only_the_app_logs_it()
    {
        using var workspace = new Workspace();
        workspace.Add(_entry, _secret);
        using var app = HeldApp.Hold(workspace);
        using var bridge = McpBridge.Start(workspace);

        var listing = bridge.Call("list_entry_names");
        var asked = bridge.Send("request_credential", Request());
        app.WaitFor("^prompt client");
        app.Send("deny");
        var denied = bridge.Reply(asked);

        Assert.False(McpBridge.IsError(listing));
        Assert.Contains("DEPLOY_KEY", listing.ToString(), StringComparison.Ordinal);
        Assert.True(McpBridge.IsError(denied));
        Assert.DoesNotContain(_secret, string.Join('\n', bridge.Process.Output), StringComparison.Ordinal);
        Assert.Equal(0, bridge.Close());

        var lines = Workspace.Lines(workspace.OwnerLog);
        Assert.Equal(2, lines.Count);
        Assert.Equal(("list_entry_names", "granted", app.Session), (lines[0].GetProperty("tool").GetString(), lines[0].GetProperty("decision").GetString(), lines[0].GetProperty("session").GetString()));
        Assert.Equal(("request_credential", "denied", "prompt", app.Session), (lines[1].GetProperty("tool").GetString(), lines[1].GetProperty("decision").GetString(), lines[1].GetProperty("method").GetString(), lines[1].GetProperty("session").GetString()));
        Assert.Empty(Workspace.Lines(workspace.BridgeLog));
    }

    [Fact]
    public void While_the_app_holds_the_vault_keypaste_approve_and_a_second_app_are_refused_by_name_before_any_password()
    {
        using var workspace = new Workspace();
        using var app = HeldApp.Hold(workspace);
        var holder = $"already unlocked in the keypaste desktop app (process {app.Process})";

        using (var approve = RunningProcess.Start(Built.Keypaste, workspace.Environment, "approve", "--vault", workspace.Vault))
        {
            approve.Send(Workspace.Master);
            approve.CloseInput();
            Assert.NotEqual(0, approve.WaitForExit());
            var said = string.Join('\n', approve.Error);
            Assert.Contains(holder, said, StringComparison.Ordinal);
            Assert.DoesNotContain("Master password", said, StringComparison.Ordinal);
            Assert.DoesNotContain("listening on", said, StringComparison.Ordinal);
        }

        var environment = workspace.Environment;
        environment["KEYPASTE_DRIVER_PASSWORD"] = Workspace.Master;
        using var second = RunningProcess.Start(Built.AppDriver, environment, "open", workspace.Vault);
        Assert.Equal(1, second.WaitForExit());
        Assert.Contains(second.Output.Concat(second.Error), line => line.Contains(holder, StringComparison.Ordinal));
    }

    [Fact]
    public void Locked_the_app_answers_nothing_and_the_bridge_logs_each_refusal_as_reaching_no_session()
    {
        using var workspace = new Workspace();
        workspace.Add(_entry, _secret);
        using var app = HeldApp.Hold(workspace);
        app.Send("lock");
        app.WaitFor("^locked");

        using var bridge = McpBridge.Start(workspace);
        Assert.True(McpBridge.IsError(bridge.Call("list_entry_names")));
        Assert.True(McpBridge.IsError(bridge.Call("request_credential", Request())));
        Assert.Equal(0, bridge.Close());

        var lines = Workspace.Lines(workspace.BridgeLog);
        Assert.Equal(2, lines.Count);
        Assert.All(lines, line => Assert.Equal("denied", line.GetProperty("decision").GetString()));
        Assert.All(lines, line => Assert.False(line.TryGetProperty("session", out _)));
        Assert.Empty(Workspace.Lines(workspace.OwnerLog));
    }

    [Fact]
    public void Unlocking_again_starts_a_new_session_and_a_killed_app_leaves_nothing_holding_the_vault()
    {
        using var workspace = new Workspace();
        workspace.Add(_entry, _secret);
        using (var app = HeldApp.Hold(workspace))
        {
            var first = app.Session;
            app.Send("lock");
            app.WaitFor("^locked");
            app.Unlock();
            Assert.NotEqual(first, app.Session);

            using (var bridge = McpBridge.Start(workspace))
            {
                Assert.False(McpBridge.IsError(bridge.Call("list_entry_names")));
                Assert.Equal(0, bridge.Close());
            }

            Assert.Equal(app.Session, Workspace.Lines(workspace.OwnerLog)[^1].GetProperty("session").GetString());
            app.Kill();
        }

        var environment = workspace.Environment;
        environment["KEYPASTE_DRIVER_PASSWORD"] = Workspace.Master;
        using var after = RunningProcess.Start(Built.AppDriver, environment, "open", workspace.Vault);
        Assert.Equal(0, after.WaitForExit());
    }

    private static JsonObject Request() => new() { ["entry"] = _entry, ["field"] = "password", ["reason"] = "ci session probe" };
}
