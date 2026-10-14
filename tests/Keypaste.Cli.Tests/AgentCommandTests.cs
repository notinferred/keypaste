using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using Keypaste.Cli.Approval;
using Keypaste.Cli.Commands;
using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste agent</c> as a full owner: what it tells the person leaving it running, that it applies
/// each agent's settings, and that it saves a change a person confirms in its terminal, under its claim,
/// or says why it could not.
/// </summary>
public sealed class AgentCommandTests : IDisposable
{
    private const string _master = "agent-command-master";
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(20);

    private readonly CliHarness _harness = new();

    public AgentCommandTests() => _harness.SeedVault(_master, ("env/dev/TOKEN", "agent-command-value"));

    public void Dispose() => _harness.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Home => _harness.Environment[KeypasteHome.EnvironmentVariable];

    private static string Announced(string? retired, params string[] automatic)
    {
        using var stderr = new StringWriter();

        AgentCommand.Announce(
            "/vaults/v.kdbx",
            "keypaste-agent-test",
            "test-session",
            ApprovalLimits.Default,
            retired,
            automatic,
            new AgentConsole(stderr, interactive: false));

        return stderr.ToString();
    }

    [Fact]
    public void WithNothingAutomatic_ItSaysNothingIsReleasedWithoutYou()
    {
        var text = Announced(null);

        Assert.Contains("keypaste: nothing is released without you saying yes. Press Ctrl+C to stop.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Automatic", text, StringComparison.Ordinal);
        Assert.DoesNotContain("policy.toml", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one claim that stops being true once an agent is Automatic: each such agent is named with
    /// where, and the closing line says what is released without the person.
    /// </summary>
    [Fact]
    public void WithAnAgentAutomatic_ItNamesItAndStopsClaimingEverythingAsks()
    {
        var text = Announced(null, "claude-code (billing:dev)", "codex (by default)");

        Assert.Contains("keypaste: set to Automatic in the vault: claude-code (billing:dev); codex (by default)", text, StringComparison.Ordinal);
        Assert.Contains("except what an agent above is Automatic for", text, StringComparison.Ordinal);
        Assert.DoesNotContain("nothing is released without you saying yes. Press", text, StringComparison.Ordinal);
    }

    /// <summary>A policy file left from before is named once, so a person who wrote rules learns they no longer apply.</summary>
    [Fact]
    public void ARetiredPolicyFile_IsNamedOnce()
    {
        var text = Announced("/home/you/.keypaste/policy.toml");

        Assert.Single(text.Split(Environment.NewLine), line => line.Contains("policy.toml", StringComparison.Ordinal));
        Assert.Contains("keypaste: /home/you/.keypaste/policy.toml is no longer read: each agent's settings are kept in the vault. `keypaste mcp policy` shows them.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePolicyFlagIsGone()
    {
        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("agent", "--policy", "rules.toml", "--vault", _harness.VaultPath));

        using var usage = new StringWriter();
        AgentCommand.WriteUsage(usage);
        Assert.DoesNotContain("--policy", usage.ToString(), StringComparison.Ordinal);
        Assert.Contains("keypaste mcp policy", usage.ToString(), StringComparison.Ordinal);
    }

    /// <summary>No mode releases an entry in a protected profile: the person at the terminal is shown it, with no timed choice.</summary>
    [Fact]
    public async Task AProtectedProfileEntry_IsPromptedDespiteAutomatic()
    {
        var prompt = new FakeSecretPrompt();
        prompt.Enqueue("h");
        using var stderr = new StringWriter();
        var console = new AgentConsole(stderr, interactive: false);
        using var grants = new GrantCache(TimeProvider.System);
        using var gate = new ApprovalGate(
            new TerminalApprovalChannel(prompt, console, ApprovalLimits.Default.Window, TimeProvider.System),
            TimeProvider.System,
            ApprovalLimits.Default);
        var source = new ProdEntry();
        var settings = new FakeAgentSettings();
        settings.Records["claude-code"] = new AgentAccess { Mode = AgentMode.Automatic, See = Everything() };
        var handler = new ApproverHandler(source, source, gate, grants, settings);

        var reply = await handler.RequestAsync(
            new CredentialRequest
            {
                Entry = ProdEntry.Path,
                Field = "password",
                Reason = "rotate the production key",
                TtlSeconds = 300,
                Exposure = ["env/**"],
                ClientName = "claude-code",
                ClientLabel = "claude-code",
            },
            "conn-1",
            Token);

        Assert.Equal(AuditMethod.Prompt, reply.Method);
        Assert.Equal(AuditDecision.Denied, reply.Decision);
        Assert.Contains("this entry is in a protected profile: it is asked about every time.", stderr.ToString(), StringComparison.Ordinal);
        Assert.Equal("[d] deny  [o] once  45s › ", Assert.Single(prompt.PromptsSeen));
    }

    /// <summary>
    /// A change from a terminal that gives an agent more is confirmed in the agent's own terminal, then
    /// saved in the vault under its claim, where the next owner reads it; its line names the change.
    /// </summary>
    [Fact]
    public async Task AChangeConfirmedInItsTerminal_IsSavedInTheVault()
    {
        await using var agent = await RunningAgent.StartAsync(_harness, _master, "y");
        using var terminal = Terminal(agent);

        terminal.AssertExit(CliApp.ExitSuccess, terminal.Run("mcp", "policy", "claude-code", "--mode", "automatic", "--run", "on", "--vault", _harness.VaultPath));

        Assert.Contains("a person confirmed it (claude-code: mode automatic, run on)", terminal.Err, StringComparison.Ordinal);
        Assert.Contains("keypaste: a change would give an agent more.", agent.Text, StringComparison.Ordinal);
        Assert.Contains("by default: Automatic: released without asking, recorded in history", agent.Text, StringComparison.Ordinal);
        Assert.Contains("may run commands, each still asked about", agent.Text, StringComparison.Ordinal);
        Assert.Contains("  asked    asked from a terminal; any program you run could have typed it", agent.Text, StringComparison.Ordinal);

        await agent.StopAsync(terminal);

        using (var vault = Vault.Open(_harness.VaultPath, _master))
        {
            var saved = Assert.Single(new AgentAccessStore(vault).Read("claude-code").Own);
            Assert.Equal((AgentMode.Automatic, true), (saved.Access.Mode, saved.Access.Run));
        }

        var line = Assert.Single(Lines(), line => line.GetProperty("tool").GetString() == "access_change");
        Assert.Equal(("granted", "prompt"), (line.GetProperty("decision").GetString(), line.GetProperty("method").GetString()));
        Assert.Equal("claude-code", line.GetProperty("client").GetProperty("label").GetString());
    }

    /// <summary>A confirmed change the vault cannot hold, because another program wrote its file, is refused and said, and nothing is saved.</summary>
    [Fact]
    public async Task AConfirmedChangeItCannotSave_IsRefusedAndSaid()
    {
        await using var agent = await RunningAgent.StartAsync(_harness, _master, "y");
        using var terminal = Terminal(agent);

        using (var other = Vault.Open(_harness.VaultPath, _master))
        {
            other.AddEntry(new VaultEntry { GroupPath = "elsewhere", Title = "WRITTEN", Password = "by another program" });
            other.Save();
        }

        terminal.AssertExit(CliApp.ExitInternalError, terminal.Run("mcp", "policy", "claude-code", "--mode", "automatic", "--vault", _harness.VaultPath));

        Assert.Contains("not changed: a person confirmed it, and the vault was not saved: another program changed the vault file", terminal.Err, StringComparison.Ordinal);
        Assert.Contains("restart keypaste agent", terminal.Err, StringComparison.Ordinal);

        await agent.StopAsync(terminal);

        using var vault = Vault.Open(_harness.VaultPath, _master);
        Assert.Empty(new AgentAccessStore(vault).Read("claude-code").Own);
    }

    /// <summary>A narrowing from a terminal applies at once, with nobody asked: in this computer's ceilings and in the vault.</summary>
    [Fact]
    public async Task ANarrowing_AppliesAtOnce_WithNobodyAsked()
    {
        await using var agent = await RunningAgent.StartAsync(_harness, _master);
        using var terminal = Terminal(agent);

        terminal.AssertExit(CliApp.ExitSuccess, terminal.Run("mcp", "policy", "claude-code", "--mode", "ask", "--vault", _harness.VaultPath));

        Assert.Contains("applied at once, since it gives the agent nothing more", terminal.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("a change would give an agent more", agent.Text, StringComparison.Ordinal);
        Assert.True(AccessCaps.TryLoad(KeypasteHome.ClientsPath(Home), out var caps, out var problem), problem);
        Assert.Equal(AgentMode.Ask, Assert.Single(caps.Rows).Mode);

        await agent.StopAsync(terminal);

        using var vault = Vault.Open(_harness.VaultPath, _master);
        Assert.Equal(AgentMode.Ask, Assert.Single(new AgentAccessStore(vault).Read("claude-code").Own).Access.Mode);
    }

    private CliHarness Terminal(RunningAgent agent)
    {
        var terminal = new CliHarness();
        terminal.Environment[KeypasteHome.EnvironmentVariable] = Home;
        terminal.Environment[ApproverEndpoint.EnvironmentVariable] = agent.Pipe;
        return terminal;
    }

    private List<JsonElement> Lines()
    {
        return [.. File.ReadAllLines(KeypasteHome.AuditPath(Home)).Where(line => line.Length > 0).Select(Parse)];

        static JsonElement Parse(string line)
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }
    }

    private static EntryExposure Everything()
    {
        Assert.True(EntryExposure.TryCreate(["**"], out var exposure, out var error), error);
        return exposure;
    }

    /// <summary>A <c>keypaste agent</c> of the harness's vault, run in this process on its own thread and pipe.</summary>
    private sealed class RunningAgent : IAsyncDisposable
    {
        private readonly StringWriter _stderr;
        private readonly TextWriter _synchronized;
        private readonly Task<int> _running;
        private readonly string _vault;
        private bool _stopped;

        private RunningAgent(string pipe, string vault, StringWriter stderr, TextWriter synchronized, Task<int> running)
        {
            Pipe = pipe;
            _vault = vault;
            _stderr = stderr;
            _synchronized = synchronized;
            _running = running;
        }

        internal string Pipe { get; }

        /// <summary>What it has printed so far.</summary>
        internal string Text
        {
            get
            {
                lock (_synchronized)
                {
                    return _stderr.ToString();
                }
            }
        }

        /// <summary>Starts it with the master password and then each answer a person would type, and waits until it listens.</summary>
        internal static async Task<RunningAgent> StartAsync(CliHarness harness, string master, params string[] answers)
        {
            var pipe = "keypaste-agent-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
            harness.Environment[ApproverEndpoint.EnvironmentVariable] = pipe;
            harness.Prompt.Interactive = false;
            harness.Prompt.Enqueue([master, .. answers]);

            var stderr = new StringWriter();
            var synchronized = TextWriter.Synchronized(stderr);
            var context = harness.NewContext(synchronized);
            var agent = new RunningAgent(pipe, harness.VaultPath, stderr, synchronized, Task.Run(() => CliApp.Run(["agent", "--vault", harness.VaultPath], context)));

            for (var waited = TimeSpan.Zero; waited < _wait && !agent.Text.Contains("listening on", StringComparison.Ordinal); waited += TimeSpan.FromMilliseconds(100))
            {
                Assert.False(agent._running.IsCompleted, $"keypaste agent stopped before it listened:{Environment.NewLine}{agent.Text}");
                await Task.Delay(TimeSpan.FromMilliseconds(100), Token);
            }

            Assert.Contains("listening on", agent.Text, StringComparison.Ordinal);
            return agent;
        }

        /// <summary>Locks it from a terminal, as a person would, and waits for it to stop.</summary>
        internal async Task StopAsync(CliHarness terminal)
        {
            terminal.AssertExit(CliApp.ExitSuccess, terminal.Run("lock", "--vault", _vault));
            Assert.Equal(CliApp.ExitSuccess, await _running.WaitAsync(_wait, Token));
            _stopped = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_stopped)
            {
                try
                {
                    await _running.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
                }
                catch (TimeoutException)
                {
                    // A test that failed before stopping it leaves it to the process's end.
                }
            }

            _synchronized.Dispose();
        }
    }

    /// <summary>One entry tagged into a protected profile.</summary>
    private sealed class ProdEntry : ICredentialSource, IEntryNameLister
    {
        internal const string Path = "env/acme/prod/API_KEY";

        private static readonly EntryName _name = new("env/acme/prod", "API_KEY");

        private static readonly string[] _tags = ["env:acme:prod"];

        public bool TryResolve(string entryArgument, [NotNullWhen(true)] out EntryName? name, out CredentialFailure failure)
        {
            name = _name;
            failure = CredentialFailure.None;
            return true;
        }

        public bool TryRead(EntryName name, string field, [NotNullWhen(true)] out ReleasedField? value, out CredentialFailure failure)
        {
            value = new ReleasedField(field, "prod-sentinel");
            failure = CredentialFailure.None;
            return true;
        }

        public bool TryList(EntryExposure exposure, [NotNullWhen(true)] out IReadOnlyList<ListedEntry>? names, out CredentialFailure failure)
        {
            names = [new ListedEntry(_name, ["password"], _tags)];
            failure = CredentialFailure.None;
            return true;
        }

        public bool RequiresLiveApproval(EntryName name) => EnvProfileNames.RequiresLiveApproval(_tags);

        public IReadOnlyList<string> Tags(EntryName name) => _tags;
    }
}
