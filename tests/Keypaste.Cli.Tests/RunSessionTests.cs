using System.Security.Cryptography;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Keypaste.Core.Policy;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste run --session</c> against a real owner on a real pipe: the set comes only from the
/// process holding the vault, after the person there approves, and no refusal ever falls back to
/// opening the vault or asking for its password (E.1c).
/// </summary>
public sealed class RunSessionTests : IDisposable
{
    private const string _master = "run-session-master-pw";
    private const string _value = "sk_live_run_session_sentinel";

    private readonly CliHarness _harness = new();
    private readonly string _pipe = "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

    public RunSessionTests()
    {
        _harness.SeedVault(_master, ("env/dev/TOKEN", _value), ("env/broken/BAD-NAME", _value));
        _harness.Prompt.PromptsSeen.Clear();
        _harness.Environment[ApproverEndpoint.EnvironmentVariable] = _pipe;
        _harness.Environment[KeypasteHome.EnvironmentVariable] = _harness.Directory;
    }

    public void Dispose() => _harness.Dispose();

    private int Run(params string[] project) =>
        _harness.Run(["run", "--session", "--vault", _harness.VaultPath, .. project, "--", "deploy", "--to", "staging"]);

    [Fact]
    public async Task An_approved_run_starts_the_child_with_the_set_and_asks_no_password()
    {
        await using var owner = Owner.Start(this, ApprovalAnswer.Approved);

        _harness.AssertExit(0, Run("dev"));

        Assert.Equal(_value, _harness.ProcessLauncher.Environment["TOKEN"]);
        Assert.Equal("deploy", _harness.ProcessLauncher.Started[^1].FileName);
        Assert.Empty(_harness.Prompt.PromptsSeen);
        Assert.DoesNotContain(_value, _harness.Out + _harness.Err, StringComparison.Ordinal);
        Assert.Contains("answer in its prompt", _harness.Err, StringComparison.Ordinal);

        var asked = Assert.IsType<EnvReleasePrompt>(owner.Channel.Last);
        Assert.Equal("deploy --to staging", asked.Command);
        Assert.Equal(Environment.CurrentDirectory, asked.Directory);
        Assert.Equal(["TOKEN"], asked.Keys);
    }

    [Theory]
    [InlineData(ApprovalAnswer.Denied, "said no")]
    [InlineData(ApprovalAnswer.TimedOut, "in time")]
    [InlineData(ApprovalAnswer.Failed, "could not be shown")]
    public async Task A_refused_run_starts_nothing_and_says_why(ApprovalAnswer answer, string why)
    {
        await using var owner = Owner.Start(this, answer);

        _harness.AssertExit(CliApp.ExitInternalError, Run("dev"));

        Assert.Empty(_harness.ProcessLauncher.Started);
        Assert.Empty(_harness.Prompt.PromptsSeen);
        Assert.Contains(why, _harness.Err, StringComparison.Ordinal);
        Assert.Contains("nothing was started", _harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain(_value, _harness.Out + _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void With_nothing_holding_the_vault_the_run_says_so_and_asks_no_password()
    {
        _harness.AssertExit(CliApp.ExitInternalError, Run("dev"));

        Assert.Empty(_harness.ProcessLauncher.Started);
        Assert.Empty(_harness.Prompt.PromptsSeen);
        Assert.Contains("nothing holds", _harness.Err, StringComparison.Ordinal);
        Assert.Contains("keypaste agent", _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_locked_owner_refuses_and_nobody_is_asked()
    {
        await using var owner = Owner.Start(this, ApprovalAnswer.Approved);
        owner.Lifetime.End();

        _harness.AssertExit(CliApp.ExitInternalError, Run("dev"));

        Assert.Empty(_harness.ProcessLauncher.Started);
        Assert.Empty(_harness.Prompt.PromptsSeen);
        Assert.Contains("locked", _harness.Err, StringComparison.Ordinal);
        Assert.Null(owner.Channel.Last);
    }

    [Fact]
    public async Task An_unknown_project_exits_3_and_an_unusable_set_names_its_entries_unasked()
    {
        await using var owner = Owner.Start(this, ApprovalAnswer.Approved);

        _harness.AssertExit(CliApp.ExitNotFound, Run("missing"));
        _harness.AssertExit(CliApp.ExitInternalError, Run("broken"));

        Assert.Contains("no env set for 'missing'", _harness.Err, StringComparison.Ordinal);
        Assert.Contains("BAD-NAME", _harness.Err, StringComparison.Ordinal);
        Assert.Empty(_harness.ProcessLauncher.Started);
        Assert.Null(owner.Channel.Last);
        Assert.DoesNotContain(_value, _harness.Out + _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void A_keyfile_with_session_and_an_approver_without_it_are_usage_errors()
    {
        _harness.AssertExit(
            CliApp.ExitUsageError,
            _harness.Run("run", "--session", "--keyfile", "k.key", "--vault", _harness.VaultPath, "dev", "--", "x"));
        _harness.AssertExit(
            CliApp.ExitUsageError,
            _harness.Run("run", "--approver", _pipe, "--vault", _harness.VaultPath, "dev", "--", "x"));

        Assert.Empty(_harness.Prompt.PromptsSeen);
        Assert.Empty(_harness.ProcessLauncher.Started);
    }

    /// <summary>Answers env prompts with a fixed answer and remembers the last one.</summary>
    private sealed class ScriptedChannel(ApprovalAnswer answer) : IApprovalChannel
    {
        internal EnvReleasePrompt? Last { get; private set; }

        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ApprovalAnswer.Denied);

        public ValueTask<ApprovalAnswer> AskAsync(EnvReleasePrompt prompt, CancellationToken cancellationToken)
        {
            Last = prompt;
            return ValueTask.FromResult(answer);
        }
    }

    /// <summary>The vault held unlocked and served on the test's pipe, as <c>keypaste agent</c> composes it.</summary>
    private sealed class Owner : IAsyncDisposable
    {
        private readonly Vault _vault;
        private readonly GrantCache _grants = new(TimeProvider.System);
        private readonly ApprovalGate _gate;
        private readonly ApproverListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _running;

        private Owner(RunSessionTests test, ApprovalAnswer answer)
        {
            _vault = Vault.Open(test._harness.VaultPath, _master);
            Channel = new ScriptedChannel(answer);
            _gate = new ApprovalGate(Channel, TimeProvider.System, ApprovalLimits.Default);

            var handler = new ApproverHandler(
                new VaultCredentialSource(() => _vault),
                new VaultEntryNameLister(() => _vault),
                _gate,
                _grants,
                PolicyGate.None);

            var authority = new SessionAuthority(
                VaultIdentity.Of(test._harness.Directory, test._harness.VaultPath),
                () => Lifetime,
                handler,
                new SessionEnvironments(_gate, asked => ReferenceEquals(asked, Lifetime) && asked.IsLive ? _vault : null, TimeProvider.System));

            _listener = new ApproverListener(test._pipe, authority);
            _running = _listener.RunAsync(_stop.Token);
        }

        internal ScriptedChannel Channel { get; }

        internal SessionLifetime Lifetime { get; } = new();

        internal static Owner Start(RunSessionTests test, ApprovalAnswer answer) => new(test, answer);

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();

            try
            {
                await _running;
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
                // Tearing the listener down is how it stops.
            }

            _listener.Dispose();
            Lifetime.Dispose();
            _gate.Dispose();
            _grants.Dispose();
            _vault.Dispose();
            _stop.Dispose();
        }
    }
}
