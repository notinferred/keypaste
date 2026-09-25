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

    [Fact]
    public async Task RunSession_Profile_SendsEnvProfile_AndChecksTheEcho()
    {
        AddStaging(("TOKEN", "staging-token"));

        await using (var owner = Owner.Start(this, ApprovalAnswer.Approved))
        {
            _harness.AssertExit(0, RunProfile("staging", "dev"));

            Assert.Equal("staging-token", _harness.ProcessLauncher.Environment["TOKEN"]);
            Assert.Equal("staging", Assert.IsType<EnvReleasePrompt>(owner.Channel.Last).Profile);
            Assert.Contains("profile 'staging'", _harness.Err, StringComparison.Ordinal);
        }

        await using var liar = Liar.Start(this, EnvResolved.Released("dev", [new EnvVariable("TOKEN", _value)]));

        _harness.AssertExit(CliApp.ExitInternalError, RunProfile("staging", "dev"));

        Assert.Equal("staging", liar.Asked?.Profile);
        Assert.Contains("released the 'dev' profile when 'staging' was asked for", _harness.Err, StringComparison.Ordinal);
        Assert.Single(_harness.ProcessLauncher.Started);
        Assert.DoesNotContain(_value, _harness.Out + _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunSession_ARefusalForAProfile_ShowsItsOwnReason()
    {
        AddStaging(("TOKEN", "staging-token"));

        await using (var declined = Owner.Start(this, ApprovalAnswer.Denied))
        {
            _harness.AssertExit(CliApp.ExitInternalError, RunProfile("staging", "dev"));
            Assert.Contains("said no", _harness.Err, StringComparison.Ordinal);
        }

        await using (var locked = Owner.Start(this, ApprovalAnswer.Approved))
        {
            locked.Lifetime.End();
            _harness.AssertExit(CliApp.ExitInternalError, RunProfile("staging", "dev"));
            Assert.Contains("locked", _harness.Err, StringComparison.Ordinal);
        }

        await using (var liar = Liar.Start(this, EnvResolved.Refused("dev", EnvOutcome.Declined, profile: "dev"), "the person asked said no"))
        {
            _harness.AssertExit(CliApp.ExitInternalError, RunProfile("staging", "dev"));
        }

        Assert.DoesNotContain("was asked for", _harness.Err, StringComparison.Ordinal);
        Assert.Empty(_harness.ProcessLauncher.Started);
    }

    [Fact]
    public async Task RunSession_ReferenceFile_SendsNamesAndLiterals_ThePromptShowsThem()
    {
        AddStaging(("TOKEN", "staging-token"), ("OTHER", "other-value"));
        var file = WriteFile("API_TOKEN=kp://dev/staging/TOKEN\nHTTPS_PROXY=http://proxy.internal:3128\n");
        await using var owner = Owner.Start(this, ApprovalAnswer.Approved);

        _harness.AssertExit(0, _harness.Run("run", "--session", "--env-file", file, "--vault", _harness.VaultPath, "--", "deploy"));

        var asked = Assert.IsType<EnvReleasePrompt>(owner.Channel.Last);
        Assert.Equal("staging", asked.Profile);
        Assert.Equal(["API_TOKEN ← TOKEN", "HTTPS_PROXY=http://proxy.internal:3128"], asked.FileLines);
        Assert.Equal("staging-token", _harness.ProcessLauncher.Environment["API_TOKEN"]);
        Assert.Equal("http://proxy.internal:3128", _harness.ProcessLauncher.Environment["HTTPS_PROXY"]);
        Assert.False(_harness.ProcessLauncher.Environment.ContainsKey("TOKEN"));
        Assert.Contains("resolving", _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunSession_OwnerThatCannotReadProfiles_StartsNothing()
    {
        await using var old = Liar.Start(this, reply: null);

        _harness.AssertExit(CliApp.ExitInternalError, RunProfile("staging", "dev"));

        Assert.Contains("is older and cannot release profiles; update it, so nothing was started", _harness.Err, StringComparison.Ordinal);
        Assert.Empty(_harness.ProcessLauncher.Started);
        Assert.Empty(_harness.Prompt.PromptsSeen);
    }

    [Fact]
    public async Task RunSession_ALiteralThePromptWouldShorten_IsRefusedUnasked()
    {
        var file = WriteFile($"API_TOKEN=kp://dev/dev/TOKEN\nNODE_OPTIONS=\"{new string(' ', 1100)}--require ./x.js\"\n");
        await using var owner = Owner.Start(this, ApprovalAnswer.Approved);

        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("run", "--session", "--env-file", file, "--vault", _harness.VaultPath, "--", "deploy"));

        Assert.Contains("line 2: the value of NODE_OPTIONS is too long", _harness.Err, StringComparison.Ordinal);
        Assert.Null(owner.Channel.Last);
        Assert.Empty(_harness.ProcessLauncher.Started);
    }

    [Fact]
    public async Task RunSession_AFileTooLargeForOneFrame_IsRefusedAsTooLong_NotAsAnOlderOwner()
    {
        var literals = string.Concat(Enumerable.Range(0, 200).Select(i => $"LITERAL_{i}={new string('x', 400)}\n"));
        var file = WriteFile($"API_TOKEN=kp://dev/dev/TOKEN\n{literals}");
        await using var owner = Owner.Start(this, ApprovalAnswer.Approved);

        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("run", "--session", "--env-file", file, "--vault", _harness.VaultPath, "--", "deploy"));

        Assert.Contains("is too long for the prompt to show whole; run without --session", _harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("older", _harness.Err, StringComparison.Ordinal);
        Assert.Null(owner.Channel.Last);
        Assert.Empty(_harness.ProcessLauncher.Started);
    }

    [Fact]
    public async Task RunSession_EntryReferences_AreRefused()
    {
        var file = WriteFile("API_TOKEN=kp://dev/dev/TOKEN\nGH=kp:///work/github\n");
        await using var owner = Owner.Start(this, ApprovalAnswer.Approved);

        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("run", "--session", "--env-file", file, "--vault", _harness.VaultPath, "--", "deploy"));

        Assert.Contains("entry references and several projects need the vault opened directly; run without --session", _harness.Err, StringComparison.Ordinal);
        Assert.Null(owner.Channel.Last);
        Assert.Empty(_harness.ProcessLauncher.Started);
        Assert.Empty(_harness.Prompt.PromptsSeen);
    }

    [Fact]
    public async Task RunSession_ReferenceFile_RequestsOnlyReferencedKeys()
    {
        AddStaging(("TOKEN", "staging-token"), ("UNUSED", "unused-value"));
        using (var vault = Vault.Open(_harness.VaultPath, _master))
        {
            vault.AddEntry(new VaultEntry { GroupPath = "env/dev/staging", Title = "BAD-NAME", Password = "x" });
            vault.Save();
        }

        var file = WriteFile("A=kp://dev/staging/TOKEN\nB=kp://dev/staging/TOKEN\n");
        await using var owner = Owner.Start(this, ApprovalAnswer.Approved);

        _harness.AssertExit(0, _harness.Run("run", "--session", "--env-file", file, "--vault", _harness.VaultPath, "--", "deploy"));

        Assert.Equal(["TOKEN"], Assert.IsType<EnvReleasePrompt>(owner.Channel.Last).Keys);
        Assert.Equal("staging-token", _harness.ProcessLauncher.Environment["A"]);
        Assert.Equal("staging-token", _harness.ProcessLauncher.Environment["B"]);
        Assert.False(_harness.ProcessLauncher.Environment.ContainsKey("UNUSED"));
    }

    private int RunProfile(string profile, string project) =>
        _harness.Run(["run", "--session", "-p", profile, "--vault", _harness.VaultPath, project, "--", "deploy"]);

    private void AddStaging(params (string Key, string Value)[] variables)
    {
        using var vault = Vault.Open(_harness.VaultPath, _master);

        foreach (var (key, value) in variables)
        {
            Assert.NotEqual(EnvSetOutcome.Rejected, new EnvStore(vault).TrySet("dev", "staging", key, value, out _));
        }

        vault.Save();
    }

    private string WriteFile(string text)
    {
        _harness.WorkingDirectory = _harness.Directory;
        var path = Path.Combine(_harness.Directory, "refs.env");
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>Attaches anyone and answers every env request with one scripted reply, or hangs up as an owner from before profiles does.</summary>
    private sealed class Liar : IApproverHandler, IAsyncDisposable
    {
        private readonly EnvResolved? _reply;
        private readonly string _reason;
        private readonly ApproverListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _running;

        private Liar(RunSessionTests test, EnvResolved? reply, string reason)
        {
            _reply = reply;
            _reason = reason;
            _listener = new ApproverListener(test._pipe, this);
            _running = _listener.RunAsync(_stop.Token);
        }

        internal EnvRequest? Asked { get; private set; }

        internal static Liar Start(RunSessionTests test, EnvResolved? reply, string reason = "") => new(test, reply, reason);

        public ValueTask<AttachReply> AttachAsync(AttachRequest request, string connectionId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(AttachReply.To("liar-session"));

        public ValueTask<NamesReply> ListAsync(NamesRequest request, string connectionId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("not asked");

        public ValueTask<CredentialReply> RequestAsync(CredentialRequest request, string connectionId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("not asked");

        public ValueTask<EnvReply> ReleaseEnvAsync(EnvRequest request, string connectionId, CancellationToken cancellationToken)
        {
            Asked = request;

            // Throwing ends the connection unanswered, which is what an owner that cannot read the kind does.
            return _reply is { } reply
                ? ValueTask.FromResult(new EnvReply(reply, _reason))
                : throw new InvalidOperationException("an owner from before profiles does not answer env-profile");
        }

        public void Disconnected(string connectionId)
        {
        }

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
            _stop.Dispose();
        }
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
