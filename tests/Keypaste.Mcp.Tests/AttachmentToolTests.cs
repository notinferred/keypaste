using System.Security.Cryptography;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Ipc;
using Keypaste.Core.Tests;
using Keypaste.Mcp.Tools;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Keypaste.Mcp.Tests;

/// <summary><c>request_attachment</c> over a real vault, owner, pipe, bridge and MCP client, with only the person scripted.</summary>
public sealed class AttachmentToolTests : IAsyncLifetime
{
    private const string _master = "correct horse battery staple";

    /// <summary>Past one sixty-four kibibyte frame, so the release crosses the pipe only in an attachment's wider reply.</summary>
    private static readonly byte[] _certificate = RandomNumberGenerator.GetBytes(1024 * 1024);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly ScriptedHuman _human = new();
    private string _directory = string.Empty;
    private Vault? _vault;
    private OwnerLog? _log;
    private ApprovalGate? _gate;
    private ApproverListener? _listener;
    private CancellationTokenSource? _stop;
    private Task? _serving;

    private string PipeName { get; } = "keypaste-attachment-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

    public ValueTask InitializeAsync()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-attachment-").FullName;
        _vault = Vault.Create(Path.Combine(_directory, "vault.kdbx"), _master);
        var item = new EntryName("env/dev", "deploy");
        _vault.AddEntry(new VaultEntry { GroupPath = item.GroupPath, Title = item.Title, Password = "not-the-one-under-test" });
        Assert.True(_vault.AddAttachment(item, "deploy.p12", new MemoryStream(_certificate), out _));
        _vault.Save();

        _gate = new ApprovalGate(_human, TimeProvider.System, ApprovalLimits.Default);
        _log = new OwnerLog(Path.Combine(_directory, "home"));
        _stop = new CancellationTokenSource();
        _listener = new ApproverListener(
            PipeName,
            TestSession.Over(
                _vault,
                new ApproverHandler(new VaultCredentialSource(() => _vault), new VaultEntryNameLister(() => _vault), _gate, TestSession.Settings()),
                _log.Log));
        _serving = _listener.RunAsync(_stop.Token);

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_stop is not null)
        {
            await _stop.CancelAsync();
        }

        if (_serving is not null)
        {
            try
            {
                await _serving;
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
                // Tearing the listener down is how it stops.
            }
        }

        _listener?.Dispose();
        _stop?.Dispose();
        _gate?.Dispose();
        _vault?.Dispose();
        _log?.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task An_approved_attachment_reaches_the_agent_as_base64_and_is_logged_once()
    {
        await using var harness = new McpHarness(PipeName, _vault!.Path);
        var client = await harness.StartAsync();
        _human.Answer = ApprovalAnswer.ApprovedOnce;

        var result = await Ask(client, "deploy.p12");

        Assert.False(result.IsError);
        Assert.Equal(Convert.ToBase64String(_certificate), result.StructuredContent!.Value.GetProperty("base64").GetString());
        Assert.Equal("attachment deploy.p12", _human.Field);
        Assert.Empty(harness.AuditLines());
        var line = Assert.Single(_log!.Lines());
        Assert.Equal(("granted", "attachment"), (line.GetProperty("decision").GetString(), line.GetProperty("args").GetProperty("field").GetString()));
    }

    [Fact]
    public async Task A_refused_attachment_carries_nothing()
    {
        await using var harness = new McpHarness(PipeName, _vault!.Path);
        var client = await harness.StartAsync();
        _human.Answer = ApprovalAnswer.Denied;

        var result = await Ask(client, "deploy.p12");

        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
        Assert.DoesNotContain(Convert.ToBase64String(_certificate)[..64], harness.Transcript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_request_without_a_name_is_refused_before_anybody_is_asked()
    {
        await using var harness = new McpHarness(PipeName, _vault!.Path);
        var client = await harness.StartAsync();

        var result = await Ask(client, string.Empty);

        Assert.True(result.IsError);
        Assert.Equal(0, _human.Asked);
    }

    private static ValueTask<CallToolResult> Ask(McpClient client, string name) =>
        client.CallToolAsync(
            ToolText.AttachmentToolName,
            new Dictionary<string, object?> { ["entry"] = "env/dev/deploy", ["name"] = name, ["reason"] = "sign the staging release" },
            cancellationToken: Token);

    /// <summary>Answers however the test says, and keeps what it was asked about.</summary>
    private sealed class ScriptedHuman : IApprovalChannel
    {
        internal ApprovalAnswer Answer { get; set; } = ApprovalAnswer.Denied;

        internal int Asked { get; private set; }

        internal string? Field { get; private set; }

        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken)
        {
            Asked++;
            Field = prompt.Field;
            return ValueTask.FromResult(Answer);
        }
    }
}
