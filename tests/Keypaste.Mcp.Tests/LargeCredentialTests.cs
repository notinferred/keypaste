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
/// An entry whose field is larger than one reply, everything real except the human.
/// </summary>
/// <remarks>
/// <para>
/// A real KDBX vault, a real <see cref="ApproverHandler"/> behind a real
/// <see cref="ApproverListener"/> on a real pipe, the real bridge and a real MCP client — the shape
/// <see cref="LargeVaultListingTests"/> uses, pointed at the other half of the same defect.
/// </para>
/// <para>
/// <c>notes</c> is a releasable field and a KDBX note has no length limit, so an entry somebody
/// pasted a certificate or a private key into is an ordinary entry rather than an attack. Until
/// F.3d, approving a request for one cost the connection, revoked the grants scoped to it, put a
/// second prompt in front of the same person and was written to the audit log as
/// <c>denied</c>/<c>failed</c> — "the approver could not be asked" — about a request somebody had
/// answered.
/// </para>
/// </remarks>
public sealed class LargeCredentialTests : IAsyncLifetime
{
    private const string _master = "correct horse battery staple";

    /// <summary>Planted where a truncating fix would keep it: at the front of the note.</summary>
    private const string _noteSentinel = "NOTE-SENTINEL-LARGE-4c71fa";

    private const string _passwordSentinel = "sk_live_SENTINEL-SMALL-8b20de";

    /// <summary>Comfortably past one sixty-four kibibyte frame, and a plausible pasted key.</summary>
    private const int _noteBytes = 200_000;

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
        "keypaste-huge-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

    public ValueTask InitializeAsync()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-huge-").FullName;
        // F.9: a key derivation blocks this thread on work queued to the same pool, so it is marked
        // where a stall in this process can be lined up against it.
        PoolTimeline.Mark("derive-enter", "LargeCredentialTests; pool thread " + Thread.CurrentThread.IsThreadPoolThread);
        _vault = Vault.Create(Path.Combine(_directory, "vault.kdbx"), _master);
        PoolTimeline.Mark("derive-exit");

        _vault.AddEntry(new VaultEntry
        {
            GroupPath = "env/dev",
            Title = "DEPLOY_CERT",
            Password = "not-the-one-under-test",
            Notes = _noteSentinel + new string('n', _noteBytes),
        });

        _vault.AddEntry(new VaultEntry
        {
            GroupPath = "env/dev",
            Title = "STRIPE_KEY",
            Password = _passwordSentinel,
        });

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

    private static Dictionary<string, object?> AskForTheNote() =>
        new()
        {
            ["entry"] = "env/dev/DEPLOY_CERT",
            ["field"] = "notes",
            ["reason"] = "read the deployment certificate for the staging rollout",
            ["ttl_seconds"] = 300,
        };

    private static Dictionary<string, object?> AskForThePassword() =>
        new()
        {
            ["entry"] = "env/dev/STRIPE_KEY",
            ["field"] = "password",
            ["reason"] = "deploy the billing service to staging",
            ["ttl_seconds"] = 300,
        };

    private static string TextOf(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private static List<(string Method, string Reason)> LinesOf(McpHarness harness) =>
        [.. harness.AuditLines().Select(line =>
        {
            using var parsed = JsonDocument.Parse(line);

            return (parsed.RootElement.GetProperty("method").GetString()!,
                    parsed.RootElement.GetProperty("reason").GetString()!);
        })];

    private async Task<(McpHarness Harness, McpClient Client)> StartAsync()
    {
        var harness = new McpHarness(PipeName);
        return (harness, await harness.StartAsync());
    }

    /// <summary>
    /// A person approves, the value will not fit, and everybody is told the truth about it.
    /// </summary>
    /// <remarks>
    /// The whole defect in one test. The agent is told the request was authorized and the value
    /// could not be returned — not that something went wrong while asking, which is what it read
    /// before and which describes a request nobody answered.
    /// </remarks>
    [Fact]
    public async Task AnEntryWithAHugeNote_IsRefusedWithAnHonestReason()
    {
        var (harness, client) = await StartAsync();
        await using var _ = harness;

        _human.Answer = ApprovalAnswer.Approved;

        var result = await client.CallToolAsync(
            ToolText.CredentialToolName, AskForTheNote(), cancellationToken: Token);

        var text = TextOf(result);

        Assert.True(result.IsError);
        Assert.Equal(ToolText.Undeliverable, text, StringComparer.Ordinal);
        Assert.DoesNotContain("went wrong", text, StringComparison.Ordinal);

        var lines = LinesOf(harness);

        Assert.Equal("undeliverable", Assert.Single(lines).Method, StringComparer.Ordinal);

        // The claim in words rather than against the constant: ApproverProtocolTests pins the exact
        // sentence, and what this file is for is that the sentence reaches the log at all.
        Assert.Contains("a person approved this release", lines[0].Reason, StringComparison.Ordinal);
        Assert.Contains("too large to send", lines[0].Reason, StringComparison.Ordinal);

        // A person really was asked. "Denied" here means nothing reached the agent, not that the
        // procedure stopped before anybody looked at it.
        Assert.Equal(1, _human.Asked);
    }

    /// <summary>Not one byte of the note leaves, on any of the three surfaces it could leave by.</summary>
    /// <remarks>
    /// The sentinel is the first thing in the note, so a reply trimmed to fit would carry it. Planted
    /// somewhere it could genuinely leak, which is the discipline <see cref="SecretHygieneTests"/>
    /// sets: asserting the absence of a string that was never anywhere proves nothing.
    /// </remarks>
    [Fact]
    public async Task AnUndeliverableRelease_LeaksNoPartOfTheNote()
    {
        var (harness, client) = await StartAsync();
        await using var _ = harness;

        _human.Answer = ApprovalAnswer.Approved;

        var result = await client.CallToolAsync(
            ToolText.CredentialToolName, AskForTheNote(), cancellationToken: Token);

        Assert.DoesNotContain(_noteSentinel, TextOf(result), StringComparison.Ordinal);
        Assert.DoesNotContain(_noteSentinel, harness.Transcript, StringComparison.Ordinal);
        Assert.DoesNotContain(_noteSentinel, harness.AuditText, StringComparison.Ordinal);
    }

    /// <summary>
    /// The connection survives it, and the next approved request on it still works.
    /// </summary>
    /// <remarks>
    /// The half a refusal on its own would not show. An over-size write ended the connection, and
    /// <c>Disconnected</c> revoked every grant scoped to it — so the damage reached requests that
    /// had nothing to do with the entry anybody had asked for.
    /// </remarks>
    [Fact]
    public async Task AfterAnUndeliverableRelease_TheNextRequestStillWorks()
    {
        var (harness, client) = await StartAsync();
        await using var _ = harness;

        _human.Answer = ApprovalAnswer.Approved;

        await client.CallToolAsync(ToolText.CredentialToolName, AskForTheNote(), cancellationToken: Token);

        var next = await client.CallToolAsync(
            ToolText.CredentialToolName, AskForThePassword(), cancellationToken: Token);

        Assert.False(next.IsError);
        Assert.Contains(_passwordSentinel, TextOf(next), StringComparison.Ordinal);

        Assert.Equal(["undeliverable", "prompt"], LinesOf(harness).Select(line => line.Method));
    }

    /// <summary>Asking twice for a field that cannot be delivered costs one prompt, not two.</summary>
    /// <remarks>
    /// The size belongs to the entry rather than to the request, so every retry ends the same way.
    /// The grant is stored as any approved release stores one, which is what lets the second request
    /// be answered from the cache instead of going back to a person (THREATS.md T-11). The two lines
    /// carry the same method and different reasons, and the reason is where "they had already said
    /// yes" is recorded.
    /// </remarks>
    [Fact]
    public async Task AnUndeliverableRelease_AsksAPersonOnce()
    {
        var (harness, client) = await StartAsync();
        await using var _ = harness;

        _human.Answer = ApprovalAnswer.Approved;

        // F.9 watches the pool across both calls. This test records `no-approver` where
        // `undeliverable` was due when it fails, and `undeliverable` has no budget of its own - it
        // is a size decision inside the approver, reached only if the exchange completed at all. So
        // the flip is the five-hundred-millisecond connect expiring, and what the pool was doing
        // while it expired is the reading that says why.
        using var watch = PoolSnapshot.Watch("two requests for a field that cannot be delivered");
        PoolTimeline.Mark("credential-enter");

        await client.CallToolAsync(ToolText.CredentialToolName, AskForTheNote(), cancellationToken: Token);
        await client.CallToolAsync(ToolText.CredentialToolName, AskForTheNote(), cancellationToken: Token);

        PoolTimeline.Mark("credential-exit");

        Assert.Equal(1, _human.Asked);

        var lines = LinesOf(harness);
        var methods = lines.Select(line => line.Method).ToArray();

        if (!methods.SequenceEqual(["undeliverable", "undeliverable"]))
        {
            Assert.Fail(
                $"the audit recorded [{string.Join(", ", methods)}] rather than two undeliverables.{Environment.NewLine}{watch.Report()}");
        }
        Assert.Contains("had already approved", lines[1].Reason, StringComparison.Ordinal);
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
