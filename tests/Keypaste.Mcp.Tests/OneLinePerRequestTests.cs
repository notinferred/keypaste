using System.Security.Cryptography;
using System.Text.Json;
using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Ownership;
using Keypaste.Core.Tests;
using Keypaste.Mcp.Tools;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Keypaste.Mcp.Tests;

/// <summary>
/// Every call is recorded once: by the owner that answered it, in its own home's log, or by the bridge
/// when nothing did, in the log its <c>--audit-log</c> names (THREATS.md T-6, T-14). The shipped bridge
/// against an owner composed as the app and <c>keypaste agent</c> compose it, each with its own log.
/// </summary>
public sealed class OneLinePerRequestTests : IAsyncLifetime
{
    private const string _master = "correct horse battery staple";
    private const string _sentinel = "SENTINEL-ONE-LINE-2d71c4";
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(30);

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-one-line-").FullName;
    private readonly SessionLifetime _lifetime = new();
    private readonly Person _person = new();
    private readonly CancellationTokenSource _stop = new();
    private Vault? _vault;
    private OwnerLog? _log;
    private SessionServer? _server;
    private Task? _serving;
    private volatile bool _unwritable;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    private string Home => Path.Combine(_directory, "home");

    private string Pipe { get; } = "keypaste-one-line-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

    public ValueTask InitializeAsync()
    {
        _vault = Vault.Create(VaultPath, _master);
        _vault.AddEntry(new VaultEntry { GroupPath = "env/dev", Title = "STRIPE_KEY", Password = _sentinel });
        _vault.Save();
        _log = new OwnerLog(Home);

        _server = SessionServer.Listen(Pipe, new SessionServerOptions
        {
            Vault = VaultIdentity.Of(Home, VaultPath),
            Lifetime = _lifetime,
            CurrentLifetime = () => _lifetime,
            UnlockedFor = asked => ReferenceEquals(asked, _lifetime) && asked.IsLive ? _vault : null,
            Channel = _person,
            Settings = TestSession.Settings(),
            Hourly = new HourlyLimit(TimeProvider.System),
            Audit = () => _unwritable ? null : _log?.Log,
            WatchEdits = _ => { },
            UnwatchEdits = _ => { },
        });
        _serving = _server.RunAsync(_stop.Token);

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.End();
        await _stop.CancelAsync();

        try
        {
            await (_serving ?? Task.CompletedTask);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Tearing the listener down is how it stops.
        }

        _server?.Dispose();
        _lifetime.Dispose();
        _stop.Dispose();
        _log?.Dispose();
        _vault?.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private static Dictionary<string, object?> Ask() => new()
    {
        ["entry"] = "env/dev/STRIPE_KEY",
        ["field"] = "password",
        ["reason"] = "deploy the billing service to staging",
        ["ttl_seconds"] = 60,
    };

    /// <summary>
    /// An answered call is the owner's line, in the owner's home, though the bridge was told to log
    /// somewhere else: no bridge argument moves it.
    /// </summary>
    [Fact]
    public async Task AnAnsweredCall_IsOneLineInTheOwnersLog_WhereverTheBridgeLogs()
    {
        _person.Answer = ApprovalAnswer.ApprovedOnce;
        await using var harness = new McpHarness(Pipe, VaultPath);
        var client = await harness.StartAsync("--expose", "env/**");

        var listing = await client.CallToolAsync(ToolText.ListToolName, cancellationToken: Token);
        var released = await client.CallToolAsync(ToolText.CredentialToolName, Ask(), cancellationToken: Token);

        Assert.NotEqual(true, listing.IsError);
        Assert.Contains(_sentinel, TextOf(released), StringComparison.Ordinal);
        Assert.NotEqual(Path.GetDirectoryName(harness.AuditPath), Home);
        Assert.Empty(harness.AuditLines());

        var lines = _log!.Lines();
        Assert.Equal(["list_entry_names", "request_credential"], lines.Select(line => line.GetProperty("tool").GetString()));
        Assert.All(lines, line =>
        {
            Assert.Equal("granted", line.GetProperty("decision").GetString());
            Assert.Equal(McpHarness.ClientName, line.GetProperty("client").GetProperty("name").GetString());
            Assert.Equal(McpHarness.ClientLabel, line.GetProperty("client").GetProperty("label").GetString());
            Assert.Equal(_lifetime.Id, line.GetProperty("session").GetString());
        });
        Assert.DoesNotContain(_sentinel, _log.Text(), StringComparison.Ordinal);
    }

    /// <summary>With nothing holding the vault, the call is the bridge's one line, and the owner's log has none.</summary>
    [Fact]
    public async Task ACallNothingAnswered_IsOneLineInTheBridgesLog()
    {
        await using var harness = new McpHarness("keypaste-nobody-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)), VaultPath);
        var client = await harness.StartAsync("--expose", "env/**");

        var result = await client.CallToolAsync(ToolText.CredentialToolName, Ask(), cancellationToken: Token);

        Assert.True(result.IsError);
        Assert.Equal("no-approver", Field(Assert.Single(harness.AuditLines()), "method"));
        Assert.Empty(_log!.Lines());
    }

    /// <summary>
    /// A client that gives up while the person is being asked leaves one line, cancelled, and it is the
    /// owner's: the bridge withdrew the request it had sent, and the owner recorded the withdrawal.
    /// </summary>
    [Fact]
    public async Task AClientThatGivesUpWhileAsked_IsOneCancelledLine_TheOwners()
    {
        _person.Hold = true;
        await using var harness = new McpHarness(Pipe, VaultPath);
        await GiveUpWhileAskedAsync(harness);

        var line = await OneOwnerLineAsync();
        Assert.Equal("request_credential", line.GetProperty("tool").GetString());
        Assert.Equal("cancelled", line.GetProperty("method").GetString());
        Assert.Equal(_lifetime.Id, line.GetProperty("session").GetString());
        Assert.True(_person.Withdrawn);
        Assert.Empty(harness.AuditLines());
        Assert.DoesNotContain(_sentinel, harness.Transcript, StringComparison.Ordinal);
    }

    /// <summary>
    /// A client that gives up while asked, when the owner's log fails during the decision, leaves one
    /// line, the bridge's: the owner's answer to the withdrawn request says it was not recorded.
    /// </summary>
    [Fact]
    public async Task AClientThatGivesUpWhileAsked_IsOneLineInTheBridgesLog_WhenTheOwnersLogFailsMeanwhile()
    {
        _person.Hold = true;
        await using var harness = new McpHarness(Pipe, VaultPath);
        await GiveUpWhileAskedAsync(harness, whileAsked: () => _unwritable = true);

        var line = await OneBridgeLineAsync(harness);
        Assert.Equal("request_credential", Field(line, "tool"));
        Assert.Equal("denied", Field(line, "decision"));
        Assert.True(_person.Withdrawn);
        Assert.Empty(_log!.Lines());
        Assert.DoesNotContain(_sentinel, harness.Transcript, StringComparison.Ordinal);
    }

    /// <summary>
    /// While the owner's log will not open, a call is refused before the person is asked, the agent is
    /// told it could not be recorded, and the bridge's line is its one line (THREATS.md T-6).
    /// </summary>
    [Fact]
    public async Task ACallWhileTheOwnersLogWillNotOpen_IsRefusedUnasked_InOneBridgeLine()
    {
        _person.Answer = ApprovalAnswer.ApprovedOnce;
        _unwritable = true;
        await using var harness = new McpHarness(Pipe, VaultPath);
        var client = await harness.StartAsync("--expose", "env/**");

        var result = await client.CallToolAsync(ToolText.CredentialToolName, Ask(), cancellationToken: Token);

        Assert.True(result.IsError);
        Assert.Equal(ToolText.AuditUnavailable, TextOf(result));
        Assert.False(_person.Asked.Task.IsCompleted);
        Assert.Equal("failed", Field(Assert.Single(harness.AuditLines()), "method"));
        Assert.Empty(_log!.Lines());
        Assert.DoesNotContain(_sentinel, harness.Transcript, StringComparison.Ordinal);
    }

    /// <summary>Asks for the credential through the shipped bridge and withdraws the call once the person is being asked.</summary>
    /// <remarks>
    /// The SDK's client never tells a server it gave up, so the notification a real client sends is
    /// written by hand here, as <c>RunToolTests.ClientCancel_KillsTheChild</c> writes it.
    /// </remarks>
    private async Task GiveUpWhileAskedAsync(McpHarness harness, Action? whileAsked = null)
    {
        var channels = harness.Serve("--expose", "env/**");
        using var reader = new StreamReader(channels.ClientReads, leaveOpen: true);

        async Task Send(object message)
        {
            await channels.ClientWrites.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(message), Token);
            await channels.ClientWrites.WriteAsync("\n"u8.ToArray(), Token);
            await channels.ClientWrites.FlushAsync(Token);
        }

        await Send(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = McpHarness.ClientName, version = McpHarness.ClientVersion } } });
        await reader.ReadLineAsync(Token);
        await Send(new { jsonrpc = "2.0", method = "notifications/initialized" });
        await Send(new { jsonrpc = "2.0", id = 9, method = "tools/call", @params = new { name = ToolText.CredentialToolName, arguments = Ask() } });

        await _person.Asked.Task.WaitAsync(_wait, Token);
        whileAsked?.Invoke();
        await Send(new { jsonrpc = "2.0", method = "notifications/cancelled", @params = new { requestId = 9, reason = "the client gave up" } });
    }

    private static async Task<string> OneBridgeLineAsync(McpHarness harness)
    {
        var deadline = DateTime.UtcNow + _wait;

        while (DateTime.UtcNow < deadline)
        {
            if (harness.AuditLines() is [var only])
            {
                return only;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), Token);
        }

        Assert.Fail($"the bridge's log holds {harness.AuditLines().Length} line(s), not one");
        return string.Empty;
    }

    private async Task<JsonElement> OneOwnerLineAsync()
    {
        var deadline = DateTime.UtcNow + _wait;

        while (DateTime.UtcNow < deadline)
        {
            if (_log!.Lines() is [var only])
            {
                return only;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), Token);
        }

        Assert.Fail($"the owner's log holds {_log!.Lines().Count} line(s), not one");
        return default;
    }

    private static string TextOf(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private static string? Field(string line, string name)
    {
        using var parsed = JsonDocument.Parse(line);
        return parsed.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
    }

    /// <summary>The person: answers as told, or holds the prompt up until it is withdrawn.</summary>
    private sealed class Person : IApprovalChannel
    {
        internal ApprovalAnswer Answer { get; set; } = ApprovalAnswer.Denied;

        internal bool Hold { get; set; }

        internal bool Withdrawn { get; private set; }

        internal TaskCompletionSource Asked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken)
        {
            Asked.TrySetResult();

            if (!Hold)
            {
                return Answer;
            }

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Withdrawn = true;
            }

            return ApprovalAnswer.Denied;
        }
    }
}
