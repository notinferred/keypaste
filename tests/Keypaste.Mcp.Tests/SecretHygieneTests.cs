using System.Security.Cryptography;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Ipc;
using Keypaste.Mcp.Tools;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Keypaste.Mcp.Tests;

/// <summary>
/// Proves that a released credential takes exactly one path out of keypaste, and that the three
/// fields nobody asked for take none.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything here is real except the human.</b> A real KDBX vault, a real
/// <see cref="VaultCredentialSource"/>, a real <see cref="ApproverHandler"/> behind a real named
/// pipe, the real bridge, and a real MCP client. The only test double is the thing that answers
/// yes or no — which is the only part of the system a test is allowed to stand in for, because a
/// person is not automatable.
/// </para>
/// <para>
/// <b>Why four sentinels and not one.</b> DECISIONS.md D-0022 and THREATS.md T-8 are explicit that
/// this repository has already rejected a "no secret leaked" test whose sentinel was never present
/// anywhere it could leak — that test proved the type system and nothing else. Here every one of
/// the entry's four fields carries a different, searchable value. The requested one has to come
/// back; the other three have to appear nowhere at all. A source that returned the whole entry, a
/// result builder that included one field too many, or a log line that recorded the value would
/// each fail on a different sentinel.
/// </para>
/// <para>
/// <b>And the sweep is over bytes.</b> The tool result, the audit log's raw text and the raw
/// JSON-RPC transcript are each searched, because a claim about what did not leak is only worth
/// making against what actually left the process rather than against an object parsed out of it.
/// </para>
/// </remarks>
public sealed class SecretHygieneTests : IAsyncLifetime
{
    internal const string Master = "correct horse battery staple";

    internal const string SentinelPassword = "SENTINEL-PASSWORD-a17f3c";
    internal const string SentinelUsername = "SENTINEL-USERNAME-b28e4d";
    internal const string SentinelUrl = "https://SENTINEL-URL-c39f5e.example";
    internal const string SentinelNotes = "SENTINEL-NOTES-d40a6f";

    /// <summary>A secret in a part of the vault this server was never allowed to name.</summary>
    internal const string SentinelOutOfScope = "SENTINEL-OUT-OF-SCOPE-e51b70";

    private static readonly string[] _everySentinel =
    [
        SentinelPassword,
        SentinelUsername,
        SentinelUrl,
        SentinelNotes,
        SentinelOutOfScope,
    ];

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
        "keypaste-hygiene-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

    public ValueTask InitializeAsync()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-hygiene-").FullName;
        _vault = Vault.Create(Path.Combine(_directory, "vault.kdbx"), Master);

        _vault.AddEntry(new VaultEntry
        {
            GroupPath = "env/dev",
            Title = "STRIPE_KEY",
            Password = SentinelPassword,
            Username = SentinelUsername,
            Url = SentinelUrl,
            Notes = SentinelNotes,
        });

        // Outside the default env/** exposure, so nothing this server can be asked should ever
        // reach it. Planted somewhere it could genuinely leak, which is the whole point.
        _vault.AddEntry(new VaultEntry
        {
            GroupPath = "personal",
            Title = "bank",
            Password = SentinelOutOfScope,
        });

        _grants = new GrantCache(TimeProvider.System);
        _gate = new ApprovalGate(_human, TimeProvider.System, ApprovalLimits.Default);

        var handler = new ApproverHandler(
            new VaultCredentialSource(() => _vault),
            new VaultEntryNameLister(() => _vault),
            _gate,
            _grants);

        _stop = new CancellationTokenSource();
        _listener = new ApproverListener(PipeName, handler);
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

    private async Task<(McpHarness Harness, McpClient Client)> StartAsync()
    {
        var harness = new McpHarness(PipeName);
        return (harness, await harness.StartAsync());
    }

    private static Dictionary<string, object?> Ask(
        string entry = "env/dev/STRIPE_KEY",
        string field = "password",
        int ttl = 300) =>
        new()
        {
            ["entry"] = entry,
            ["field"] = field,
            ["reason"] = "deploy the billing service to staging",
            ["ttl_seconds"] = ttl,
        };

    private static string TextOf(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    /// <summary>
    /// Sweeps every channel that leaves the process for a sentinel, and says which one it found it
    /// in. The audit log and the raw transcript are searched as text, because that is what is
    /// actually on disk and on the wire.
    /// </summary>
    private static void AssertNowhere(McpHarness harness, string sentinel, string result, string what)
    {
        Assert.False(
            result.Contains(sentinel, StringComparison.Ordinal),
            $"{what}: the tool result contains {sentinel}");

        // The bytes that actually left the process, not an object parsed out of them.
        Assert.False(
            harness.Transcript.Contains(sentinel, StringComparison.Ordinal),
            $"{what}: the wire carried {sentinel}");

        Assert.False(
            harness.AuditText.Contains(sentinel, StringComparison.Ordinal),
            $"{what}: the audit log contains {sentinel}");
    }

    /// <summary>
    /// The approve path, and the only one in keypaste that produces a credential. The requested
    /// field comes back; the other three, which the same entry is carrying, do not.
    /// </summary>
    [Fact]
    public async Task OnApproval_TheRequestedFieldComesBack_AndOnlyThat()
    {
        _human.Answer = ApprovalAnswer.Approved;

        var (harness, client) = await StartAsync();

        await using (harness)
        {
            var result = await client.CallToolAsync(ToolText.CredentialToolName, Ask(), cancellationToken: Token);
            var text = TextOf(result);

            Assert.False(result.IsError);
            Assert.Contains(SentinelPassword, text, StringComparison.Ordinal);

            // The three the agent did not ask for, from the very same entry. This is the assertion
            // a source that returned a whole VaultEntry would fail.
            Assert.DoesNotContain(SentinelUsername, text, StringComparison.Ordinal);
            Assert.DoesNotContain(SentinelUrl, text, StringComparison.Ordinal);
            Assert.DoesNotContain(SentinelNotes, text, StringComparison.Ordinal);

            // And the log records the access without recording what was handed over.
            var log = harness.AuditText;

            foreach (var sentinel in _everySentinel)
            {
                Assert.DoesNotContain(sentinel, log, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// Each field in turn, so "only the requested one" is proved four times rather than once for
    /// whichever field happens to be first.
    /// </summary>
    [Theory]
    [InlineData("password", SentinelPassword)]
    [InlineData("username", SentinelUsername)]
    [InlineData("url", SentinelUrl)]
    [InlineData("notes", SentinelNotes)]
    public async Task WhicheverFieldIsAskedFor_IsTheOnlyOneReleased(string field, string expected)
    {
        _human.Answer = ApprovalAnswer.Approved;

        var (harness, client) = await StartAsync();

        await using (harness)
        {
            var text = TextOf(await client.CallToolAsync(ToolText.CredentialToolName, Ask(field: field), cancellationToken: Token));

            Assert.Contains(expected, text, StringComparison.Ordinal);

            foreach (var other in _everySentinel.Where(s => !string.Equals(s, expected, StringComparison.Ordinal)))
            {
                Assert.DoesNotContain(other, text, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// Every path that is not an approval, swept the same way. The vault is real and the field is
    /// genuinely there, so each of these is a case where the secret existed, was reachable, and did
    /// not come out.
    /// </summary>
    [Theory]
    [InlineData(ApprovalAnswer.Denied)]
    [InlineData(ApprovalAnswer.TimedOut)]
    [InlineData(ApprovalAnswer.Busy)]
    [InlineData(ApprovalAnswer.NoChannel)]
    [InlineData(ApprovalAnswer.Failed)]
    public async Task OnEveryAnswerThatIsNotYes_NothingLeaves(ApprovalAnswer answer)
    {
        _human.Answer = answer;

        var (harness, client) = await StartAsync();

        await using (harness)
        {
            var result = await client.CallToolAsync(ToolText.CredentialToolName, Ask(), cancellationToken: Token);

            Assert.True(result.IsError);

            foreach (var sentinel in _everySentinel)
            {
                AssertNowhere(harness, sentinel, TextOf(result), answer.ToString());
            }
        }
    }

    /// <summary>
    /// A channel that throws is an error path, and law 3.7 makes an error path a denial. Separate
    /// from the table above because an exception is the shape most likely to skip the checks the
    /// other answers go through.
    /// </summary>
    [Fact]
    public async Task WhenAskingThrows_NothingLeaves()
    {
        _human.Throw = true;

        var (harness, client) = await StartAsync();

        await using (harness)
        {
            var result = await client.CallToolAsync(ToolText.CredentialToolName, Ask(), cancellationToken: Token);

            Assert.True(result.IsError);

            foreach (var sentinel in _everySentinel)
            {
                AssertNowhere(harness, sentinel, TextOf(result), "a channel that threw");
            }
        }
    }

    /// <summary>
    /// The entry outside the exposure, asked for both ways it can be named. Its secret is real and
    /// sitting in the same vault the approver has open, which is what makes this a test rather than
    /// a tautology.
    /// </summary>
    [Fact]
    public async Task AnEntryOutsideTheExposure_YieldsNothingByEitherName()
    {
        _human.Answer = ApprovalAnswer.Approved;

        var (harness, client) = await StartAsync();

        await using (harness)
        {
            var byPath = await client.CallToolAsync(
                ToolText.CredentialToolName, Ask(entry: "personal/bank"), cancellationToken: Token);

            var byHandle = await client.CallToolAsync(
                ToolText.CredentialToolName,
                Ask(entry: EntryHandle.For(new EntryName("personal", "bank"))),
                cancellationToken: Token);

            Assert.True(byPath.IsError);
            Assert.True(byHandle.IsError);

            AssertNowhere(harness, SentinelOutOfScope, TextOf(byPath) + TextOf(byHandle), "out of scope");

            // Nobody was even asked. A refusal that had prompted first would still have put an
            // entry name the user never exposed in front of them.
            Assert.Equal(0, _human.Asked);
        }
    }

    /// <summary>
    /// The listing path, which must never produce a field value under any circumstances — that is
    /// what <see cref="EntryName"/> having two members is for (THREATS.md T-8).
    /// </summary>
    [Fact]
    public async Task TheListingPath_NeverProducesAFieldValue()
    {
        var (harness, client) = await StartAsync();

        await using (harness)
        {
            var result = await client.CallToolAsync(ToolText.ListToolName, cancellationToken: Token);
            var text = TextOf(result);

            // First, that the listing actually listed something. Without this the sweep below would
            // pass for a listing that refused, which proves nothing at all - the exact trap
            // THREATS.md T-8 says this repository has already fallen into once.
            Assert.False(result.IsError, text);
            Assert.Contains("STRIPE_KEY", text, StringComparison.Ordinal);

            // And that the entry whose name it just showed did not bring its fields along.
            foreach (var sentinel in _everySentinel)
            {
                AssertNowhere(harness, sentinel, text, "the listing path");
            }

            // The out-of-scope entry is not even named, let alone read.
            Assert.DoesNotContain("bank", text, StringComparison.Ordinal);
        }
    }

    /// <summary>Answers however the test says, and counts how often it was asked.</summary>
    private sealed class ScriptedHuman : IApprovalChannel
    {
        internal ApprovalAnswer Answer { get; set; } = ApprovalAnswer.Denied;

        internal bool Throw { get; set; }

        internal int Asked { get; private set; }

        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken)
        {
            Asked++;

            return Throw
                ? throw new InvalidOperationException("the approval channel is not available")
                : ValueTask.FromResult(Answer);
        }
    }
}
