using System.Security.Cryptography;
using System.Text.Json;
using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Keypaste.Core.Tests;
using Keypaste.Mcp.Tools;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Keypaste.Mcp.Tests;

/// <summary>
/// <c>request_access_change</c>: an agent asks about its own settings and nobody else's, a bridge with no
/// name asks nobody, a malformed argument is named and never sent, an end and an hourly cap are sent as
/// bounds with the end read on this computer's clock and sent in UTC, what the agent reads says what the
/// owner decided, a refusal holding back its next request for more, and with nothing holding the vault
/// a request for less still lowers this computer's ceilings.
/// </summary>
public sealed class AccessChangeToolTests : IDisposable
{
    private const string _reason = "the deploy task runs the billing migration";

    private static readonly DateTimeOffset _now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-access-tool-").FullName;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static async Task<CallToolResult> AskAsync(McpClient client, Dictionary<string, object?> arguments) =>
        await client.CallToolAsync(ToolText.AccessToolName, arguments, cancellationToken: Token);

    private static Dictionary<string, object?> Change(string name, object? value) => new() { ["reason"] = _reason, [name] = value };

    private static string TextOf(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private static JsonElement Line(string text)
    {
        using var parsed = JsonDocument.Parse(text);
        return parsed.RootElement.Clone();
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString() : null;

    [Fact]
    public async Task TheTool_IsOfferedOnEveryBridge_AndTakesNoAgentsName()
    {
        await using var harness = new McpHarness();
        var client = await harness.StartAsync();

        var tool = Assert.Single(await client.ListToolsAsync(cancellationToken: Token), tool => tool.Name == ToolText.AccessToolName);
        var schema = tool.ProtocolTool.InputSchema;

        Assert.Equal(["mode", "per_hour", "project", "reason", "run", "see", "until", "values"], schema.GetProperty("properties").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal((1, AccessLimit.MaximumPerHour), (schema.GetProperty("properties").GetProperty("per_hour").GetProperty("minimum").GetInt32(), schema.GetProperty("properties").GetProperty("per_hour").GetProperty("maximum").GetInt32()));
        Assert.Contains("ask for the shortest time", tool.ProtocolTool.Description, StringComparison.Ordinal);
        Assert.Equal(["reason"], schema.GetProperty("required").EnumerateArray().Select(name => name.GetString()));
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.False(tool.ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.False(tool.ProtocolTool.Annotations.IdempotentHint);
    }

    /// <summary>The request names this bridge's own label whatever the agent says, so an agent can ask only about the settings that follow it.</summary>
    [Fact]
    public async Task AnAgentNamingAnotherAgent_StillAsksOnlyAboutItself()
    {
        await using var harness = new McpHarness();
        harness.Approver.AccessAnswer = _ => new AccessChangeReply(AuditDecision.Granted, AuditMethod.Narrowing, "applied at once on this computer");
        harness.Approver.Start();
        var client = await harness.StartAsync();

        var result = await AskAsync(client, new() { ["reason"] = _reason, ["mode"] = "ask", ["agent"] = "claude-code" });

        var asked = Assert.Single(harness.Approver.AccessReceived);
        Assert.Equal((McpHarness.ClientLabel, AccessOrigin.Agent, _reason, "ask"), (asked.Agent, asked.Origin, asked.Reason, asked.Mode));
        Assert.False(result.IsError);
        Assert.Equal(ToolText.AccessNarrowed, TextOf(result));
        Assert.Empty(harness.AuditLines());
    }

    /// <summary>Settings follow the label, so a bridge with none has no settings of its own: nobody is asked, and the bridge records it.</summary>
    [Fact]
    public async Task OnABridgeWithNoLabel_NobodyIsAsked()
    {
        await using var harness = new McpHarness { Label = null };
        harness.Approver.Start();
        var client = await harness.StartAsync();

        var result = await AskAsync(client, Change("run", "on"));

        Assert.True(result.IsError);
        Assert.Equal(ToolText.AccessUnnamed, TextOf(result));
        Assert.Equal(0, harness.Approver.Attaches);
        Assert.Empty(harness.Approver.AccessReceived);

        var line = Line(Assert.Single(harness.AuditLines()));
        Assert.Equal(("access_change", "denied", "invalid-request"), (Text(line, "tool"), Text(line, "decision"), Text(line, "method")));
        Assert.Null(Text(line.GetProperty("client"), "label"));
    }

    public static TheoryData<string, object?, string> Malformed => new()
    {
        { "reason", string.Empty, "reason" },
        { "mode", "always", "mode" },
        { "mode", 3, "mode" },
        { "project", "billing:Prod!", "project" },
        { "see", new[] { "env\\dev" }, "see" },
        { "see", "env/**", "see" },
        { "see", Enumerable.Repeat("env/**", EntryExposure.MaximumGlobs + 1).ToArray(), "see" },
        { "values", true, "values" },
        { "run", "yes", "run" },
        { "per_hour", 0, "per_hour" },
        { "per_hour", AccessLimit.MaximumPerHour + 1, "per_hour" },
        { "per_hour", "20", "per_hour" },
        { "per_hour", 2.5, "per_hour" },
        { "until", "none", "until" },
        { "until", "tomorrow", "until" },
        { "until", "2026-07-26 13:00", "until" },
        { "until", 20261231, "until" },
    };

    /// <summary>A malformed argument is named with its rule, the value it was given is never repeated, and nothing reaches the owner.</summary>
    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task AMalformedArgument_IsNamed_NeverEchoed_AndNeverSent(string name, object? value, string named)
    {
        await using var harness = new McpHarness { Clock = new ManualClock(_now) { Zone = TestZones.PlusTwo } };
        harness.Approver.Start();
        var client = await harness.StartAsync();

        var result = await AskAsync(client, Change(name, value));

        Assert.True(result.IsError);
        Assert.StartsWith($"keypaste: DENIED. The \"{named}\" argument ", TextOf(result), StringComparison.Ordinal);
        Assert.DoesNotContain("Prod!", TextOf(result), StringComparison.Ordinal);
        Assert.DoesNotContain("env\\dev", TextOf(result), StringComparison.Ordinal);
        Assert.DoesNotContain("tomorrow", TextOf(result), StringComparison.Ordinal);
        Assert.Empty(harness.Approver.AccessReceived);
        Assert.Equal("invalid-request", Text(Line(Assert.Single(harness.AuditLines())), "method"));
    }

    /// <summary>
    /// An end and an hourly cap are sent as the owner reads them: the end, a time with no offset read on
    /// this computer's clock, in UTC, and a date at the close of that day here.
    /// </summary>
    [Theory]
    [InlineData("2026-07-26 18:00", "2026-07-26T16:00:00Z")]
    [InlineData("2026-07-26T18:00:00Z", "2026-07-26T18:00:00Z")]
    [InlineData("2026-07-27", "2026-07-27T21:59:59Z")]
    public async Task AnEndAndAnHourlyCap_AreSentAsBounds_TheEndInUtc(string until, string sent)
    {
        await using var harness = new McpHarness { Clock = new ManualClock(_now) { Zone = TestZones.PlusTwo } };
        harness.Approver.AccessAnswer = _ => new AccessChangeReply(AuditDecision.Granted, AuditMethod.Prompt, "a person confirmed it");
        harness.Approver.Start();
        var client = await harness.StartAsync();

        var result = await AskAsync(client, new() { ["reason"] = _reason, ["project"] = "billing:dev", ["mode"] = "automatic", ["until"] = until, ["per_hour"] = 20 });

        Assert.Equal(ToolText.AccessConfirmed, TextOf(result));
        var asked = Assert.Single(harness.Approver.AccessReceived);
        Assert.Equal(("billing:dev", "automatic", "20", sent), (asked.Project, asked.Mode, asked.PerHour, asked.Until));
    }

    [Fact]
    public async Task AProjectWithWhatAppliesToTheWholeAgent_OrNothingToChange_IsRefusedUnsent()
    {
        await using var harness = new McpHarness();
        harness.Approver.Start();
        var client = await harness.StartAsync();

        var row = await AskAsync(client, new() { ["reason"] = _reason, ["project"] = "billing", ["run"] = "on" });
        var nothing = await AskAsync(client, new() { ["reason"] = _reason });

        Assert.Equal(ToolText.Invalid("project", AccessChangeRules.ProjectRowRule), TextOf(row));
        Assert.Equal(ToolText.AccessNothing, TextOf(nothing));
        Assert.Empty(harness.Approver.AccessReceived);
        Assert.Equal(2, harness.AuditLines().Length);
    }

    /// <summary>
    /// With nothing holding the vault, a bridge whose log is not its home's and that was given no
    /// <c>--clients</c> cannot tell which ceilings the owner reads, so it lowers nothing even for less,
    /// records the request, naming the change, and tells the agent that running setup again fixes it.
    /// </summary>
    [Theory]
    [InlineData("mode", "automatic")]
    [InlineData("values", "no")]
    public async Task WithNoOwner_ABridgeThatCannotTellWhichCeilingsApply_RecordsTheRequest_AndSaysHowToFixIt(string name, string value)
    {
        await using var harness = new McpHarness();
        var client = await harness.StartAsync();

        var result = await AskAsync(client, Change(name, value));

        Assert.True(result.IsError);
        Assert.Equal(ToolText.AccessCeilingsUnknown, TextOf(result));
        Assert.False(File.Exists(harness.ClientsPath));

        var line = Line(Assert.Single(harness.AuditLines()));
        Assert.Equal(("access_change", "denied", "no-approver"), (Text(line, "tool"), Text(line, "decision"), Text(line, "method")));
        Assert.Equal(McpHarness.ClientLabel, Text(line.GetProperty("client"), "label"));
        Assert.Contains("it was given no --clients", Text(line, "reason"), StringComparison.Ordinal);
        Assert.EndsWith($"({McpHarness.ClientLabel}: {name} {value})", Text(line, "reason"), StringComparison.Ordinal);
        Assert.Equal(_reason, Text(line.GetProperty("args"), "reason_excerpt"));
    }

    /// <summary>
    /// A bridge setup registered for another home, its log and its ceilings both pinned, lowers that
    /// home's ceilings for an agent's request for less while nothing holds the vault, and still refuses
    /// one for more, saying how to get an owner.
    /// </summary>
    [Fact]
    public async Task WithNoOwner_ABridgePinnedToAHomesCeilings_LowersThem_AndRefusesMore()
    {
        await using var harness = new McpHarness();
        var client = await harness.StartAsync("--clients", harness.ClientsPath);

        var widened = await AskAsync(client, Change("mode", "automatic"));
        var narrowed = await AskAsync(client, Change("values", "no"));

        Assert.Equal(ToolText.AccessNoOwner, TextOf(widened));
        Assert.Equal(ToolText.AccessNarrowed, TextOf(narrowed));
        Assert.True(AccessCaps.TryLoad(harness.ClientsPath, out var caps, out var problem), problem);
        var ceiling = Assert.Single(caps.Rows);
        Assert.Equal((McpHarness.ClientLabel, false), (ceiling.Label, ceiling.Values));
    }

    /// <summary>
    /// With nothing holding the vault, an agent's request for less, an end to Automatic included, lowers
    /// this computer's ceilings, as <c>keypaste mcp policy</c> would, and the bridge records it; one for
    /// more is still refused and lowers nothing.
    /// </summary>
    [Fact]
    public async Task WithNoOwner_ARequestForLess_LowersThisComputersCeilings_AndOneForMoreIsRefused()
    {
        await using var harness = new McpHarness { InItsOwnHome = true };
        var client = await harness.StartAsync();

        var widened = await AskAsync(client, Change("mode", "automatic"));

        Assert.Equal(ToolText.AccessNoOwner, TextOf(widened));
        Assert.False(File.Exists(harness.ClientsPath));

        var narrowed = await AskAsync(client, Change("values", "no"));

        Assert.False(narrowed.IsError);
        Assert.Equal(ToolText.AccessNarrowed, TextOf(narrowed));
        Assert.True(AccessCaps.TryLoad(harness.ClientsPath, out var caps, out var problem), problem);
        var ceiling = Assert.Single(caps.Rows);
        Assert.Equal((McpHarness.ClientLabel, false), (ceiling.Label, ceiling.Values));

        var ended = await AskAsync(client, Change("until", "2099-12-31T18:00:00+00:00"));

        Assert.Equal(ToolText.AccessNarrowed, TextOf(ended));
        Assert.True(AccessCaps.TryLoad(harness.ClientsPath, out caps, out problem), problem);
        ceiling = Assert.Single(caps.Rows);
        Assert.Equal((false, new AccessLimit { Until = new DateTimeOffset(2099, 12, 31, 18, 0, 0, TimeSpan.Zero) }), (ceiling.Values, ceiling.Limit));

        var lines = harness.AuditLines().Select(Line).ToList();
        Assert.Equal([("denied", "no-approver"), ("granted", "narrowing"), ("granted", "narrowing")], lines.Select(line => (Text(line, "decision"), Text(line, "method"))));
        Assert.Equal(McpHarness.ClientLabel, Text(lines[1].GetProperty("client"), "label"));
        Assert.EndsWith($"({McpHarness.ClientLabel}: values no)", Text(lines[1], "reason"), StringComparison.Ordinal);
    }

    /// <summary>A change the owner says it made but could not record is recorded here as what the owner said, and the agent is told what happened.</summary>
    [Fact]
    public async Task AChangeItsOwnerDidNotRecord_IsRecordedHere_AsTheOwnerSaid()
    {
        await using var harness = new McpHarness();
        harness.Approver.Records = false;
        harness.Approver.AccessAnswer = _ => new AccessChangeReply(AuditDecision.Granted, AuditMethod.Narrowing, "applied at once on this computer");
        harness.Approver.Start();
        var client = await harness.StartAsync();

        var result = await AskAsync(client, Change("values", "no"));

        Assert.Equal(ToolText.AccessNarrowed, TextOf(result));
        var line = Line(Assert.Single(harness.AuditLines()));
        Assert.Equal(("granted", "narrowing"), (Text(line, "decision"), Text(line, "method")));
        Assert.EndsWith(McpAudit.Unrecorded, Text(line, "reason"), StringComparison.Ordinal);
    }

    /// <summary>
    /// A narrowing made with nothing holding the vault whose line cannot be written still holds, and the
    /// agent is told it was made and is not recorded, never that it was refused (THREATS.md T-6).
    /// </summary>
    [Fact]
    public async Task ANarrowingMadeButNotRecorded_HoldsAndIsToldAsMade()
    {
        await using var harness = new McpHarness { InItsOwnHome = true };
        var client = await harness.StartAsync();
        CallToolResult narrowed;

        using (new FileStream(harness.AuditPath + ".lock", FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
        {
            narrowed = await AskAsync(client, Change("values", "no"));
        }

        Assert.False(narrowed.IsError);
        Assert.Equal(ToolText.AccessNarrowedUnrecorded, TextOf(narrowed));
        Assert.True(AccessCaps.TryLoad(harness.ClientsPath, out var caps, out var problem), problem);
        Assert.False(Assert.Single(caps.Rows).Values);
        Assert.Empty(harness.AuditLines());
    }

    /// <summary>A change a person confirmed that neither its owner nor the bridge could record is told as made, and as unrecorded.</summary>
    [Fact]
    public async Task AConfirmedChangeNobodyCouldRecord_IsToldAsMade_AndUnrecorded()
    {
        await using var harness = new McpHarness();
        harness.Approver.Records = false;
        harness.Approver.AccessAnswer = _ => new AccessChangeReply(AuditDecision.Granted, AuditMethod.Prompt, "a person confirmed it");
        harness.Approver.Start();
        var client = await harness.StartAsync();
        CallToolResult confirmed;

        using (new FileStream(harness.AuditPath + ".lock", FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
        {
            confirmed = await AskAsync(client, Change("run", "on"));
        }

        Assert.False(confirmed.IsError);
        Assert.Equal(ToolText.AccessConfirmedUnrecorded, TextOf(confirmed));
    }

    /// <summary>
    /// Through a real owner: a widening is put to the person as this agent's own request with its reason,
    /// a refusal holds back the agent's next request for more without asking, a narrowing still applies at
    /// once, and the owner records each on its own line under the bridge's client.
    /// </summary>
    [Fact]
    public async Task ThroughTheOwner_AWideningWaitsForThePerson_ARefusalHoldsTheNextBack_AndANarrowingApplies()
    {
        await using var owner = new Owner(this);
        await using var harness = new McpHarness(owner.PipeName, VaultPath);
        var client = await harness.StartAsync();

        owner.Answer = ApprovalAnswer.ApprovedOnce;
        var confirmed = await AskAsync(client, Change("run", "on"));

        Assert.False(confirmed.IsError);
        Assert.Equal(ToolText.AccessConfirmed, TextOf(confirmed));
        var prompt = Assert.Single(owner.Prompts);
        Assert.Equal((McpHarness.ClientLabel, AccessOrigin.Agent, _reason), (prompt.Agent, prompt.Origin, prompt.Reason));
        Assert.True(owner.Settings.Records[McpHarness.ClientLabel].Run);

        owner.Answer = ApprovalAnswer.Denied;
        var refused = await AskAsync(client, Change("mode", "automatic"));
        var cooled = await AskAsync(client, new() { ["reason"] = "another reason", ["project"] = "billing", ["mode"] = "automatic" });
        var narrowed = await AskAsync(client, Change("values", "no"));

        Assert.Equal((ToolText.AccessDenied, ToolText.AccessCooldown), (TextOf(refused), TextOf(cooled)));
        Assert.Equal(ToolText.AccessNarrowed, TextOf(narrowed));
        Assert.Equal(2, owner.Prompts.Count);
        Assert.Equal(AgentMode.AllowForAWhile, owner.Settings.Records[McpHarness.ClientLabel].Mode);
        Assert.False(Assert.Single(owner.Settings.Caps.Rows).Values);
        Assert.Single(owner.Settings.Written);

        var lines = owner.Log.Lines();
        Assert.Equal(["prompt", "prompt", "cooldown", "narrowing"], lines.Select(line => Text(line, "method")));
        Assert.All(lines, line =>
        {
            Assert.Equal("access_change", Text(line, "tool"));
            Assert.Equal((McpHarness.ClientName, McpHarness.ClientLabel), (Text(line.GetProperty("client"), "name"), Text(line.GetProperty("client"), "label")));
        });
        Assert.Empty(harness.AuditLines());
    }

    /// <summary>
    /// Through a real owner, an agent's request for Automatic until a time, at most so many an hour, puts
    /// that limit in front of the person, its end on this computer's clock with its offset, and once
    /// confirmed the record holds the end in UTC.
    /// </summary>
    [Fact]
    public async Task ThroughTheOwner_TheConfirmationShowsTheLimitAsked_AndTheRecordHoldsItInUtc()
    {
        var clock = new ManualClock(_now) { Zone = TestZones.PlusTwo };
        await using var owner = new Owner(this, clock);
        await using var harness = new McpHarness(owner.PipeName, VaultPath) { Clock = clock };
        var client = await harness.StartAsync();

        owner.Answer = ApprovalAnswer.ApprovedOnce;
        var confirmed = await AskAsync(client, new() { ["reason"] = _reason, ["mode"] = "automatic", ["until"] = "2026-07-26 18:00", ["per_hour"] = 20 });

        Assert.Equal(ToolText.AccessConfirmed, TextOf(confirmed));
        Assert.Equal(
            ["by default: Automatic until 2026-07-26 18:00 +02:00, at most 20 an hour: released without asking, recorded in history"],
            Assert.Single(owner.Prompts).Gains);
        var saved = owner.Settings.Records[McpHarness.ClientLabel];
        Assert.Equal(AgentMode.Automatic, saved.Mode);
        Assert.Equal(new AccessLimit { PerHour = 20, Until = new DateTimeOffset(2026, 7, 26, 16, 0, 0, TimeSpan.Zero) }, saved.Limit);
        Assert.EndsWith($"({McpHarness.ClientLabel}: mode automatic, per-hour 20, until 2026-07-26T16:00:00Z)", Text(Assert.Single(owner.Log.Lines()), "reason"), StringComparison.Ordinal);
    }

    /// <summary>A real owner over the test's directory: settings in memory, a person who answers as told, and its own log.</summary>
    private sealed class Owner : IAsyncDisposable, IApprovalChannel
    {
        private readonly SessionLifetime _lifetime = new("access-tool-session");
        private readonly GrantCache _grants = new(TimeProvider.System);
        private readonly ApprovalGate _gate;
        private readonly ApproverListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serving;

        internal Owner(AccessChangeToolTests test, TimeProvider? clock = null)
        {
            Log = new OwnerLog(Path.Combine(test._directory, "home"));
            _gate = new ApprovalGate(this, TimeProvider.System, ApprovalLimits.Default);

            var handler = new ApproverHandler(
                new VaultCredentialSource(() => null),
                new VaultEntryNameLister(() => null),
                _gate,
                _grants,
                Settings,
                clock: clock);
            var authority = new SessionAuthority(VaultIdentity.Of(test._directory, test.VaultPath), () => _lifetime, handler, () => Log.Log);

            PipeName = "keypaste-access-tool-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
            _listener = new ApproverListener(PipeName, authority);
            _serving = _listener.RunAsync(_stop.Token);
        }

        internal string PipeName { get; }

        internal OwnerLog Log { get; }

        internal FakeAgentSettings Settings { get; } = new();

        internal ApprovalAnswer Answer { get; set; } = ApprovalAnswer.Denied;

        internal List<AccessChangePrompt> Prompts { get; } = [];

        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ApprovalAnswer.Denied);

        public ValueTask<ApprovalAnswer> AskAsync(AccessChangePrompt prompt, CancellationToken cancellationToken)
        {
            Prompts.Add(prompt);
            return ValueTask.FromResult(Answer);
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();

            try
            {
                await _serving;
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
                // Tearing the listener down is how it stops.
            }

            _listener.Dispose();
            _stop.Dispose();
            _gate.Dispose();
            _grants.Dispose();
            _lifetime.Dispose();
            Log.Dispose();
        }
    }
}
