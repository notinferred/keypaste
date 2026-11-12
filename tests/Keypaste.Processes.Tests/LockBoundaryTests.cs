using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace Keypaste.Processes.Tests;

/// <summary>
/// A lock is one transition across real processes (U.2): a request a real keypaste mcp has waiting at the owner is answered as a vault-locked
/// denial of the session it reached, whether the app locks, quits or keypaste approve is stopped.
/// </summary>
/// <remarks>Idle and an expired sleep take the same transition over a real pipe with a manual clock, in App.Tests' <c>LockBoundaryTests</c>.</remarks>
public sealed partial class LockBoundaryTests
{
    private const string _entry = "env/ci/DEPLOY_KEY";
    private const string _secret = "SENTINEL-LOCK-PASSWORD-7c2d95";

    [Fact]
    public void A_manual_lock_denies_the_waiting_request_and_the_next_unlock_asks_it_again()
    {
        using var workspace = Seeded();
        using var app = HeldApp.Hold(workspace, "--held-prompt");
        var first = app.Session;

        using (var bridge = McpBridge.Start(workspace))
        {
            var waiting = bridge.Send("request_credential", Request());
            app.WaitFor("^asking");
            app.Send("lock");
            app.WaitFor("^locked");

            AssertDeniedAsLocked(workspace, bridge, bridge.Reply(waiting), first);
            app.WaitFor("^withdrawn");
        }

        app.Unlock();
        Assert.NotEqual(first, app.Session);
        using (var bridge = McpBridge.Start(workspace))
        {
            bridge.Send("request_credential", Request());
            app.WaitFor("^asking", 2);
        }
    }

    [Fact]
    public void Quitting_the_app_denies_the_waiting_request()
    {
        using var workspace = Seeded();
        using var app = HeldApp.Hold(workspace, "--held-prompt");
        using var bridge = McpBridge.Start(workspace);

        var waiting = bridge.Send("request_credential", Request());
        app.WaitFor("^asking");
        app.Quit();
        app.WaitFor("^shut down");

        AssertDeniedAsLocked(workspace, bridge, bridge.Reply(waiting), app.Session);
    }

    [Fact]
    public void Stopping_keypaste_approve_denies_the_request_waiting_at_its_prompt()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows has no signal to stop keypaste approve with.");

        using var workspace = Seeded();
        using var approve = RunningProcess.Start(Built.Keypaste, workspace.Environment, "approve", "--vault", workspace.Vault);
        approve.Send(Workspace.Master);
        var session = SessionRegex().Match(approve.WaitForError("listening on")).Groups[1].Value;

        using var bridge = McpBridge.Start(workspace);
        var waiting = bridge.Send("request_credential", Request());
        approve.WaitForError("an agent is asking for a credential");
        using (var term = RunningProcess.Start("/bin/kill", new Dictionary<string, string>(), "-TERM", approve.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)))
        {
            Assert.Equal(0, term.WaitForExit());
        }

        Assert.Equal(0, approve.WaitForExit());
        Assert.Contains(approve.Error, line => line.Contains("withdrawn before you answered", StringComparison.Ordinal));
        Assert.Contains(approve.Error, line => line.Contains("keypaste approve has stopped. The vault is locked.", StringComparison.Ordinal));
        AssertDeniedAsLocked(workspace, bridge, bridge.Reply(waiting), session);
    }

    private static Workspace Seeded()
    {
        var workspace = new Workspace();
        workspace.Add(_entry, _secret);
        return workspace;
    }

    private static void AssertDeniedAsLocked(Workspace workspace, McpBridge bridge, JsonElement reply, string session)
    {
        Assert.True(McpBridge.IsError(reply));
        Assert.DoesNotContain(_secret, string.Join('\n', bridge.Process.Output), StringComparison.Ordinal);
        var line = Workspace.Lines(workspace.OwnerLog)[^1];
        Assert.Equal(
            ("request_credential", "denied", "vault-locked", session),
            (line.GetProperty("tool").GetString(), line.GetProperty("decision").GetString(), line.GetProperty("method").GetString(), line.GetProperty("session").GetString()));
    }

    private static JsonObject Request() => new() { ["entry"] = _entry, ["field"] = "password", ["reason"] = "ci lock probe" };

    [GeneratedRegex(" for unlock ([0-9a-f]+),")]
    private static partial Regex SessionRegex();
}
