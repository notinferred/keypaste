using System.Security.Cryptography;
using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Ipc;
using Keypaste.Core.Tests;
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

    /// <summary>An env-named custom field of the same entry, which may leave by name (C.5a).</summary>
    internal const string SentinelApiKey = "SENTINEL-OPENAI-API-KEY-a93d1e";

    /// <summary>A custom field of the same entry that is not env-named, so it never leaves.</summary>
    internal const string SentinelRecovery = "SENTINEL-RECOVERY-CODES-b04e2f";

    /// <summary>A secret in a part of the vault this server was never allowed to name.</summary>
    internal const string SentinelOutOfScope = "SENTINEL-OUT-OF-SCOPE-e51b70";

    /// <summary>
    /// A secret inside the exposure but in no project the agent is Automatic for, so it is reachable
    /// by asking a person and never without one.
    /// </summary>
    /// <remarks>
    /// This one is load-bearing, and it is why the count went from five to six. "Automatic did not
    /// widen" asserted against <see cref="SentinelOutOfScope"/> alone would be asserted against a
    /// value the exposure already blocks, so it would pass for a mode check that did nothing at all —
    /// exactly the vacuous-sentinel trap this class's own doc comment says the repository has fallen
    /// into before.
    /// </remarks>
    internal const string SentinelOutsideTheRow = "SENTINEL-OUTSIDE-THE-RULE-f62c81";

    private static readonly string[] _everySentinel =
    [
        SentinelPassword,
        SentinelUsername,
        SentinelUrl,
        SentinelNotes,
        SentinelApiKey,
        SentinelRecovery,
        SentinelOutOfScope,
        SentinelOutsideTheRow,
    ];

    /// <summary>
    /// What the agent may do through the first approver: see as far as the bridge exposes, and run
    /// commands. Kept under the label the <em>operator</em> gave the bridge, never the name the client
    /// asserts (THREATS.md T-3).
    /// </summary>
    private static AgentAccess Sees { get; } = AgentAccess.Defaults with { See = Everything(), Run = true };

    /// <summary>
    /// What the second approver holds for the same agent: Automatic for the billing project only, which
    /// the stripe entry is tagged into, so the root token is inside the exposure and outside the row.
    /// </summary>
    private static AgentAccess AutomaticForBilling { get; } = Sees with { Projects = [new ProjectAccess(Billing(), AgentMode.Automatic)] };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string _directory = string.Empty;
    private Vault? _vault;
    private OwnerLog? _log;
    private readonly ScriptedHuman _human = new();
    private GrantCache? _grants;
    private ApprovalGate? _gate;
    private ApproverListener? _listener;
    private CancellationTokenSource? _stop;
    private Task? _serving;

    private GrantCache? _automaticGrants;
    private ApprovalGate? _automaticGate;
    private ApproverListener? _automaticListener;
    private CancellationTokenSource? _automaticStop;
    private Task? _automaticServing;

    private readonly FakeAgentSettings _settings = new();
    private readonly FakeAgentSettings _automaticSettings = new();

    private string PipeName { get; } =
        "keypaste-hygiene-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

    private string AutomaticPipeName { get; } =
        "keypaste-hygiene-automatic-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

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

        Assert.True(_vault.SetFields(
            new EntryName("env/dev", "STRIPE_KEY"),
            [new FieldWrite("OPENAI_API_KEY", SentinelApiKey), new FieldWrite("Recovery codes", SentinelRecovery)]));

        // Outside the env/** exposure these tests run with, so nothing this server can be asked should ever
        // reach it. Planted somewhere it could genuinely leak, which is the whole point.
        _vault.AddEntry(new VaultEntry
        {
            GroupPath = "personal",
            Title = "bank",
            Password = SentinelOutOfScope,
        });

        // Inside the exposure and in no project the agent is Automatic for: reachable by asking a
        // person, never without one. This is the entry that makes "Automatic cannot widen" a claim
        // about the mode rather than about the exposure quietly doing all the work.
        _vault.AddEntry(new VaultEntry
        {
            GroupPath = "env/prod",
            Title = "ROOT_TOKEN",
            Password = SentinelOutsideTheRow,
        });

        Assert.True(_vault.AddTag(new EntryName("env/dev", "STRIPE_KEY"), "env:billing"));

        // What an agent is answered from is the vault as saved (D-0317).
        _vault.Save();

        _settings.Records[McpHarness.ClientLabel] = Sees;
        _automaticSettings.Records[McpHarness.ClientLabel] = AutomaticForBilling;

        _grants = new GrantCache(TimeProvider.System);
        _gate = new ApprovalGate(_human, TimeProvider.System, ApprovalLimits.Default);
        _log = new OwnerLog(Path.Combine(_directory, "home"));

        var handler = new ApproverHandler(
            new VaultCredentialSource(() => _vault),
            new VaultEntryNameLister(() => _vault),
            _gate,
            _grants,
            _settings);

        _stop = new CancellationTokenSource();
        _listener = new ApproverListener(PipeName, TestSession.Over(_vault, handler, _log.Log));
        _serving = _listener.RunAsync(_stop.Token);

        // A second approver over the same vault, where the agent is Automatic for billing. Two
        // listeners rather than one whose settings change per test, so neither test's answers depend
        // on what another left behind.

        _automaticGrants = new GrantCache(TimeProvider.System);
        _automaticGate = new ApprovalGate(_human, TimeProvider.System, ApprovalLimits.Default);
        _automaticStop = new CancellationTokenSource();
        _automaticListener = new ApproverListener(
            AutomaticPipeName,
            TestSession.Over(
                _vault,
                new ApproverHandler(
                    new VaultCredentialSource(() => _vault),
                    new VaultEntryNameLister(() => _vault),
                    _automaticGate,
                    _automaticGrants,
                    _automaticSettings),
                _log.Log));

        _automaticServing = _automaticListener.RunAsync(_automaticStop.Token);

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var stop in new[] { _stop, _automaticStop })
        {
            if (stop is not null)
            {
                await stop.CancelAsync();
            }
        }

        foreach (var serving in new[] { _serving, _automaticServing })
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
        _automaticListener?.Dispose();
        _stop?.Dispose();
        _automaticStop?.Dispose();
        _gate?.Dispose();
        _automaticGate?.Dispose();
        _grants?.Dispose();
        _automaticGrants?.Dispose();
        _vault?.Dispose();
        _log?.Dispose();

        Directory.Delete(_directory, recursive: true);
    }

    private async Task<(McpHarness Harness, McpClient Client)> StartAsync()
    {
        var harness = new McpHarness(PipeName, _vault!.Path);
        return (harness, await harness.StartAsync());
    }

    /// <summary>A bridge talking to the approver where the agent is Automatic for billing.</summary>
    private async Task<(McpHarness Harness, McpClient Client)> StartAutomaticAsync()
    {
        var harness = new McpHarness(AutomaticPipeName, _vault!.Path);
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
    /// in. Both audit logs and the raw transcript are searched as text, because that is what is
    /// actually on disk and on the wire.
    /// </summary>
    private void AssertNowhere(McpHarness harness, string sentinel, string result, string what)
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
            $"{what}: the bridge's audit log contains {sentinel}");

        Assert.False(
            _log!.Text().Contains(sentinel, StringComparison.Ordinal),
            $"{what}: the owner's audit log contains {sentinel}");
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
            var log = _log!.Text() + harness.AuditText;

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
    [InlineData("OPENAI_API_KEY", SentinelApiKey)]
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
    /// A custom field that is not env-named, KeePassXC's own attributes and a standard name in capitals
    /// are refused by the bridge before the owner is asked, in the words the plan fixes, and audited as
    /// an invalid field with no value anywhere.
    /// </summary>
    [Theory]
    [InlineData("Recovery codes")]
    [InlineData("otp")]
    [InlineData("KP2A_URL_1")]
    [InlineData("URL")]
    public async Task AFieldThatNeverLeaves_IsRefusedUnasked_AndAuditedAsInvalid(string field)
    {
        _human.Answer = ApprovalAnswer.Approved;

        var (harness, client) = await StartAsync();

        await using (harness)
        {
            var result = await client.CallToolAsync(ToolText.CredentialToolName, Ask(field: field), cancellationToken: Token);

            Assert.True(result.IsError);
            Assert.Equal(
                "keypaste: DENIED. The \"field\" argument must be password, username, url, notes or a custom field named like an environment variable. This call was recorded in the audit log.",
                TextOf(result));
            Assert.Equal(0, _human.Asked);

            using var line = System.Text.Json.JsonDocument.Parse(Assert.Single(harness.AuditLines()));
            Assert.Equal("invalid", line.RootElement.GetProperty("args").GetProperty("field").GetString());
            Assert.Equal("invalid-request", line.RootElement.GetProperty("method").GetString());

            foreach (var sentinel in _everySentinel)
            {
                AssertNowhere(harness, sentinel, TextOf(result), field);
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
    public async Task OnAnAutomaticRelease_TheRequestedFieldComesBack_AndOnlyThat()
    {
        var (harness, client) = await StartAutomaticAsync();

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
                AssertNowhere(harness, other, text, "an Automatic release");
            }

            // The owner's log records that it happened — which, with no human witness, is the only
            // record that it happened at all — and records it as Automatic, never as an approval.
            Assert.Contains("\"method\":\"automatic\"", _log!.Text(), StringComparison.Ordinal);
            Assert.DoesNotContain(SentinelPassword, _log.Text(), StringComparison.Ordinal);
            Assert.Empty(harness.AuditLines());
        }
    }

    /// <summary>Automatic covers the entry, and still releases only the one field asked for, whichever it is.</summary>
    [Theory]
    [InlineData("username", SentinelUsername)]
    [InlineData("url", SentinelUrl)]
    [InlineData("notes", SentinelNotes)]
    public async Task AnAutomaticRelease_IsOfTheOneFieldAsked(string field, string sentinel)
    {
        var (harness, client) = await StartAutomaticAsync();

        await using (harness)
        {
            var result = await client.CallToolAsync(
                ToolText.CredentialToolName, Ask(field: field), cancellationToken: Token);
            var text = TextOf(result);

            Assert.False(result.IsError, text);
            Assert.Contains(sentinel, text, StringComparison.Ordinal);
            Assert.Equal(0, _human.Asked);

            foreach (var each in _everySentinel.Where(each => !string.Equals(each, sentinel, StringComparison.Ordinal)))
            {
                AssertNowhere(harness, each, text, $"an Automatic release of {field}");
            }
        }
    }

    /// <summary>
    /// An entry inside the bridge's exposure and in no project the agent is Automatic for. This is the
    /// sentinel that makes "Automatic cannot widen" a claim about the mode: the exposure permits this
    /// entry, so nothing but the row's own project is standing between the agent and it.
    /// </summary>
    [Fact]
    public async Task AnEntryInsideTheExposureAndOutsideTheRow_StillNeedsAPerson()
    {
        _human.Answer = ApprovalAnswer.Denied;

        var (harness, client) = await StartAutomaticAsync();

        await using (harness)
        {
            var result = await client.CallToolAsync(
                ToolText.CredentialToolName, Ask(entry: "env/prod/ROOT_TOKEN"), cancellationToken: Token);

            Assert.True(result.IsError);
            Assert.Equal(1, _human.Asked);

            AssertNowhere(harness, SentinelOutsideTheRow, TextOf(result), "outside the row");
        }
    }

    /// <summary>
    /// Settings that see everything, for an entry outside <c>--expose</c>, asked for both ways the
    /// entry can be named: the bridge's exposure still bounds what the agent reaches.
    /// </summary>
    [Fact]
    public async Task AnAutomaticAgentAskingOutsideTheExposure_YieldsNothingByEitherName()
    {
        var (harness, client) = await StartAutomaticAsync();

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

            AssertNowhere(harness, SentinelOutOfScope, TextOf(byPath) + TextOf(byHandle), "out of scope, Automatic");

            // Not even asked about: what the agent can see is checked first, and a mode is never a
            // reason to put an unexposed entry name in front of somebody.
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
    /// <summary>
    /// A run, whatever the person answers: the injected value is the password sentinel, the child
    /// prints it in every form the scrubber knows, and it reaches neither the result, the wire nor the
    /// log. A reference outside the exposure starts nothing and names nothing.
    /// </summary>
    [Theory]
    [InlineData(ApprovalAnswer.ApprovedOnce)]
    [InlineData(ApprovalAnswer.Approved)]
    [InlineData(ApprovalAnswer.Denied)]
    [InlineData(ApprovalAnswer.TimedOut)]
    public async Task ARun_LeavesNoValueAnywhere_WhateverTheAnswer(ApprovalAnswer answer)
    {
        _human.Answer = answer;
        Keypaste.Core.Tests.ProjectVariables.Set(_vault!, "dev", "STRIPE_KEY", SentinelPassword);
        _vault!.Save();
        var pipe = "keypaste-hygiene-run-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        using var envGrants = new EnvGrantCache(TimeProvider.System);
        using var gate = new ApprovalGate(_human, TimeProvider.System, ApprovalLimits.Default);
        using var grants = new GrantCache(TimeProvider.System);
        using var lifetime = new Keypaste.Core.Ownership.SessionLifetime(TestSession.Id);
        var authority = new Keypaste.Core.Ownership.SessionAuthority(
            Keypaste.Core.Ownership.VaultIdentity.Of(Path.GetDirectoryName(_vault!.Path)!, _vault.Path),
            () => lifetime,
            new ApproverHandler(new VaultCredentialSource(() => _vault), new VaultEntryNameLister(() => _vault), gate, grants, _settings),
            audit: () => _log!.Log,
            new Keypaste.Core.Ownership.SessionEnvironments(gate, _ => _vault, TimeProvider.System, envGrants));
        using var stop = new CancellationTokenSource();
        using var listener = new ApproverListener(pipe, authority);
        var serving = listener.RunAsync(stop.Token);

        await using (var harness = new McpHarness(pipe, _vault.Path))
        {
            var client = await harness.StartAsync("--allow-run");
            var work = Directory.CreateDirectory(Path.Combine(_directory, "work")).FullName;

            var run = await client.CallToolAsync(
                ToolText.RunToolName,
                new Dictionary<string, object?>
                {
                    ["command"] = new[] { Keypaste.Core.Tests.Reporter.Path, "--stdout", "STRIPE_KEY", "--forms", "STRIPE_KEY", "--env" },
                    ["directory"] = work,
                    ["project"] = "dev",
                    ["reason"] = "print the key to prove it cannot be read back",
                },
                cancellationToken: Token);

            var outside = await client.CallToolAsync(
                ToolText.RunToolName,
                new Dictionary<string, object?>
                {
                    ["command"] = new[] { Keypaste.Core.Tests.Reporter.Path, "--stdout", "BANK" },
                    ["directory"] = work,
                    ["env"] = new Dictionary<string, string> { ["BANK"] = "kp:///personal/bank" },
                    ["reason"] = "read a bank password",
                },
                cancellationToken: Token);

            Assert.Equal(!answer.Releases(), run.IsError);
            Assert.True(outside.IsError);

            foreach (var sentinel in _everySentinel)
            {
                AssertNowhere(harness, sentinel, TextOf(run) + TextOf(outside), $"run answered {answer}");
            }
        }

        await stop.CancelAsync();

        try
        {
            await serving;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Tearing the listener down is how it stops.
        }
    }

    private static EntryExposure Everything()
    {
        Assert.True(EntryExposure.TryCreate(["**"], out var exposure, out var error), error);
        return exposure;
    }

    private static ProjectScope Billing()
    {
        Assert.True(ProjectScope.TryCreate("billing", null, out var scope, out var error), error);
        return scope;
    }

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

        public ValueTask<ApprovalAnswer> AskAsync(RunPrompt prompt, CancellationToken cancellationToken)
        {
            Asked++;
            return ValueTask.FromResult(Answer);
        }
    }
}
