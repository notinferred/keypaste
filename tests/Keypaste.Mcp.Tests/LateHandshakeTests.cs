using System.Collections.Concurrent;
using System.Text.Json;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Tests;
using Keypaste.Mcp.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace Keypaste.Mcp.Tests;

/// <summary>
/// A call that overtook the handshake naming its client is put to the owner and logged under the
/// name the handshake gave, once the call has waited for it (F.61).
/// </summary>
/// <remarks>
/// F.5 made a call wait for the handshake but kept the identity it had read before waiting, so a
/// client that named itself while its call waited was put to the owner and logged as an unnamed
/// client. A real client cannot produce that ordering in process (<see cref="HandshakeGraceTests"/>);
/// a server whose client is unnamed the first time anybody asks and named every time after can, and
/// each tool is called on it as the server's dispatch would call it.
/// </remarks>
public sealed class LateHandshakeTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Dictionary<string, object?> Credential => new()
    {
        ["entry"] = "env/dev/STRIPE_KEY",
        ["field"] = "password",
        ["reason"] = "deploy",
        ["ttl_seconds"] = 60,
    };

    [Fact]
    public async Task ACredentialRequest_ThatOvertookTheHandshake_IsPutToTheOwnerAndLoggedUnderTheNameItGave()
    {
        await using var harness = new McpHarness();
        harness.Approver.StartApproving();
        harness.Serve();

        await CallAsync(harness, ToolText.CredentialToolName, Credential);

        var asked = Assert.Single(harness.Approver.Received);
        Assert.Equal((McpHarness.ClientName, McpHarness.ClientVersion), (asked.ClientName, asked.ClientVersion));
        Assert.Equal(("prompt", McpHarness.ClientName, McpHarness.ClientVersion), Logged(harness));
    }

    [Fact]
    public async Task AListing_ThatOvertookTheHandshake_IsLoggedUnderTheNameItGave()
    {
        await using var harness = new McpHarness();
        harness.Source.With("env/dev", "STRIPE_KEY");
        harness.Serve();

        await CallAsync(harness, ToolText.ListToolName, null);

        Assert.Equal(("exposure", McpHarness.ClientName, McpHarness.ClientVersion), Logged(harness));
    }

    [Fact]
    public async Task ARun_ThatOvertookTheHandshake_IsPutToTheOwnerAndLoggedUnderTheNameItGave()
    {
        await using var harness = new McpHarness();
        var asked = new ConcurrentQueue<RunRequest>();
        harness.Approver.RunAnswer = request =>
        {
            asked.Enqueue(request);
            return FakeApprover.RunReplyOf(EnvOutcome.NoSession, AuditMethod.NoSession, []);
        };
        harness.Approver.Start();
        harness.Serve("--expose", "env/**", "--allow-run");

        await CallAsync(harness, ToolText.RunToolName, new()
        {
            ["command"] = new[] { Reporter.Path },
            ["directory"] = Path.GetDirectoryName(harness.VaultPath),
            ["project"] = "acme-api",
            ["reason"] = "run the pending migration",
        });

        var run = Assert.Single(asked);
        Assert.Equal((McpHarness.ClientName, McpHarness.ClientVersion), (run.ClientName, run.ClientVersion));
        Assert.Equal(("no-session", McpHarness.ClientName, McpHarness.ClientVersion), Logged(harness));
    }

    [Fact]
    public async Task ACallFromAClientThatNeverIntroducedItself_IsStillRefusedUnasked()
    {
        await using var harness = new McpHarness();
        harness.Approver.StartApproving();
        harness.Serve();

        await CallAsync(harness, ToolText.CredentialToolName, Credential, introduces: false);

        Assert.Empty(harness.Approver.Received);
        Assert.Equal<(string?, string?, string?)>(("not-initialized", null, null), Logged(harness));
    }

    private static async Task CallAsync(McpHarness harness, string tool, Dictionary<string, object?>? arguments, bool introduces = true)
    {
        await using var server = new LateHandshakeServer(introduces);

        await harness.Tool(tool).InvokeAsync(
            new RequestContext<CallToolRequestParams>(
                server,
                new JsonRpcRequest { Method = RequestMethods.ToolsCall },
                new CallToolRequestParams
                {
                    Name = tool,
                    Arguments = arguments?.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value)),
                }),
            Token);
    }

    /// <summary>The call's one audit line: how it was decided, and the name and version it gives the client.</summary>
    private static (string? Method, string? Name, string? Version) Logged(McpHarness harness)
    {
        using var parsed = JsonDocument.Parse(Assert.Single(harness.AuditLines()));
        var client = parsed.RootElement.GetProperty("client");

        return (Text(parsed.RootElement, "method"), Text(client, "name"), Text(client, "version"));
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString() : null;

#pragma warning disable MCPEXP002 // Subclassing is the one way to publish the handshake after a call has started, in process.
    /// <summary>
    /// A server whose client has not introduced itself the first time anybody asks, and has every time
    /// after unless it never does: the handshake landing just after the call that overtook it.
    /// </summary>
    private sealed class LateHandshakeServer(bool introduces) : McpServer
    {
        private readonly Implementation _client = new() { Name = McpHarness.ClientName, Version = McpHarness.ClientVersion };
        private int _asked;

        public override Implementation? ClientInfo => Interlocked.Increment(ref _asked) > 1 && introduces ? _client : null;

        public override ClientCapabilities? ClientCapabilities => null;

        public override McpServerOptions ServerOptions { get; } = new();

        public override IServiceProvider? Services => null;

        public override LoggingLevel? LoggingLevel => null;

        public override string? SessionId => null;

        public override string? NegotiatedProtocolVersion => null;

        public override Task RunAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public override Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public override Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public override IAsyncDisposable RegisterNotificationHandler(string method, Func<JsonRpcNotification, CancellationToken, ValueTask> handler) =>
            throw new NotSupportedException();

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
#pragma warning restore MCPEXP002
}
