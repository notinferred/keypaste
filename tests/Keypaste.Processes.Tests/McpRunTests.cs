using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Keypaste.Processes.Tests;

/// <summary>
/// The built run tool end to end, across real processes (D-0358, D-0359): keypaste approve holds the vault and asks a person, and
/// keypaste mcp --allow-run starts the command with the approved value in its environment and returns its output scrubbed. An agent's
/// commands are off until keypaste clients turns them on through the owner, which seals the change, and every run is then asked.
/// </summary>
/// <remarks>The run's rules and its scrubbing are Mcp.Tests' and Core.Tests' run tool tests, in one process.</remarks>
public sealed class McpRunTests
{
    private const string _core = "SENTINEL-RUN-GATE-7c31f9";

    // Every character the common dumps escape differently: a quote, an apostrophe, a backslash, a dollar, a line break, a non-ASCII letter and a percent-escape.
    private const string _secret = _core + "\"q'x\\y$z\né%40end";

    [Fact]
    public void A_run_is_refused_while_the_agents_commands_are_off_and_once_the_owner_seals_them_on_each_is_asked_and_scrubbed()
    {
        using var workspace = new Workspace();
        var project = Path.Combine(Path.GetDirectoryName(workspace.Vault)!, "project");
        Directory.CreateDirectory(project);
        EnvSet(workspace, $"DEPLOY_KEY={_secret}");
        EnvSet(workspace, "DATABASE_URL=pre-{S:DEPLOY_KEY}-post");
        var pipe = $"keypaste-run-e2e-{Environment.ProcessId}-{Guid.NewGuid():n}"[..40];

        using var approve = RunningProcess.Start(Built.Keypaste, workspace.Environment, "approve", "--vault", workspace.Vault, "--approver", pipe, "--approval-timeout", "30");
        foreach (var line in new[] { Workspace.Master, "o", "o", "o", "d" })
        {
            approve.Send(line);
        }

        var session = System.Text.RegularExpressions.Regex.Match(approve.WaitForError("listening on"), " for unlock ([^,]+),").Groups[1].Value;
        var offMarker = Path.Combine(project, "run-off-ran");
        var deniedMarker = Path.Combine(project, "denied-run-ran");

        using (var bridge = Bridge(workspace, pipe))
        {
            Assert.True(McpBridge.IsError(bridge.Call("run", Run(project, ["sh", "-c", $"printf ran > \"{offMarker}\""]))));
            Assert.Equal(0, bridge.Close());
        }

        Assert.False(File.Exists(offMarker), "a run started while the agent's commands are off");
        Assert.Equal(0, Prompts(approve));

        using (var clients = RunningProcess.Start(Built.Keypaste, workspace.Environment, "clients", "ci-probe", "--run", "on", "--vault", workspace.Vault, "--approver", pipe))
        {
            clients.CloseInput();
            Assert.Equal(0, clients.WaitForExit());
            Assert.Contains(clients.Error, line => line.Contains("sealed by the process holding the vault", StringComparison.Ordinal));
        }

        JsonElement printed, again, spliced, denied;
        using (var bridge = Bridge(workspace, pipe))
        {
            string[] prints = ["sh", "-c", "printf %s \"$DEPLOY_KEY\"; printf %s \"$DEPLOY_KEY\" >&2; exit 3"];
            printed = Result(bridge.Call("run", Run(project, prints)));
            again = Result(bridge.Call("run", Run(project, prints)));
            spliced = Result(bridge.Call("run", Run(project, ["sh", "-c", "v=${DATABASE_URL#pre-}; v=${v%-post}; printf %s \"$v\"; printf %s \"$v\" >&2"], keys: ["DATABASE_URL"])));
            denied = bridge.Call("run", Run(project, ["sh", "-c", $"printf ran > \"{deniedMarker}\""]));
            Assert.DoesNotContain(_core, string.Join('\n', bridge.Process.Output.Concat(bridge.Process.Error)), StringComparison.Ordinal);
            Assert.Equal(0, bridge.Close());
        }

        Assert.Equal((3, "[keypaste:DEPLOY_KEY]", "[keypaste:DEPLOY_KEY]"), Streams(printed));
        Assert.Equal(3, again.GetProperty("exit_code").GetInt32());
        Assert.Equal(4, Prompts(approve));
        Assert.Equal(["DATABASE_URL"], spliced.GetProperty("injected").EnumerateArray().Select(key => key.GetString()));
        Assert.Equal(("[keypaste:DATABASE_URL]", "[keypaste:DATABASE_URL]"), (spliced.GetProperty("stdout").GetString(), spliced.GetProperty("stderr").GetString()));
        Assert.True(McpBridge.IsError(denied));
        Assert.False(File.Exists(deniedMarker), "the denied run started its command");
        Assert.Contains(approve.Error, line => line.Contains("ci run probe", StringComparison.Ordinal));

        Assert.DoesNotContain(_core, Workspace.Text(workspace.BridgeLog), StringComparison.Ordinal);
        Assert.DoesNotContain(_core, Workspace.Text(workspace.OwnerLog), StringComparison.Ordinal);
        var lines = Workspace.Lines(workspace.OwnerLog);
        Assert.Equal(6, lines.Count);
        Assert.Equal(("run", "denied", "run-off", session), (lines[0].GetProperty("tool").GetString(), lines[0].GetProperty("decision").GetString(), lines[0].GetProperty("method").GetString(), lines[0].GetProperty("session").GetString()));
        Assert.Equal(("access_change", "granted", "rules-changed", "ci-probe"), (lines[1].GetProperty("tool").GetString(), lines[1].GetProperty("decision").GetString(), lines[1].GetProperty("method").GetString(), lines[1].GetProperty("client").GetProperty("label").GetString()));
        foreach (var run in lines.Skip(2))
        {
            Assert.Equal(("run", session, 16), (run.GetProperty("tool").GetString(), run.GetProperty("session").GetString(), run.GetProperty("vault").GetString()!.Length));
        }

        foreach (var allowed in lines.Skip(2).Take(3))
        {
            Assert.Equal(("granted", "prompt", "env/ci/.env"), (allowed.GetProperty("decision").GetString(), allowed.GetProperty("method").GetString(), Assert.Single(allowed.GetProperty("entries").EnumerateArray()).GetString()));
        }

        Assert.StartsWith("sh -c", lines[2].GetProperty("command").GetString(), StringComparison.Ordinal);
        Assert.Equal(("denied", "prompt"), (lines[5].GetProperty("decision").GetString(), lines[5].GetProperty("method").GetString()));
    }

    private static void EnvSet(Workspace workspace, string assignment)
    {
        using var set = RunningProcess.Start(Built.Keypaste, workspace.Environment, "env", "set", "ci", assignment, "--vault", workspace.Vault);
        set.Send(Workspace.Master);
        set.CloseInput();
        Assert.Equal(0, set.WaitForExit());
    }

    private static McpBridge Bridge(Workspace workspace, string pipe) =>
        McpBridge.Start(workspace, exposure: null, label: "ci-probe", vault: null, "--approver", pipe, "--allow-run");

    private static JsonObject Run(string directory, string[] command, string[]? keys = null)
    {
        var arguments = new JsonObject
        {
            ["command"] = new JsonArray([.. command.Select(part => (JsonNode)part)]),
            ["directory"] = directory,
            ["project"] = "ci",
            ["reason"] = "ci run probe",
        };
        if (keys is not null)
        {
            arguments["keys"] = new JsonArray([.. keys.Select(key => (JsonNode)key)]);
        }

        return arguments;
    }

    private static JsonElement Result(JsonElement reply)
    {
        Assert.False(McpBridge.IsError(reply), reply.ToString());
        return reply.GetProperty("result").GetProperty("structuredContent");
    }

    private static (int, string?, string?) Streams(JsonElement result) =>
        (result.GetProperty("exit_code").GetInt32(), result.GetProperty("stdout").GetString(), result.GetProperty("stderr").GetString());

    private static int Prompts(RunningProcess approve) => approve.Error.Count(line => line.Contains("tool         keypaste.run", StringComparison.Ordinal));
}
