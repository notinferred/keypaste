using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Xunit;

namespace Keypaste.Processes.Tests;

/// <summary>
/// The app asks in its own prompt window (4.4), across real processes: a built keypaste mcp's request raises the prompt
/// tests/Keypaste.AppDriver draws on a headless display and clicks, and a terminal's keypaste set or rm is asked about there (D-0464).
/// </summary>
/// <remarks>
/// Deny, closing, a lock, the timeout, arming, focus and the prompt's words are App.Tests' <c>DesktopApprovalTests</c> and
/// <c>TerminalEditWindowTests</c>; here is what only a separate client, bridge or terminal process can do to a prompt.
/// </remarks>
public sealed class DesktopApprovalTests
{
    private const string _entry = "env/ci/DEPLOY_KEY";
    private const string _label = "ci-desktop-probe";
    private const string _secret = "SENTINEL-DESKTOP-PASSWORD-91a4e2";

    [Fact]
    public void Allow_once_in_the_apps_window_releases_the_field_audited_as_a_prompted_grant_of_its_session_and_label_only()
    {
        using var workspace = Seeded();
        using var app = HeldApp.Hold(workspace);
        using var bridge = McpBridge.Start(workspace, label: _label);

        var asked = bridge.Send("request_credential", Request("password"));
        Assert.Contains($"label={_label} entry={_entry} field=password for=once always=none", app.WaitFor("^prompt client"), StringComparison.Ordinal);
        app.Send("once");
        var reply = bridge.Reply(asked);
        app.WaitFor("^prompt withdrawn");

        Assert.False(McpBridge.IsError(reply));
        Assert.Contains(_secret, reply.ToString(), StringComparison.Ordinal);
        var line = Workspace.Lines(workspace.OwnerLog)[^1];
        Assert.Equal(
            ("request_credential", "granted", "prompt", app.Session, _label),
            (line.GetProperty("tool").GetString(), line.GetProperty("decision").GetString(), line.GetProperty("method").GetString(),
             line.GetProperty("session").GetString(), line.GetProperty("client").GetProperty("label").GetString()));
        Assert.DoesNotContain(_secret, Workspace.Text(workspace.OwnerLog), StringComparison.Ordinal);
        Assert.DoesNotContain(app.Driver.Output, l => l.Contains(_secret, StringComparison.Ordinal));
    }

    public static TheoryData<string> GivingUp => ["cancelled", "input closed", "bridge gone"];

    [Theory]
    [MemberData(nameof(GivingUp))]
    public void A_client_giving_up_takes_the_prompt_down_and_leaves_one_cancelled_line(string how)
    {
        using var workspace = Seeded();
        using var app = HeldApp.Hold(workspace);
        var before = Workspace.Count(workspace.OwnerLog);
        using (var bridge = McpBridge.Start(workspace, label: _label))
        {
            var asked = bridge.Send("request_credential", Request("password"));
            app.WaitFor("^prompt client");
            switch (how)
            {
                case "cancelled":
                    bridge.Cancel(asked);
                    break;
                case "input closed":
                    bridge.Process.CloseInput();
                    break;
                default:
                    bridge.Process.Kill();
                    break;
            }

            app.WaitFor("^prompt withdrawn");
            Assert.DoesNotContain(bridge.Process.Output, line => line.Contains(_secret, StringComparison.Ordinal));
        }

        var line = Assert.Single(WaitForLines(workspace, before));
        Assert.Equal(
            ("request_credential", "denied", "cancelled", app.Session, _label),
            (line.GetProperty("tool").GetString(), line.GetProperty("decision").GetString(), line.GetProperty("method").GetString(),
             line.GetProperty("session").GetString(), line.GetProperty("client").GetProperty("label").GetString()));
    }

    [Fact]
    public void A_terminals_edit_is_asked_about_in_the_apps_window_saved_once_allowed_and_a_denied_one_changes_nothing()
    {
        const string edited = "SENTINEL-TERMINAL-EDIT-6f20b8";
        using var workspace = Seeded();
        workspace.Add("env/ci/STAGING", "staging-value");
        using var app = HeldApp.Hold(workspace);

        var before = SHA256.HashData(File.ReadAllBytes(workspace.Vault));
        using (var set = RunningProcess.Start(Built.Keypaste, workspace.Environment, "set", _entry, "--vault", workspace.Vault))
        {
            set.Send(edited);
            set.CloseInput();
            Assert.Contains(
                $"command=keypaste set item={_entry} asked=asked from a terminal; any program you run could have typed it",
                app.WaitFor("^edit-prompt command="),
                StringComparison.Ordinal);
            Assert.DoesNotContain(app.Driver.Output, line => line.Contains(edited, StringComparison.Ordinal));
            app.Send("allow");
            Assert.Equal(0, set.WaitForExit());
        }

        app.WaitFor("^edit-prompt withdrawn");
        Assert.NotEqual(before, SHA256.HashData(File.ReadAllBytes(workspace.Vault)));
        var line = Workspace.Lines(workspace.OwnerLog)[^1];
        Assert.Equal(("terminal_edit", "granted", "prompt"), (line.GetProperty("tool").GetString(), line.GetProperty("decision").GetString(), line.GetProperty("method").GetString()));
        Assert.DoesNotContain(edited, Workspace.Text(workspace.OwnerLog), StringComparison.Ordinal);

        before = SHA256.HashData(File.ReadAllBytes(workspace.Vault));
        using (var rm = RunningProcess.Start(Built.Keypaste, workspace.Environment, "rm", "env/ci/STAGING", "--yes", "--vault", workspace.Vault))
        {
            rm.CloseInput();
            app.WaitFor("^edit-prompt command=", 2);
            app.Send("deny");
            Assert.NotEqual(0, rm.WaitForExit());
            Assert.Contains(rm.Error, l => l.Contains("not saved: a person refused this change", StringComparison.Ordinal));
        }

        app.WaitFor("^edit-prompt withdrawn", 2);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(workspace.Vault)));
    }

    private static Workspace Seeded()
    {
        var workspace = new Workspace();
        workspace.Add(_entry, _secret);
        return workspace;
    }

    // The owner records an abandoned request once the bridge's hang-up reaches it, which may be just after the prompt comes down.
    private static IReadOnlyList<System.Text.Json.JsonElement> WaitForLines(Workspace workspace, int before)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (Workspace.Count(workspace.OwnerLog) <= before && deadline.Elapsed < RunningProcess.Wait)
        {
            Thread.Sleep(50);
        }

        Thread.Sleep(500);
        return Workspace.Lines(workspace.OwnerLog, before);
    }

    private static JsonObject Request(string field) => new() { ["entry"] = _entry, ["field"] = field, ["reason"] = "ci desktop approval probe" };
}
