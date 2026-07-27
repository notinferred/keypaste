using System.Security.Cryptography;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Ipc;
using Keypaste.Core.Policy;
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

    /// <summary>
    /// A secret inside the exposure but outside every policy rule, so it is reachable by asking a
    /// person and never by a standing rule.
    /// </summary>
    /// <remarks>
    /// This one is load-bearing, and it is why the count went from five to six. "The rule did not
    /// widen" asserted against <see cref="SentinelOutOfScope"/> alone would be asserted against a
    /// value the exposure already blocks, so it would pass for a policy check that did nothing at
    /// all — exactly the vacuous-sentinel trap this class's own doc comment says the repository has
    /// fallen into before.
    /// </remarks>
    internal const string SentinelOutsideTheRule = "SENTINEL-OUTSIDE-THE-RULE-f62c81";

    private static readonly string[] _everySentinel =
    [
        SentinelPassword,
        SentinelUsername,
        SentinelUrl,
        SentinelNotes,
        SentinelOutOfScope,
        SentinelOutsideTheRule,
    ];

    /// <summary>
    /// The rule the second approver runs with. It keys on the label the <em>operator</em> gave the
    /// bridge, never on the name the client asserts (THREATS.md T-3), and it covers one field of one
    /// subtree — so three of the six sentinels are inside the exposure and outside it.
    /// </summary>
    private static string Policy =>
        $"""
        [[allow]]
        client          = "{McpHarness.ClientLabel}"
        entries         = ["env/dev/**"]
        fields          = ["password"]
        max_ttl_seconds = 300
        """;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string _directory = string.Empty;
    private Vault? _vault;
    private readonly ScriptedHuman _human = new();
    private GrantCache? _grants;
    private ApprovalGate? _gate;
    private ApproverListener? _listener;
    private CancellationTokenSource? _stop;
    private Task? _serving;

    private GrantCache? _ruleGrants;
    private ApprovalGate? _ruleGate;
    private ApproverListener? _ruleListener;
    private CancellationTokenSource? _ruleStop;
    private Task? _ruleServing;

    private string PipeName { get; } =
        "keypaste-hygiene-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

    private string RulePipeName { get; } =
        "keypaste-hygiene-rule-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

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

        // Inside the exposure and outside every rule: reachable by asking a person, never by a
        // standing one. This is the entry that makes "a rule cannot widen" a claim about the policy
        // check rather than about the exposure quietly doing all the work.
        _vault.AddEntry(new VaultEntry
        {
            GroupPath = "env/prod",
            Title = "ROOT_TOKEN",
            Password = SentinelOutsideTheRule,
        });

        _grants = new GrantCache(TimeProvider.System);
        _gate = new ApprovalGate(_human, TimeProvider.System, ApprovalLimits.Default);

        var handler = new ApproverHandler(
            new VaultCredentialSource(() => _vault),
            new VaultEntryNameLister(() => _vault),
            _gate,
            _grants,
            PolicyGate.None);

        _stop = new CancellationTokenSource();
        _listener = new ApproverListener(PipeName, handler);
        _serving = _listener.RunAsync(_stop.Token);

        // A second approver over the same vault, with a rule in force. Two listeners rather than one
        // whose policy can be changed per test, because a rule set that shifts under a running
        // approver is precisely what reading the file once rules out — a fixture that allowed it
        // would be modelling something the product does not do.
        Assert.True(Toml.TryParse(Policy, out var syntax, out var syntaxError), syntaxError);
        Assert.True(PolicyDocument.TryCreate(syntax, out var rules, out var ruleError), ruleError);

        _ruleGrants = new GrantCache(TimeProvider.System);
        _ruleGate = new ApprovalGate(_human, TimeProvider.System, ApprovalLimits.Default);
        _ruleStop = new CancellationTokenSource();
        _ruleListener = new ApproverListener(
            RulePipeName,
            new ApproverHandler(
                new VaultCredentialSource(() => _vault),
                new VaultEntryNameLister(() => _vault),
                _ruleGate,
                _ruleGrants,
                new PolicyGate(rules, TimeProvider.System)));

        _ruleServing = _ruleListener.RunAsync(_ruleStop.Token);

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var stop in new[] { _stop, _ruleStop })
        {
            if (stop is not null)
            {
                await stop.CancelAsync();
            }
        }

        foreach (var serving in new[] { _serving, _ruleServing })
        {
            if (serving is null)
            {
                continue;
            }

            try
            {
                await serving;
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
                // Tearing the listener down is how it stops.
            }
        }

        _listener?.Dispose();
        _ruleListener?.Dispose();
        _stop?.Dispose();
        _ruleStop?.Dispose();
        _gate?.Dispose();
        _ruleGate?.Dispose();
        _grants?.Dispose();
        _ruleGrants?.Dispose();
        _vault?.Dispose();

        Directory.Delete(_directory, recursive: true);
    }

    private async Task<(McpHarness Harness, McpClient Client)> StartAsync()
    {
        var harness = new McpHarness(PipeName);
        return (harness, await harness.StartAsync());
    }

    /// <summary>A bridge talking to the approver that has a standing rule in force.</summary>
    private async Task<(McpHarness Harness, McpClient Client)> StartPreapprovedAsync()
    {
        var harness = new McpHarness(RulePipeName);
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
    /// The second path that produces a credential, and the first in this product that does so with
    /// nobody watching. The requested field comes back, the other five sentinels do not, and no
    /// prompt was drawn.
    /// </summary>
    [Fact]
    public async Task OnAPolicyGrant_TheRequestedFieldComesBack_AndOnlyThat()
    {
        var (harness, client) = await StartPreapprovedAsync();

        await using (harness)
        {
            var result = await client.CallToolAsync(ToolText.CredentialToolName, Ask(), cancellationToken: Token);
            var text = TextOf(result);

            Assert.False(result.IsError, text);
            Assert.Contains(SentinelPassword, text, StringComparison.Ordinal);

            // Nobody was asked. Everything else in this class runs through a human who said yes;
            // this is the one where the count has to be zero.
            Assert.Equal(0, _human.Asked);

            foreach (var other in _everySentinel.Where(s => !string.Equals(s, SentinelPassword, StringComparison.Ordinal)))
            {
                AssertNowhere(harness, other, text, "a policy grant");
            }

            // The log records that it happened — which, with no human witness, is the only record
            // that it happened at all — and records it as a policy release, never as an approval.
            Assert.Contains("\"method\":\"policy\"", harness.AuditText, StringComparison.Ordinal);
            Assert.DoesNotContain(SentinelPassword, harness.AuditText, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A field the rule does not name is not covered by it, so a person is asked — and if they say
    /// no, nothing leaves. The rule covers <c>password</c>; this asks for the other three.
    /// </summary>
    [Theory]
    [InlineData("username", SentinelUsername)]
    [InlineData("url", SentinelUrl)]
    [InlineData("notes", SentinelNotes)]
    public async Task ARuleForAFieldTheAgentDidNotAskFor_ReleasesNothing(string field, string sentinel)
    {
        _human.Answer = ApprovalAnswer.Denied;

        var (harness, client) = await StartPreapprovedAsync();

        await using (harness)
        {
            var result = await client.CallToolAsync(
                ToolText.CredentialToolName, Ask(field: field), cancellationToken: Token);

            Assert.True(result.IsError);
            Assert.Equal(1, _human.Asked);

            foreach (var each in _everySentinel)
            {
                AssertNowhere(harness, each, TextOf(result), $"a rule that does not cover {field}");
            }

            Assert.NotEmpty(sentinel);
        }
    }

    /// <summary>
    /// An entry inside the bridge's exposure and outside the rule. This is the sentinel that makes
    /// "a rule cannot widen" a claim about the policy check: the exposure permits this entry, so
    /// nothing but the rule's own pattern is standing between the agent and it.
    /// </summary>
    [Fact]
    public async Task AnEntryInsideTheExposureAndOutsideTheRule_StillNeedsAPerson()
    {
        _human.Answer = ApprovalAnswer.Denied;

        var (harness, client) = await StartPreapprovedAsync();

        await using (harness)
        {
            var result = await client.CallToolAsync(
                ToolText.CredentialToolName, Ask(entry: "env/prod/ROOT_TOKEN"), cancellationToken: Token);

            Assert.True(result.IsError);
            Assert.Equal(1, _human.Asked);

            AssertNowhere(harness, SentinelOutsideTheRule, TextOf(result), "outside the rule");
        }
    }

    /// <summary>
    /// A rule reaching past <c>--expose</c>, asked for both ways the entry can be named. The rule
    /// here covers only <c>env/dev/**</c>, so this also proves a handle is not a way around it.
    /// </summary>
    [Fact]
    public async Task APolicyRuleForAnEntryOutsideTheExposure_YieldsNothingByEitherName()
    {
        var (harness, client) = await StartPreapprovedAsync();

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

            AssertNowhere(harness, SentinelOutOfScope, TextOf(byPath) + TextOf(byHandle), "out of scope, with a rule");

            // Not even asked about, exactly as without a policy: the exposure is checked first and
            // a rule is never a reason to put an unexposed entry name in front of somebody.
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
