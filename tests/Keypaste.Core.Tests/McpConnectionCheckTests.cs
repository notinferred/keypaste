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
/// A scripted bridge over in-memory pipes, and a silent one over real anonymous pipes for the
/// deadlines and an exit. That the check speaks what the real server accepts is
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

        var answer = await check.RequestAsync(Listed(await check.ListAsync(Token))[0], Token);

        Assert.Equal(McpCheckOutcome.Granted, answer.Outcome);
        Assert.DoesNotContain(_secret, answer.ToString(), StringComparison.Ordinal);
        Assert.Contains(_secret, bridge.Sent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_denial_is_reported_in_the_bridges_words()
    {
        await using var bridge = new ScriptedBridge { Deny = true };
        await using var check = bridge.Check();

        var answer = await check.RequestAsync(Listed(await check.ListAsync(Token))[0], Token);

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

        var answer = await check.RequestAsync(Listed(await check.ListAsync(Token))[0], Token);

        Assert.Equal(McpCheckOutcome.Failed, answer.Outcome);
        Assert.Equal("the bridge exited without answering", answer.Said);
    }

    [Fact]
    public async Task A_listing_nobody_answers_fails_at_the_step_timeout_and_a_closed_pipe_is_an_exit()
    {
        var clock = new ManualClock();
        using var bridge = new SilentBridge(clock, McpConnectionCheck.StepTimeout);
        await using var check = new McpConnectionCheck(bridge.ToBridge, bridge.FromBridge, clock);

        var listing = await bridge.Watch(check.ListAsync(Token));

        Assert.Equal("initialize", bridge.Method);
        Assert.True(bridge.PendingAtTheLastSecond);
        Assert.Empty(listing.Entries);
        Assert.Equal("the bridge did not answer within 10 seconds", listing.Problem);

        bridge.Exit();

        Assert.Equal("the bridge exited without answering", (await check.ListAsync(Token)).Problem);
    }

    [Fact]
    public async Task A_request_nobody_answers_fails_at_its_deadline()
    {
        var clock = new ManualClock();
        using var bridge = new SilentBridge(clock, McpConnectionCheck.RequestTimeout);
        await using var check = new McpConnectionCheck(bridge.ToBridge, bridge.FromBridge, clock);

        var answer = await bridge.Watch(check.RequestAsync(new McpListedEntry("k1_first", "env/ci/DEPLOY_KEY"), Token));

        Assert.Equal("tools/call", bridge.Method);
        Assert.True(bridge.PendingAtTheLastSecond);
        Assert.Equal(McpCheckOutcome.Failed, answer.Outcome);
        Assert.Equal("no answer within 60 seconds", answer.Said);
    }

    [Fact]
    public async Task Notifications_before_the_answer_are_passed_over()
    {
        await using var bridge = new ScriptedBridge { NotifyFirst = true };
        await using var check = bridge.Check();

        Assert.Equal(2, Listed(await check.ListAsync(Token)).Count);
    }

    [Fact]
    public async Task The_scripted_bridge_answers_within_the_checks_own_call()
    {
        await using var bridge = new ScriptedBridge();
        await using var check = bridge.Check();

        var listing = check.ListAsync(Token);

        Assert.True(listing.IsCompleted, "the scripted listing did not finish inside ListAsync");
        Assert.Equal(2, Listed(await listing).Count);
    }

    private static IReadOnlyList<McpListedEntry> Listed(McpCheckListing listing)
    {
        Assert.Null(listing.Problem);
        return listing.Entries;
    }

    /// <summary>A bridge that answers from a script, inside the write that reaches it.</summary>
    private sealed class ScriptedBridge : IAsyncDisposable
    {
        private readonly ScriptedBridgeChannels _channels = new();
        private readonly StringBuilder _sent = new();
        private readonly Task _serving;

        internal ScriptedBridge()
        {
            _serving = ServeAsync();
        }

        internal bool Deny { get; init; }

        internal bool RefuseListing { get; init; }

        internal bool ExitOnRequest { get; init; }

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

        internal McpConnectionCheck Check(TimeProvider? clock = null) => _channels.Check(clock);

        private async Task ServeAsync()
        {
            using var reader = new StreamReader(_channels.BridgeReads, new UTF8Encoding(false));
            await using var writer = new StreamWriter(_channels.BridgeWrites, new UTF8Encoding(false)) { AutoFlush = true };

            while (await reader.ReadLineAsync() is { } line)
            {
                var message = JsonNode.Parse(line)!;
                Received.Add(message);

                if (message["id"] is not { } id)
                {
                    continue;
                }

                var tool = (string?)message["params"]?["name"];
                if (tool == "request_credential" && ExitOnRequest)
                {
                    return;
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
            _channels.ToBridge.Dispose();
            await _serving.WaitAsync(TimeSpan.FromSeconds(10));
            _channels.Dispose();
        }
    }

    /// <summary>
    /// A bridge over real anonymous pipes that reads one request, closes its end and never answers. A
    /// dedicated thread reads it and moves the clock across the deadline, so no step waits for a
    /// thread-pool worker.
    /// </summary>
    private sealed class SilentBridge : IDisposable
    {
        private readonly AnonymousPipeServerStream _toBridge = new(PipeDirection.Out);
        private readonly AnonymousPipeServerStream _fromBridge = new(PipeDirection.In);
        private readonly ManualResetEventSlim _watched = new();
        private readonly ManualClock _clock;
        private readonly TimeSpan _deadline;
        private Task? _pending;

        internal SilentBridge(ManualClock clock, TimeSpan deadline)
        {
            _clock = clock;
            _deadline = deadline;
            new Thread(Answer) { IsBackground = true, Name = "silent bridge" }.Start();
        }

        internal Stream ToBridge => _toBridge;

        internal Stream FromBridge => _fromBridge;

        internal string? Method { get; private set; }

        internal bool PendingAtTheLastSecond { get; private set; }

        internal Task<T> Watch<T>(Task<T> pending)
        {
            _pending = pending;
            _watched.Set();
            return pending;
        }

        /// <summary>Closes the end the bridge would have answered on, as a bridge process that exits does.</summary>
        internal void Exit() => _fromBridge.DisposeLocalCopyOfClientHandle();

        public void Dispose()
        {
            _toBridge.Dispose();
            _fromBridge.Dispose();
            _watched.Dispose();
        }

        private void Answer()
        {
            try
            {
                if (ReadRequest() is not { } line || !_watched.Wait(TimeSpan.FromSeconds(30)))
                {
                    return;
                }

                Method = (string?)JsonNode.Parse(line)!["method"];
                _clock.Advance(_deadline - TimeSpan.FromSeconds(1));
                PendingAtTheLastSecond = !_pending!.IsCompleted;
                _clock.Advance(TimeSpan.FromSeconds(1));
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // The test ended before a request arrived.
            }
        }

        private string? ReadRequest()
        {
            using var reads = new AnonymousPipeClientStream(PipeDirection.In, _toBridge.ClientSafePipeHandle);
            using var reader = new StreamReader(reads, new UTF8Encoding(false));
            return reader.ReadLine();
        }
    }
}
