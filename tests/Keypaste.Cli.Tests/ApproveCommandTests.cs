using System.Security.Cryptography;
using System.Text.Json;
using Keypaste.Cli.Approval;
using Keypaste.Cli.Commands;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste approve</c> as a full owner: what it tells the person leaving it running, that it applies
/// each agent's settings, and that it saves a change a person confirms in its terminal, under its claim,
/// or says why it could not.
/// </summary>
public sealed class ApproveCommandTests : IDisposable
{
    private const string _master = "approve-command-master";
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(20);

    private readonly CliHarness _harness = new();

    public ApproveCommandTests() => _harness.SeedVault(_master, ("env/dev/TOKEN", "approve-command-value"));

    public void Dispose() => _harness.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Home => _harness.Environment[KeypasteHome.EnvironmentVariable];

    private static string Announced(string? retired, params string[] allowed)
    {
        using var stderr = new StringWriter();

        ApproveCommand.Announce(
            "/vaults/v.kdbx",
            "keypaste-agent-test",
            "test-session",
            ApprovalLimits.Default,
            retired,
            allowed,
            new AgentConsole(stderr, interactive: false));

        return stderr.ToString();
    }

    [Fact]
    public void WithNothingAllowed_ItSaysEveryValueWaitsForYou()
    {
        var text = Announced(null);

        Assert.Contains("keypaste: every value waits for you to say yes; agents see each item's URL, and its username unless Settings hides it. Press Ctrl+C to stop.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("without asking", text, StringComparison.Ordinal);
        Assert.DoesNotContain("policy.toml", text, StringComparison.Ordinal);
    }

    /// <summary>The one claim that stops being true once a category is allowed: each is named, and the closing line says only the others wait.</summary>
    [Fact]
    public void WithACategoryAllowed_ItNamesItAndStopsClaimingEveryValueWaits()
    {
        var text = Announced(null, "passwords", "project variables");

        Assert.Contains("keypaste: released to every agent without asking, by this computer's rules: passwords, project variables", text, StringComparison.Ordinal);
        Assert.Contains("keypaste: every other value waits for you to say yes", text, StringComparison.Ordinal);
    }

    /// <summary>A policy file left from before is named once, so a person who wrote rules learns they no longer apply.</summary>
    [Fact]
    public void ARetiredPolicyFile_IsNamedOnce()
    {
        var text = Announced("/home/you/.keypaste/policy.toml");

        Assert.Single(text.Split(Environment.NewLine), line => line.Contains("policy.toml", StringComparison.Ordinal));
        Assert.Contains("keypaste: /home/you/.keypaste/policy.toml is no longer read: agents' rules are kept on this computer. `keypaste clients` shows them.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePolicyFlagIsGone()
    {
        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("approve", "--policy", "rules.toml", "--vault", _harness.VaultPath));

        using var help = new CliHarness();
        help.Run("approve", "--help");
        Assert.DoesNotContain("--policy", help.Out, StringComparison.Ordinal);
        Assert.Contains("keypaste clients", help.Out, StringComparison.Ordinal);
    }

    /// <summary>Pairing is armed only by the person who starts it, with a name a pairing can have (D-0634).</summary>
    [Fact]
    public void PairBrowser_TakesAName_AndRefusesOneNoPairingCanHave()
    {
        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("approve", "--pair-browser", "work/laptop", "--vault", _harness.VaultPath));
        Assert.Contains("--pair-browser: the name cannot contain '/'", _harness.Err, StringComparison.Ordinal);

        using var help = new CliHarness();
        help.Run("approve", "--help");
        Assert.Contains("--pair-browser <name>", help.Out, StringComparison.Ordinal);
    }

    /// <summary>
    /// A terminal's edit to the vault <c>keypaste approve</c> holds is asked about in the approver's own terminal
    /// by name only, saved there under its claim once allowed, and recorded on its own line (D-0464).
    /// </summary>
    [Fact]
    public async Task ATerminalEditConfirmedInItsTerminal_IsSavedInTheVault()
    {
        await using var approver = await RunningApprover.StartAsync(_harness, _master, "y");
        using var terminal = Terminal(approver);

        terminal.Prompt.Enqueue("approve-command-new-value");
        terminal.AssertExit(CliApp.ExitSuccess, terminal.Run("set", "env/dev/TOKEN", "--vault", _harness.VaultPath));

        Assert.Contains("updated env/dev/TOKEN", terminal.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("Master password: ", terminal.Prompt.PromptsSeen);
        Assert.Contains("keypaste: a terminal asks to change this vault.", approver.Text, StringComparison.Ordinal);
        Assert.Contains("  command  keypaste set", approver.Text, StringComparison.Ordinal);
        Assert.Contains("  asked    asked from a terminal; any program you run could have typed it", approver.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("approve-command-new-value", approver.Text, StringComparison.Ordinal);

        await approver.StopAsync(terminal);

        using (var vault = Vault.Open(_harness.VaultPath, _master))
        {
            Assert.Equal("approve-command-new-value", vault.Find(new EntryName("env/dev", "TOKEN"))!.Password);
        }

        var line = Assert.Single(Lines(), line => line.GetProperty("tool").GetString() == AuditTools.Edit);
        Assert.Equal(("granted", "prompt"), (line.GetProperty("decision").GetString(), line.GetProperty("method").GetString()));
        Assert.DoesNotContain("approve-command-new-value", File.ReadAllText(KeypasteHome.AuditPath(Home)), StringComparison.Ordinal);
    }

    /// <summary>A terminal's edit the person denies at the approver's terminal writes nothing, and the terminal is told.</summary>
    [Fact]
    public async Task ATerminalEditDeniedInItsTerminal_ChangesNothing()
    {
        await using var approver = await RunningApprover.StartAsync(_harness, _master, "d");
        using var terminal = Terminal(approver);

        terminal.AssertExit(CliApp.ExitUsageError, terminal.Run("rm", "env/dev/TOKEN", "--yes", "--vault", _harness.VaultPath));

        Assert.Contains("keypaste rm: not saved: a person refused this change", terminal.Err, StringComparison.Ordinal);
        Assert.Contains("keypaste: denied. Nothing was written.", approver.Text, StringComparison.Ordinal);

        await approver.StopAsync(terminal);

        using var vault = Vault.Open(_harness.VaultPath, _master);
        Assert.NotNull(vault.Find(new EntryName("env/dev", "TOKEN")));
    }

    private CliHarness Terminal(RunningApprover approver)
    {
        var terminal = new CliHarness();
        terminal.Environment[KeypasteHome.EnvironmentVariable] = Home;
        terminal.Environment[ApproverEndpoint.EnvironmentVariable] = approver.Pipe;
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

    /// <summary><c>keypaste approve</c> on the harness's vault, run in this process on its own thread and pipe.</summary>
    private sealed class RunningApprover : IAsyncDisposable
    {
        private readonly StringWriter _stderr;
        private readonly TextWriter _synchronized;
        private readonly Task<int> _running;
        private readonly string _vault;
        private bool _stopped;

        private RunningApprover(string pipe, string vault, StringWriter stderr, TextWriter synchronized, Task<int> running)
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
        internal static async Task<RunningApprover> StartAsync(CliHarness harness, string master, params string[] answers)
        {
            var pipe = "keypaste-agent-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
            harness.Environment[ApproverEndpoint.EnvironmentVariable] = pipe;
            harness.Prompt.Interactive = false;
            harness.Prompt.Enqueue([master, .. answers]);

            var stderr = new StringWriter();
            var synchronized = TextWriter.Synchronized(stderr);
            var context = harness.NewContext(synchronized);
            var approver = new RunningApprover(pipe, harness.VaultPath, stderr, synchronized, Task.Run(() => CliApp.Run(["approve", "--vault", harness.VaultPath], context)));

            for (var waited = TimeSpan.Zero; waited < _wait && !approver.Text.Contains("listening on", StringComparison.Ordinal); waited += TimeSpan.FromMilliseconds(100))
            {
                Assert.False(approver._running.IsCompleted, $"keypaste approve stopped before it listened:{Environment.NewLine}{approver.Text}");
                await Task.Delay(TimeSpan.FromMilliseconds(100), Token);
            }

            Assert.Contains("listening on", approver.Text, StringComparison.Ordinal);
            return approver;
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
}
