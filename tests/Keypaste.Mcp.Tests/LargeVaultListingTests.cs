using System.Security.Cryptography;
using System.Text.Json;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Ipc;
using Keypaste.Core.Policy;
using Keypaste.Core.Tests;
using Keypaste.Mcp.Tools;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Keypaste.Mcp.Tests;

/// <summary>
/// A vault with more entries than one reply can carry, everything real except the human.
/// </summary>
/// <remarks>
/// <para>
/// A real KDBX vault, a real <see cref="VaultEntryNameLister"/>, a real
/// <see cref="ApproverHandler"/> behind a real <see cref="ApproverListener"/> on a real pipe, the
/// real bridge and a real MCP client — the same shape as <see cref="SecretHygieneTests"/>, in its
/// own class so that seeding two thousand entries does not slow that one's two dozen tests.
/// </para>
/// <para>
/// Two thousand entries is an unremarkable vault for anyone who has imported from a password
/// manager, and it is well over what one sixty-four kibibyte frame holds. Until F.3c that vault
/// could not be listed at all: the write threw, the connection went down, and it took with it the
/// grants scoped to it — so listing your own entries silently revoked an approval you had just
/// given. That is what <see cref="OnALargeVault_AGrantSurvivesAListing"/> is about, and it is the
/// part a person would actually notice.
/// </para>
/// </remarks>
public sealed class LargeVaultListingTests : IAsyncLifetime
{
    private const string _master = "correct horse battery staple";
    private const string _sentinel = "SENTINEL-LARGE-VAULT-9a3e21";

    /// <summary>Comfortably past what one frame holds, and an ordinary size for a real vault.</summary>
    private const int _entries = 2000;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string _directory = string.Empty;
    private Vault? _vault;
    private readonly ScriptedHuman _human = new();
    private GrantCache? _grants;
    private ApprovalGate? _gate;
    private ApproverListener? _listener;
    private CancellationTokenSource? _stop;
    private Task? _serving;

    private string PipeName { get; } =
        "keypaste-large-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

    public ValueTask InitializeAsync()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-large-").FullName;
        // F.9: a key derivation blocks this thread on work queued to the same pool, so it is marked
        // where a stall in this process can be lined up against it.
        PoolTimeline.Mark("derive-enter", "LargeVaultListingTests; pool thread " + Thread.CurrentThread.IsThreadPoolThread);
        _vault = Vault.Create(Path.Combine(_directory, "vault.kdbx"), _master);
        PoolTimeline.Mark("derive-exit");

        _vault.AddEntry(new VaultEntry
        {
            GroupPath = "env/dev",
            Title = "STRIPE_KEY",
            Password = _sentinel,
        });

        for (var i = 0; i < _entries; i++)
        {
            _vault.AddEntry(new VaultEntry
            {
                GroupPath = "env/dev/services",
                Title = $"SERVICE_ACCOUNT_ACCESS_TOKEN_AB_{i:D4}",
                Password = $"not-the-sentinel-{i}",
            });
        }

        _grants = new GrantCache(TimeProvider.System);
        _gate = new ApprovalGate(_human, TimeProvider.System, ApprovalLimits.Default);

        _stop = new CancellationTokenSource();
        _listener = new ApproverListener(
            PipeName,
            new ApproverHandler(
                new VaultCredentialSource(() => _vault),
                new VaultEntryNameLister(() => _vault),
                _gate,
                _grants,
                PolicyGate.None));

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
        _grants?.Dispose();
        _vault?.Dispose();

        Directory.Delete(_directory, recursive: true);
    }

    private static Dictionary<string, object?> Ask() =>
        new()
        {
            ["entry"] = "env/dev/STRIPE_KEY",
            ["field"] = "password",
            ["reason"] = "deploy the billing service to staging",
            ["ttl_seconds"] = 300,
        };

    private static string TextOf(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private static List<string> MethodsOf(McpHarness harness) =>
        [.. harness.AuditLines().Select(line =>
        {
            using var parsed = JsonDocument.Parse(line);
            return parsed.RootElement.GetProperty("method").GetString()!;
        })];

    private async Task<(McpHarness Harness, McpClient Client)> StartAsync()
    {
        var harness = new McpHarness(PipeName);
        return (harness, await harness.StartAsync());
    }

    /// <summary>
    /// A large vault lists what fits, says it is not the whole list, and releases nothing.
    /// </summary>
    [Fact]
    public async Task ALargeVault_ListsWhatFitsAndSaysSoWithoutLeakingAField()
    {
        var (harness, client) = await StartAsync();
        await using var _ = harness;

        var call = await ListingCall.ReadAsync(client, harness, Token);
        var text = call.Text;

        Assert.False(call.Result.IsError, call.Report());
        Assert.True(call.Structured.GetProperty("truncated").GetBoolean());
        Assert.True(call.Structured.GetProperty("count").GetInt32() > 0);
        Assert.Contains(ToolText.ListingIncomplete, text, StringComparison.Ordinal);

        // A listing is names. Nothing about running out of room turns it into a credential path.
        Assert.DoesNotContain(_sentinel, text, StringComparison.Ordinal);
        Assert.DoesNotContain(_sentinel, harness.Transcript, StringComparison.Ordinal);
        Assert.DoesNotContain(_sentinel, string.Join("\n", harness.AuditLines()), StringComparison.Ordinal);
    }

    /// <summary>
    /// Listing a large vault does not cost the connection the approval already given on it.
    /// </summary>
    /// <remarks>
    /// The whole failure in one test. A grant is scoped to the connection the approver minted it
    /// for, and an over-size reply ended that connection — so the second request, which should have
    /// been answered from the cache without troubling anybody, went back to a person instead. The
    /// audit method is what settles it: <c>grant-cache</c> means "they already said yes to this",
    /// and <c>prompt</c> means they were asked twice.
    /// </remarks>
    [Fact]
    public async Task OnALargeVault_AGrantSurvivesAListing()
    {
        var (harness, client) = await StartAsync();
        await using var _ = harness;

        _human.Answer = ApprovalAnswer.Approved;

        var first = await client.CallToolAsync(ToolText.CredentialToolName, Ask(), cancellationToken: Token);
        Assert.Contains(_sentinel, TextOf(first), StringComparison.Ordinal);

        await client.CallToolAsync(ToolText.ListToolName, cancellationToken: Token);

        var again = await client.CallToolAsync(ToolText.CredentialToolName, Ask(), cancellationToken: Token);
        Assert.Contains(_sentinel, TextOf(again), StringComparison.Ordinal);

        Assert.Equal(["prompt", "exposure", "grant-cache"], MethodsOf(harness));
        Assert.Equal(1, _human.Asked);
    }

    /// <summary>Answers however the test says, and counts how often it was asked.</summary>
    private sealed class ScriptedHuman : IApprovalChannel
    {
        internal ApprovalAnswer Answer { get; set; } = ApprovalAnswer.Denied;

        internal int Asked { get; private set; }

        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken)
        {
            Asked++;

            return ValueTask.FromResult(Answer);
        }
    }
}
