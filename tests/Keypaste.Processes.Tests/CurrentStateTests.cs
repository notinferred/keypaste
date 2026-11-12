using System.Text.Json.Nodes;
using Xunit;

namespace Keypaste.Processes.Tests;

/// <summary>
/// The owning session answers from the vault as last saved (U.3) when the save that changed it is another process's: the built keypaste
/// under another home saves the vault the app holds, the built bridge's next request is refused as vault-changed, and once the app merges
/// that save with its own edit the same session answers from the merged copy and asks again.
/// </summary>
/// <remarks>Edits, moves, deletes, regroups, access changes, merges and reloads are App.Tests' <c>CurrentStateTests</c>, over a real pipe.</remarks>
public sealed class CurrentStateTests
{
    private const string _entry = "env/ci/DEPLOY_KEY";
    private const string _first = "SENTINEL-CURRENT-V1-3b9d";
    private const string _external = "SENTINEL-CURRENT-V3-3b9d";

    [Fact]
    public void Another_programs_save_is_refused_until_the_app_merges_it_and_the_same_session_then_answers_from_the_merge()
    {
        using var workspace = new Workspace();
        workspace.Add(_entry, _first);
        workspace.Keypaste("add", Workspace.Master, "keep-value", "personal/KEEP", "--vault", workspace.Vault);
        using var app = HeldApp.Hold(workspace, newPassword: "keep-edited", "--approving-prompt");
        using var bridge = McpBridge.Start(workspace);
        Assert.Contains(_first, bridge.Call("request_credential", Request()).ToString(), StringComparison.Ordinal);

        // Another home holds no claim on the vault, so its keypaste saves while the app holds it (T-29).
        var other = Path.Combine(Path.GetDirectoryName(workspace.Vault)!, "other-home");
        Directory.CreateDirectory(other);
        using (var set = RunningProcess.Start(Built.Keypaste, new Dictionary<string, string> { ["KEYPASTE_HOME"] = other }, "set", _entry, "--vault", workspace.Vault))
        {
            set.Send(Workspace.Master);
            set.Send(_external);
            set.CloseInput();
            Assert.Equal(0, set.WaitForExit());
        }

        var refused = bridge.Call("request_credential", Request());
        Assert.True(McpBridge.IsError(refused));
        var line = Workspace.Lines(workspace.OwnerLog)[^1];
        Assert.Equal(("denied", "vault-changed", app.Session), (line.GetProperty("decision").GetString(), line.GetProperty("method").GetString(), line.GetProperty("session").GetString()));
        Assert.True(McpBridge.IsError(bridge.Call("list_entry_names")));

        app.Act("edit personal/KEEP");
        app.WaitFor("^saved personal/KEEP");
        app.WaitFor("^merged saved: Another program saved vault.kdbx, and keypaste merged it with your changes and saved");
        var merges = Workspace.Lines(workspace.OwnerLog).Where(l => l.GetProperty("tool").GetString() == "vault_merge").ToList();
        Assert.Equal(("merged", app.Session), (Assert.Single(merges).GetProperty("method").GetString(), merges[0].GetProperty("session").GetString()));

        var asked = app.Driver.Output.Count(l => l.StartsWith("approved", StringComparison.Ordinal));
        var released = bridge.Call("request_credential", Request());
        Assert.Contains(_external, released.ToString(), StringComparison.Ordinal);
        Assert.Equal(app.Session, Workspace.Lines(workspace.OwnerLog)[^1].GetProperty("session").GetString());
        Assert.Equal(asked + 1, app.Driver.Output.Count(l => l.StartsWith("approved", StringComparison.Ordinal)));
    }

    private static JsonObject Request() => new() { ["entry"] = _entry, ["field"] = "password", ["reason"] = "ci current-state probe" };
}
