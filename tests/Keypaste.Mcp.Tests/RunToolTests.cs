using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Keypaste.Core;
using Keypaste.Core.Activity;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Clients;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Keypaste.Core.Policy;
using Keypaste.Core.Tests;
using Keypaste.Mcp.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Keypaste.Mcp.Tests;

/// <summary>
/// The shipped <c>run</c> tool against a real vault's owner and a real child (D-0358): a person's
/// answer decides whether anything starts, the audit line is written first, the child gets the values
/// and nothing of the protocol, and what comes back holds no value in any form the scrubber knows.
/// </summary>
public sealed class RunToolTests : IDisposable
{
    /// <summary>The part of the injected value any printed form of it still holds.</summary>
    private const string _core = "SENTINEL-RUN-7f3a";

    /// <summary>A value holding every character the common dumps escape differently.</summary>
    private const string _value = "p\"a's\\" + _core + "$w\nnext-line-é%40end";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-run-tool-").FullName;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    private string WorkPath => Path.Combine(_directory, "work");

    public RunToolTests()
    {
        Directory.CreateDirectory(WorkPath);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private Dictionary<string, object?> Run(params string[] steps) => new()
    {
        ["command"] = new[] { Reporter.Path }.Concat(steps).ToArray(),
        ["directory"] = WorkPath,
        ["project"] = "acme-api",
        ["reason"] = "run the pending migration",
    };

    private static string TextOf(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private static JsonElement Structured(CallToolResult result) => Assert.IsType<JsonElement>(result.StructuredContent);

    private static string? Field(string line, string name)
    {
        using var parsed = JsonDocument.Parse(line);
        return parsed.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static void AssertNoValue(string text)
    {
        Assert.DoesNotContain(_core, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheValue_NeverAppearsInTheResult()
    {
        await using var owner = Owner.Start(this, ApprovalAnswer.ApprovedOnce);
        await using var harness = new McpHarness(owner.PipeName, VaultPath);
        var client = await harness.StartAsync("--expose", "env/**", "--allow-run");

        List<string> steps = ["--stdout", "DATABASE_URL", "--forms", "DATABASE_URL", "--env"];

        if (OperatingSystem.IsWindows())
        {
            steps.AddRange(["--utf16", "DATABASE_URL"]);
        }

        var result = await client.CallToolAsync(ToolText.RunToolName, Run([.. steps]), cancellationToken: Token);

        Assert.False(result.IsError, TextOf(result));
        Assert.Contains("[keypaste:DATABASE_URL]", TextOf(result), StringComparison.Ordinal);
        AssertNoValue(TextOf(result));
        AssertNoValue(harness.Transcript);
        AssertNoValue(harness.AuditText);
        Assert.Equal(0, Structured(result).GetProperty("exit_code").GetInt32());
        Assert.True(Structured(result).GetProperty("replacements").GetInt32() >= 10);
    }

    [Fact]
    public async Task ExitCodeAndBothStreams_ComeBack()
    {
        await using var owner = Owner.Start(this, ApprovalAnswer.ApprovedOnce);
        await using var harness = new McpHarness(owner.PipeName, VaultPath);
        var client = await harness.StartAsync("--expose", "env/**", "--allow-run");

        var result = await client.CallToolAsync(ToolText.RunToolName, Run("--print", "hello", "--stderr", "oops", "--exit", "3"), cancellationToken: Token);

        Assert.False(result.IsError);
        var structured = Structured(result);
        Assert.Equal(3, structured.GetProperty("exit_code").GetInt32());
        Assert.Equal("hello", structured.GetProperty("stdout").GetString()!.Trim());
        Assert.Equal("oops", structured.GetProperty("stderr").GetString()!.Trim());
        Assert.Equal(["DATABASE_URL", "TOKEN"], structured.GetProperty("injected").EnumerateArray().Select(name => name.GetString()));
        Assert.StartsWith("keypaste: RAN.", TextOf(result), StringComparison.Ordinal);
        Assert.Contains("A command can still reveal a value in another form", TextOf(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADeniedRun_StartsNothing()
    {
        await using var owner = Owner.Start(this, ApprovalAnswer.Denied);
        await using var harness = new McpHarness(owner.PipeName, VaultPath);
        var client = await harness.StartAsync("--expose", "env/**", "--allow-run");
        var marker = Path.Combine(_directory, "ran");

        var result = await client.CallToolAsync(ToolText.RunToolName, Run("--touch", marker), cancellationToken: Token);

        Assert.True(result.IsError);
        Assert.False(File.Exists(marker));
        Assert.Equal(1, owner.Asked);

        var line = Assert.Single(harness.AuditLines());
        Assert.Equal(("run", "denied", "prompt"), (Field(line, "tool"), Field(line, "decision"), Field(line, "method")));
    }

    [Fact]
    public async Task TheAuditLine_IsWrittenBeforeTheChildStarts_AndNamesWhatRan()
    {
        await using var owner = Owner.Start(this, ApprovalAnswer.ApprovedOnce);
        await using var harness = new McpHarness(owner.PipeName, VaultPath);
        var client = await harness.StartAsync("--expose", "env/**", "--allow-run");

        var result = await client.CallToolAsync(ToolText.RunToolName, Run("--has-line", harness.AuditPath, "\"tool\":\"run\""), cancellationToken: Token);

        Assert.Contains("audit=found", Structured(result).GetProperty("stdout").GetString(), StringComparison.Ordinal);

        var line = Assert.Single(harness.AuditLines());
        using var parsed = JsonDocument.Parse(line);
        var root = parsed.RootElement;
        Assert.Equal("granted", root.GetProperty("decision").GetString());
        Assert.Equal(["env/acme-api/DATABASE_URL", "env/acme-api/TOKEN"], root.GetProperty("entries").EnumerateArray().Select(entry => entry.GetString()));
        Assert.StartsWith(Reporter.Path, root.GetProperty("command").GetString(), StringComparison.Ordinal);
        Assert.Equal(64, root.GetProperty("command_sha256").GetString()!.Length);
        Assert.Equal(VaultIdentity.Of(KeypasteHome.Resolve(null), VaultPath).Key, root.GetProperty("vault").GetString());
        Assert.Equal(owner.Session, root.GetProperty("session").GetString());
    }

    [Fact]
    public async Task AnUnwritableAuditLog_StartsNothing()
    {
        await using var owner = Owner.Start(this, ApprovalAnswer.ApprovedOnce);
        await using var harness = new McpHarness(owner.PipeName, VaultPath);
        var client = await harness.StartAsync("--expose", "env/**", "--allow-run");
        var marker = Path.Combine(_directory, "ran");

        using (new FileStream(harness.AuditPath + ".lock", FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
        {
            var result = await client.CallToolAsync(ToolText.RunToolName, Run("--touch", marker), cancellationToken: Token);

            Assert.True(result.IsError);
            Assert.Equal(ToolText.AuditUnavailable, TextOf(result));
        }

        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task TheChild_ReadsNothingFromTheProtocolStream()
    {
        await using var owner = Owner.Start(this, ApprovalAnswer.ApprovedOnce);
        await using var harness = new McpHarness(owner.PipeName, VaultPath);
        var client = await harness.StartAsync("--expose", "env/**", "--allow-run");

        var result = await client.CallToolAsync(ToolText.RunToolName, Run("--read-stdin"), cancellationToken: Token);

        Assert.Equal("stdin=0", Structured(result).GetProperty("stdout").GetString()!.Trim());
    }

    [Fact]
    public async Task TheChild_HasTheValues_AndNoKeypasteVariable()
    {
        await using var owner = Owner.Start(this, ApprovalAnswer.ApprovedOnce);
        await using var harness = new McpHarness(owner.PipeName, VaultPath);
        var client = await harness.StartAsync("--expose", "env/**", "--allow-run");
        Environment.SetEnvironmentVariable("KEYPASTE_RUN_TOOL_PROBE", "inherited");

        try
        {
            var result = await client.CallToolAsync(ToolText.RunToolName, Run("--stdout", "TOKEN", "KEYPASTE_RUN_TOOL_PROBE"), cancellationToken: Token);

            var stdout = Structured(result).GetProperty("stdout").GetString()!.ReplaceLineEndings("\n");
            Assert.Contains("TOKEN=[keypaste:TOKEN]\n", stdout, StringComparison.Ordinal);
            Assert.Contains("KEYPASTE_RUN_TOOL_PROBE=\n", stdout, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("KEYPASTE_RUN_TOOL_PROBE", null);
        }
    }

    [Fact]
    public async Task AMissingOrRelativeDirectory_IsRefusedUnasked()
    {
        await using var owner = Owner.Start(this, ApprovalAnswer.ApprovedOnce);
        await using var harness = new McpHarness(owner.PipeName, VaultPath);
        var client = await harness.StartAsync("--expose", "env/**", "--allow-run");

        foreach (var directory in new[] { Path.Combine(_directory, "absent"), "relative/dir" })
        {
            var run = Run("--print", "x");
            run["directory"] = directory;

            var result = await client.CallToolAsync(ToolText.RunToolName, run, cancellationToken: Token);

            Assert.True(result.IsError);
            Assert.Contains("\"directory\" argument", TextOf(result), StringComparison.Ordinal);
        }

        Assert.Equal(0, owner.Asked);
        Assert.All(harness.AuditLines(), line => Assert.Equal("invalid-request", Field(line, "method")));
    }

    [Fact]
    public async Task ABareNameInTheBridgesDirectory_IsNotRun()
    {
        await using var owner = Owner.Start(this, ApprovalAnswer.ApprovedOnce);
        await using var harness = new McpHarness(owner.PipeName, VaultPath);
        var client = await harness.StartAsync("--expose", "env/**", "--allow-run");
        var run = Run();
        run["command"] = new[] { Path.GetFileNameWithoutExtension(Environment.ProcessPath)! };

        var result = await client.CallToolAsync(ToolText.RunToolName, run, cancellationToken: Token);

        Assert.True(result.IsError);
        Assert.Contains("\"command\" argument", TextOf(result), StringComparison.Ordinal);
        Assert.Equal(0, owner.Asked);
    }

    [Fact]
    public async Task AReferenceOutsideTheExposure_IsRefusedByTheBridge()
    {
        await using var owner = Owner.Start(this, ApprovalAnswer.ApprovedOnce);
        await using var harness = new McpHarness(owner.PipeName, VaultPath);
        var client = await harness.StartAsync("--expose", "env/**", "--allow-run");
        var run = Run("--print", "x");
        run.Remove("project");
        run["env"] = new Dictionary<string, string> { ["BANK"] = "kp:///personal/bank" };

        var result = await client.CallToolAsync(ToolText.RunToolName, run, cancellationToken: Token);

        Assert.True(result.IsError);
        Assert.Equal(ToolText.OutOfScope, TextOf(result));
        Assert.Equal(0, owner.Runs);
    }

    [Fact]
    public async Task Timeout_StopsTheChild_AndSaysSo()
    {
        await using var owner = Owner.Start(this, ApprovalAnswer.ApprovedOnce);
        await using var harness = new McpHarness(owner.PipeName, VaultPath);
        var client = await harness.StartAsync("--expose", "env/**", "--allow-run");
        var run = Run("--sleep", "60");
        run["timeout_seconds"] = 2;

        var result = await client.CallToolAsync(ToolText.RunToolName, run, cancellationToken: Token);

        Assert.False(result.IsError);
        Assert.Equal(JsonValueKind.Null, Structured(result).GetProperty("exit_code").ValueKind);
        Assert.True(Structured(result).GetProperty("timed_out").GetBoolean());
        Assert.Contains("timed out after 2 s and was stopped", TextOf(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClientCancel_KillsTheChild()
    {
        await using var owner = Owner.Start(this, ApprovalAnswer.ApprovedOnce);
        await using var harness = new McpHarness(owner.PipeName, VaultPath);
        var channels = harness.Serve("--expose", "env/**", "--allow-run");
        var pidFile = Path.Combine(_directory, "pid");
        using var reader = new StreamReader(channels.ClientReads);

        async Task Send(object message)
        {
            var line = JsonSerializer.SerializeToUtf8Bytes(message);
            await channels.ClientWrites.WriteAsync(line, Token);
            await channels.ClientWrites.WriteAsync("\n"u8.ToArray(), Token);
            await channels.ClientWrites.FlushAsync(Token);
        }

        // Written by hand, because the SDK's client never tells a server it gave up: a real client's
        // notifications/cancelled is what reaches the tool's token.
        await Send(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "cancel-probe", version = "1.0" } } });
        await reader.ReadLineAsync(Token);
        await Send(new { jsonrpc = "2.0", method = "notifications/initialized" });
        await Send(new { jsonrpc = "2.0", id = 9, method = "tools/call", @params = new { name = ToolText.RunToolName, arguments = Run("--pid-file", pidFile, "--sleep", "60") } });

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!File.Exists(pidFile) || new FileInfo(pidFile).Length == 0)
        {
            Assert.True(DateTime.UtcNow < deadline, "the child never started");
            await Task.Delay(50, Token);
        }

        await Send(new { jsonrpc = "2.0", method = "notifications/cancelled", @params = new { requestId = 9, reason = "the client gave up" } });

        var pid = int.Parse(File.ReadAllText(pidFile), CultureInfo.InvariantCulture);
        var gone = DateTime.UtcNow + TimeSpan.FromSeconds(15);

        while (true)
        {
            try
            {
                using var process = Process.GetProcessById(pid);

                if (process.HasExited)
                {
                    break;
                }
            }
            catch (ArgumentException)
            {
                break;
            }

            Assert.True(DateTime.UtcNow < gone, "the child outlived the cancelled call");
            await Task.Delay(100, Token);
        }
    }

    [Fact]
    public async Task ASecondConcurrentRun_IsBusy()
    {
        await using var owner = Owner.Start(this, ApprovalAnswer.ApprovedOnce);
        await using var harness = new McpHarness(owner.PipeName, VaultPath);
        var client = await harness.StartAsync("--expose", "env/**", "--allow-run");
        var pidFile = Path.Combine(_directory, "pid");

        var first = client.CallToolAsync(ToolText.RunToolName, Run("--pid-file", pidFile, "--sleep", "4"), cancellationToken: Token).AsTask();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!File.Exists(pidFile))
        {
            Assert.True(DateTime.UtcNow < deadline, "the first run never started");
            await Task.Delay(50, Token);
        }

        var second = await client.CallToolAsync(ToolText.RunToolName, Run("--print", "x"), cancellationToken: Token);

        Assert.True(second.IsError);
        Assert.Equal(ToolText.RunBusy, TextOf(second));
        Assert.False((await first).IsError);
    }

    [Fact]
    public async Task AnOwnerWithoutRun_IsRefused_NothingStarts()
    {
        await using var harness = new McpHarness();
        harness.Approver.Start();
        var client = await harness.StartAsync("--expose", "env/**", "--allow-run");
        var marker = Path.Combine(_directory, "ran");

        var result = await client.CallToolAsync(ToolText.RunToolName, Run("--touch", marker), cancellationToken: Token);

        Assert.True(result.IsError);
        Assert.False(File.Exists(marker));
        Assert.Equal("no-session", Field(Assert.Single(harness.AuditLines()), "method"));
    }

    [Fact]
    public async Task AReplyNamingOtherVariables_StartsNothing()
    {
        await using var harness = new McpHarness();
        harness.Approver.RunAnswer = _ => FakeApprover.RunReplyOf(EnvOutcome.Resolved, AuditMethod.Prompt, [("PATH", "/tmp/evil")]);
        harness.Approver.Start();
        var client = await harness.StartAsync("--expose", "env/**", "--allow-run");
        var marker = Path.Combine(_directory, "ran");

        var result = await client.CallToolAsync(ToolText.RunToolName, Run("--touch", marker), cancellationToken: Token);

        Assert.True(result.IsError);
        Assert.Equal(ToolText.RunMismatched, TextOf(result));
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task ALostRunReply_IsNotSentAgain()
    {
        await using var harness = new McpHarness();
        harness.Approver.DropRunOnce = () => { };
        harness.Approver.Start();
        var client = await harness.StartAsync("--expose", "env/**", "--allow-run");

        var result = await client.CallToolAsync(ToolText.RunToolName, Run("--print", "x"), cancellationToken: Token);

        Assert.True(result.IsError);
        Assert.Equal(ToolText.RunUnanswered, TextOf(result));
        Assert.Equal(1, harness.Approver.Runs);
    }

    [Fact]
    public async Task ALongRun_SendsProgress_WhenTheClientAsks()
    {
        await using var owner = Owner.Start(this, ApprovalAnswer.ApprovedOnce);
        await using var harness = new McpHarness(owner.PipeName, VaultPath);
        var client = await harness.StartAsync("--expose", "env/**", "--allow-run");
        var beats = 0;
        var progress = new Progress<ProgressNotificationValue>(_ => Interlocked.Increment(ref beats));

        var result = await client.CallToolAsync(ToolText.RunToolName, Run("--sleep", "12"), progress, cancellationToken: Token);

        Assert.False(result.IsError);
        Assert.True(Volatile.Read(ref beats) >= 1);
    }

    [Fact]
    public async Task NoAllowRun_OmitsTheTool()
    {
        await using var harness = new McpHarness();
        var client = await harness.StartAsync();

        var tools = await client.ListToolsAsync(cancellationToken: Token);

        Assert.Equal([ToolText.ListToolName, ToolText.CredentialToolName], tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheToolList_MarksRunDestructiveAndOpenWorld()
    {
        await using var harness = new McpHarness();
        var client = await harness.StartAsync("--allow-run");

        var run = Assert.Single(await client.ListToolsAsync(cancellationToken: Token), tool => tool.Name == ToolText.RunToolName);

        Assert.True(run.ProtocolTool.Annotations!.DestructiveHint);
        Assert.True(run.ProtocolTool.Annotations.OpenWorldHint);
        Assert.False(run.ProtocolTool.Annotations.ReadOnlyHint);
        Assert.False(run.ProtocolTool.Annotations.IdempotentHint);
    }

    [Fact]
    public async Task InjectOnly_RefusesRequestCredential_WithTheRunHint()
    {
        await using var owner = Owner.Start(this, ApprovalAnswer.Approved, clients: "[[client]]\nlabel = \"*\"\npolicy = \"inject-only\"\n");
        await using var harness = new McpHarness(owner.PipeName, VaultPath);
        var client = await harness.StartAsync("--expose", "env/**", "--allow-run");

        var result = await client.CallToolAsync(
            ToolText.CredentialToolName,
            new Dictionary<string, object?> { ["entry"] = "env/acme-api/TOKEN", ["field"] = "password", ["reason"] = "deploy", ["ttl_seconds"] = 60 },
            cancellationToken: Token);

        Assert.True(result.IsError);
        Assert.Equal(ToolText.InjectOnly, TextOf(result));
        Assert.Equal(0, owner.Asked);
        Assert.Equal("inject-only", Field(Assert.Single(harness.AuditLines()), "method"));
    }

    [Fact]
    public async Task EveryAttach_CarriesTheClientIdentity()
    {
        await using var harness = new McpHarness();
        harness.Approver.Start();
        var client = await harness.StartAsync("--expose", "env/**");

        await client.CallToolAsync(
            ToolText.CredentialToolName,
            new Dictionary<string, object?> { ["entry"] = "env/dev/STRIPE_KEY", ["field"] = "password", ["reason"] = "deploy", ["ttl_seconds"] = 60 },
            cancellationToken: Token);

        var attached = Assert.Single(harness.Approver.Attached);
        Assert.Equal(new AttachClient(McpHarness.ClientName, McpHarness.ClientVersion, McpHarness.ClientLabel), attached.Client);
    }

    [Fact]
    public async Task KeyOf_MatchesWhatTheBridgeWrites()
    {
        await using var owner = Owner.Start(this, ApprovalAnswer.ApprovedOnce);
        await using var harness = new McpHarness(owner.PipeName, VaultPath);
        var client = await harness.StartAsync("--expose", "env/**");

        await client.CallToolAsync(
            ToolText.CredentialToolName,
            new Dictionary<string, object?> { ["entry"] = "env/acme-api/TOKEN", ["field"] = "password", ["reason"] = "deploy", ["ttl_seconds"] = 60 },
            cancellationToken: Token);

        using var parsed = JsonDocument.Parse(Assert.Single(harness.AuditLines()));
        Assert.Equal(
            EntryActivity.KeyOf(new EntryName("env/acme-api", "TOKEN")),
            parsed.RootElement.GetProperty("args").GetProperty("entry").GetString());
    }

    /// <summary>A real owner of the test's vault, whose person answers every question the same way.</summary>
    private sealed class Owner : IAsyncDisposable, IApprovalChannel
    {
        private readonly Vault _vault;
        private readonly GrantCache _grants = new(TimeProvider.System);
        private readonly EnvGrantCache _envGrants = new(TimeProvider.System);
        private readonly ApprovalGate _gate;
        private readonly ApproverListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serving;
        private readonly SessionLifetime _lifetime = new();
        private readonly ApprovalAnswer _answer;
        private int _asked;
        private int _runs;

        private Owner(RunToolTests test, ApprovalAnswer answer, string? clients)
        {
            _answer = answer;
            _vault = Vault.Create(test.VaultPath, "correct horse battery staple");
            var store = new EnvStore(_vault);
            store.TrySet("acme-api", "DATABASE_URL", _value, out _);
            store.TrySet("acme-api", "TOKEN", "tok-" + _core, out _);
            _vault.AddEntry(new VaultEntry { GroupPath = "personal", Title = "bank", Password = "bank-" + _core });
            _vault.Save();

            _gate = new ApprovalGate(this, TimeProvider.System, ApprovalLimits.Default);

            ClientPolicySource? source = null;

            if (clients is not null)
            {
                var path = Path.Combine(test._directory, "clients.toml");
                File.WriteAllText(path, clients);
                source = new ClientPolicySource(path);
            }

            var handler = new ApproverHandler(
                new VaultCredentialSource(() => _vault),
                new VaultEntryNameLister(() => _vault),
                _gate,
                _grants,
                PolicyGate.None,
                clients: source);

            var authority = new SessionAuthority(
                VaultIdentity.Of(KeypasteHome.Resolve(null), test.VaultPath),
                () => _lifetime,
                handler,
                new SessionEnvironments(_gate, lifetime => ReferenceEquals(lifetime, _lifetime) ? _vault : null, TimeProvider.System, _envGrants));

            PipeName = "keypaste-run-tool-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
            _listener = new ApproverListener(PipeName, new Counting(authority, this));
            _serving = _listener.RunAsync(_stop.Token);
        }

        internal string PipeName { get; }

        internal string Session => _lifetime.Id;

        internal int Asked => Volatile.Read(ref _asked);

        internal int Runs => Volatile.Read(ref _runs);

        internal static Owner Start(RunToolTests test, ApprovalAnswer answer, string? clients = null) => new(test, answer, clients);

        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _asked);
            return ValueTask.FromResult(_answer);
        }

        public ValueTask<ApprovalAnswer> AskAsync(RunPrompt prompt, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _asked);
            return ValueTask.FromResult(_answer);
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
            _lifetime.Dispose();
            _gate.Dispose();
            _grants.Dispose();
            _envGrants.Dispose();
            _vault.Dispose();
        }

        /// <summary>Counts the runs that reach the owner, so a bridge's own refusal is seen to send nothing.</summary>
        private sealed class Counting(IApproverHandler inner, Owner owner) : IApproverHandler
        {
            public ValueTask<AttachReply> AttachAsync(AttachRequest request, string connectionId, CancellationToken cancellationToken) =>
                inner.AttachAsync(request, connectionId, cancellationToken);

            public ValueTask<NamesReply> ListAsync(NamesRequest request, string connectionId, CancellationToken cancellationToken) =>
                inner.ListAsync(request, connectionId, cancellationToken);

            public ValueTask<CredentialReply> RequestAsync(CredentialRequest request, string connectionId, CancellationToken cancellationToken) =>
                inner.RequestAsync(request, connectionId, cancellationToken);

            public ValueTask<EnvReply> ReleaseEnvAsync(EnvRequest request, string connectionId, CancellationToken cancellationToken) =>
                inner.ReleaseEnvAsync(request, connectionId, cancellationToken);

            public ValueTask<RunReply> ReleaseRunAsync(RunRequest request, string connectionId, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref owner._runs);
                return inner.ReleaseRunAsync(request, connectionId, cancellationToken);
            }

            public void Disconnected(string connectionId) => inner.Disconnected(connectionId);
        }
    }
}
