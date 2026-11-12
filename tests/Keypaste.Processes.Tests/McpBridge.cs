using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Keypaste.Processes.Tests;

/// <summary>The built <c>keypaste mcp</c>, spoken to over its standard input and output as an MCP client does.</summary>
internal sealed class McpBridge : IDisposable
{
    private readonly RunningProcess _bridge;
    private int _next = 1;

    private McpBridge(RunningProcess bridge) => _bridge = bridge;

    internal RunningProcess Process => _bridge;

    /// <summary>Starts the bridge for <paramref name="vault"/>, else <paramref name="workspace"/>'s vault, exposing <paramref name="exposure"/>, and initializes it.</summary>
    internal static McpBridge Start(Workspace workspace, string exposure = "env/**", string label = "ci-probe", string? vault = null, params string[] options)
    {
        var bridge = new McpBridge(RunningProcess.Start(
            Built.Keypaste,
            workspace.Environment,
            ["mcp", "--vault", vault ?? workspace.Vault, "--expose", exposure, "--audit-log", workspace.BridgeLog, "--client-label", label, .. options]));
        bridge.Ask("initialize", new JsonObject
        {
            ["protocolVersion"] = "2025-11-25",
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "ci-probe", ["version"] = "1.0.0" },
        });
        bridge._bridge.Send("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
        return bridge;
    }

    /// <summary>Calls a tool and waits for its reply.</summary>
    internal JsonElement Call(string tool, JsonObject? arguments = null) => Reply(Send(tool, arguments));

    /// <summary>Calls a tool without waiting, for a reply a person or a lock decides.</summary>
    /// <returns>The call's id, for <see cref="Reply"/>.</returns>
    internal int Send(string tool, JsonObject? arguments = null) =>
        Request("tools/call", new JsonObject { ["name"] = tool, ["arguments"] = arguments ?? new JsonObject() });

    /// <summary>Waits for the reply to the call <paramref name="id"/>.</summary>
    internal JsonElement Reply(int id)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < RunningProcess.Wait)
        {
            foreach (var line in _bridge.Output)
            {
                if (line.StartsWith('{') && JsonDocument.Parse(line).RootElement is { } reply
                    && reply.TryGetProperty("id", out var answered) && answered.GetInt32() == id)
                {
                    return reply.Clone();
                }
            }

            Thread.Sleep(50);
        }

        Assert.Fail($"keypaste mcp gave no reply to {id}:\n{string.Join('\n', _bridge.Output)}\n{string.Join('\n', _bridge.Error)}");
        return default;
    }

    /// <summary>Tells the bridge the client gave up on the call <paramref name="id"/>, as MCP's cancellation does.</summary>
    internal void Cancel(int id) =>
        _bridge.Send(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "notifications/cancelled",
            ["params"] = new JsonObject { ["requestId"] = id, ["reason"] = "the client gave up" },
        }.ToJsonString());

    /// <summary>Whether a tool call's reply is an error, which no released value is part of.</summary>
    internal static bool IsError(JsonElement reply) => reply.GetProperty("result").GetProperty("isError").GetBoolean();

    /// <summary>Closes the client's side and waits for the bridge to exit.</summary>
    internal int Close()
    {
        _bridge.CloseInput();
        return _bridge.WaitForExit();
    }

    public void Dispose() => _bridge.Dispose();

    private JsonElement Ask(string method, JsonObject parameters) => Reply(Request(method, parameters));

    private int Request(string method, JsonObject parameters)
    {
        var id = _next++;
        _bridge.Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters }.ToJsonString());
        return id;
    }
}
