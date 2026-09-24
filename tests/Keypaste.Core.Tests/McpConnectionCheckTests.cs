using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Keypaste.Core.Clients;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// What the connection check sends a bridge, and what it keeps of the answers.
/// </summary>
/// <remarks>
/// A scripted bridge over real pipes. That the check speaks what the real server accepts is
/// <c>ConnectionCheckTests</c> in the bridge's own tests; these cover the order, the timeouts and
/// what happens to a released value.
/// </remarks>
public sealed class McpConnectionCheckTests
{
    private const string _secret = "s3cr3t-value-the-check-must-never-keep";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task It_introduces_itself_before_listing_and_asks_for_the_chosen_entry_with_its_fixed_reason()
    {
        await using var bridge = new ScriptedBridge();
        await using var check = bridge.Check();

        var listing = await check.ListAsync(Token);
        Assert.Null(listing.Problem);
        Assert.Equal([new McpListedEntry("k1_first", "env/ci/DEPLOY_KEY"), new McpListedEntry("k1_second", "env/ci/OTHER")], listing.Entries);

        await check.RequestAsync(listing.Entries[1], Token);

        Assert.Equal(["initialize", "notifications/initialized", "tools/call", "tools/call"], bridge.Methods);
        var hello = bridge.Received[0]["params"]!;
        Assert.Equal(McpConnectionCheck.ClientName, (string?)hello["clientInfo"]!["name"]);

        var request = bridge.Received[3]["params"]!;
        Assert.Equal("request_credential", (string?)request["name"]);
        Assert.Equal("k1_second", (string?)request["arguments"]!["entry"]);
        Assert.Equal("password", (string?)request["arguments"]!["field"]);
        Assert.Equal(McpConnectionCheck.Reason, (string?)request["arguments"]!["reason"]);
        Assert.Equal(60, (int?)request["arguments"]!["ttl_seconds"]);
    }

    [Fact]
    public async Task A_grant_is_reported_without_its_value()
    {
        await using var bridge = new ScriptedBridge();
        await using var check = bridge.Check();

        var listing = await check.ListAsync(Token);
        var answer = await check.RequestAsync(listing.Entries[0], Token);

        Assert.Equal(McpCheckOutcome.Granted, answer.Outcome);
        Assert.DoesNotContain(_secret, answer.ToString(), StringComparison.Ordinal);
        Assert.Contains(_secret, bridge.Sent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_denial_is_reported_in_the_bridges_words()
    {
        await using var bridge = new ScriptedBridge { Deny = true };
        await using var check = bridge.Check();

        var answer = await check.RequestAsync((await check.ListAsync(Token)).Entries[0], Token);

        Assert.Equal(McpCheckOutcome.Denied, answer.Outcome);
        Assert.Equal("keypaste: DENIED. A person refused this request.", answer.Said);
    }

    [Fact]
    public async Task A_refused_listing_says_why_and_lists_nothing()
    {
        await using var bridge = new ScriptedBridge { RefuseListing = true };
        await using var check = bridge.Check();

        var listing = await check.ListAsync(Token);

        Assert.Empty(listing.Entries);
        Assert.Equal("keypaste: nothing holds this vault unlocked.", listing.Problem);
    }

    [Fact]
    public async Task A_bridge_that_exits_before_answering_is_a_failure_not_a_denial()
    {
        await using var bridge = new ScriptedBridge { ExitOnRequest = true };
        await using var check = bridge.Check();

        var answer = await check.RequestAsync((await check.ListAsync(Token)).Entries[0], Token);

        Assert.Equal(McpCheckOutcome.Failed, answer.Outcome);
        Assert.Equal("the bridge exited without answering", answer.Said);
    }

    [Fact]
    public async Task A_request_nobody_answers_fails_at_its_deadline()
    {
        var clock = new ManualClock();
        await using var bridge = new ScriptedBridge { SilentOnRequest = true };
        await using var check = bridge.Check(clock);

        var listing = await check.ListAsync(Token);
        var pending = check.RequestAsync(listing.Entries[0], Token);
        await bridge.RequestArrived.Task.WaitAsync(Token);

        clock.Advance(McpConnectionCheck.RequestTimeout - TimeSpan.FromSeconds(1));
        Assert.False(pending.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(1));
        var answer = await pending.WaitAsync(Token);

        Assert.Equal(McpCheckOutcome.Failed, answer.Outcome);
        Assert.Equal("no answer within 60 seconds", answer.Said);
    }

    [Fact]
    public async Task Notifications_before_the_answer_are_passed_over()
    {
        await using var bridge = new ScriptedBridge { NotifyFirst = true };
        await using var check = bridge.Check();

        var listing = await check.ListAsync(Token);

        Assert.Equal(2, listing.Entries.Count);
    }

    /// <summary>A bridge that answers from a script over a pair of anonymous pipes.</summary>
    private sealed class ScriptedBridge : IAsyncDisposable
    {
        private readonly AnonymousPipeServerStream _toBridge = new(PipeDirection.Out);
        private readonly AnonymousPipeServerStream _fromBridge = new(PipeDirection.In);
        private readonly AnonymousPipeClientStream _bridgeReads;
        private readonly AnonymousPipeClientStream _bridgeWrites;
        private readonly StringBuilder _sent = new();
        private readonly Task _serving;

        internal ScriptedBridge()
        {
            _bridgeReads = new AnonymousPipeClientStream(PipeDirection.In, _toBridge.ClientSafePipeHandle);
            _bridgeWrites = new AnonymousPipeClientStream(PipeDirection.Out, _fromBridge.ClientSafePipeHandle);
            _serving = Task.Run(ServeAsync);
        }

        internal bool Deny { get; init; }

        internal bool RefuseListing { get; init; }

        internal bool ExitOnRequest { get; init; }

        internal bool SilentOnRequest { get; init; }

        internal bool NotifyFirst { get; init; }

        internal List<JsonNode> Received { get; } = [];

        internal List<string?> Methods => [.. Received.Select(message => (string?)message["method"])];

        internal string Sent
        {
            get
            {
                lock (_sent)
                {
                    return _sent.ToString();
                }
            }
        }

        internal TaskCompletionSource RequestArrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal McpConnectionCheck Check(TimeProvider? clock = null) => new(_toBridge, _fromBridge, clock);

        private async Task ServeAsync()
        {
            using var reader = new StreamReader(_bridgeReads, new UTF8Encoding(false));
            await using var writer = new StreamWriter(_bridgeWrites, new UTF8Encoding(false)) { AutoFlush = true };

            while (await reader.ReadLineAsync() is { } line)
            {
                var message = JsonNode.Parse(line)!;
                Received.Add(message);

                if (message["id"] is not { } id)
                {
                    continue;
                }

                var tool = (string?)message["params"]?["name"];
                if (tool == "request_credential")
                {
                    RequestArrived.TrySetResult();
                    if (ExitOnRequest)
                    {
                        return;
                    }

                    if (SilentOnRequest)
                    {
                        continue;
                    }
                }

                if (NotifyFirst)
                {
                    await Send(writer, new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/message", ["params"] = new JsonObject() });
                }

                await Send(writer, new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id.DeepClone(),
                    ["result"] = (string?)message["method"] == "initialize" ? new JsonObject() : Result(tool),
                });
            }
        }

        private JsonObject Result(string? tool) => tool switch
        {
            "list_entry_names" when RefuseListing => Refusal("keypaste: nothing holds this vault unlocked.\nStart it."),
            "list_entry_names" => new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "names" }),
                ["structuredContent"] = new JsonObject
                {
                    ["vault"] = "open",
                    ["entries"] = new JsonArray(
                        new JsonObject { ["handle"] = "k1_first", ["group"] = "env/ci", ["name"] = "DEPLOY_KEY", ["altered"] = false },
                        new JsonObject { ["handle"] = "k1_second", ["group"] = "env/ci", ["name"] = "OTHER", ["altered"] = false }),
                },
            },
            _ when Deny => Refusal("keypaste: DENIED. A person refused this request.\nDo not retry."),
            _ => new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "keypaste: APPROVED.\n" + _secret }),
                ["structuredContent"] = new JsonObject { ["field"] = "password", ["value"] = _secret, ["expires_in_seconds"] = 60 },
            },
        };

        private static JsonObject Refusal(string text) => new()
        {
            ["isError"] = true,
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        };

        private async Task Send(StreamWriter writer, JsonObject message)
        {
            var text = message.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
            lock (_sent)
            {
                _sent.AppendLine(text);
            }

            await writer.WriteLineAsync(text);
        }

        public async ValueTask DisposeAsync()
        {
            _toBridge.Dispose();
            await _serving.WaitAsync(TimeSpan.FromSeconds(10));
            _bridgeWrites.Dispose();
            _bridgeReads.Dispose();
            _fromBridge.Dispose();
        }
    }
}
