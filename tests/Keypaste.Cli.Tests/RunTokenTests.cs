using System.Security.Cryptography;
using Keypaste.Cli.Commands;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Keypaste.Core.Policy;
using Keypaste.Core.Tokens;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste run --token</c> and <c>--bundle</c>: a set injected under a token with no password
/// asked, from the owner's session or from a bundle, the token stripped from the child, and nothing
/// started on any refusal.
/// </summary>
public sealed class RunTokenTests : IDisposable
{
    private const string _master = TokenVerbTests.Master;

    private readonly CliHarness _harness = new();
    private readonly string _pipe = "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

    public RunTokenTests()
    {
        _harness.Environment[KeypasteHome.EnvironmentVariable] = _harness.Directory;
        _harness.Environment[ApproverEndpoint.EnvironmentVariable] = _pipe;
        _harness.SeedVault(
            _master,
            ("env/acme-api/staging/DATABASE_URL", TokenVerbTests.Value),
            ("env/acme-api/prod/DATABASE_URL", TokenVerbTests.ProdValue),
            ("env/web/staging/KEY", "web-key"));
        _harness.Prompt.PromptsSeen.Clear();
    }

    public void Dispose() => _harness.Dispose();

    private string BundlePath => Path.Combine(_harness.Directory, "b.kpb");

    private string RunnerHome => Path.Combine(_harness.Directory, "runner-home");

    [Fact]
    public void Bundle_InjectsTheValues_AndStripsKEYPASTE_TOKEN()
    {
        var token = Bundled("read:acme-api/staging/*");
        _harness.Environment[RunWithToken.EnvironmentVariable] = token;
        _harness.Environment["UNRELATED"] = "kept";
        _harness.Environment.Remove(VaultLocator.EnvironmentVariable);

        _harness.AssertExit(0, _harness.Run("run", "--bundle", BundlePath, "--", "deploy", "--now"));

        var child = _harness.ProcessLauncher.Environment;
        Assert.Equal(TokenVerbTests.Value, child["DATABASE_URL"]);
        Assert.Equal("kept", child["UNRELATED"]);
        Assert.False(child.ContainsKey(RunWithToken.EnvironmentVariable));
        Assert.DoesNotContain(child.Values, value => value.Contains(token[13..], StringComparison.Ordinal));
        Assert.Equal("deploy", _harness.ProcessLauncher.Started[^1].FileName);
        Assert.Empty(_harness.Prompt.PromptsSeen);
        Assert.DoesNotContain(TokenVerbTests.Value, _harness.Out + _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Bundle_WrongToken_StartsNothing()
    {
        Bundled("read:acme-api/staging/*");
        _harness.Environment[RunWithToken.EnvironmentVariable] = TokenSecret.New(out _, out _);

        _harness.AssertExit(CliApp.ExitInternalError, _harness.Run("run", "--bundle", BundlePath, "--", "deploy"));

        Assert.Empty(_harness.ProcessLauncher.Started);
        Assert.Contains("the bundle was made for another token, so nothing was started", _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Bundle_Expired_StartsNothing()
    {
        var token = Bundled("read:acme-api/staging/*", "--ttl", "1h");
        _harness.Environment[RunWithToken.EnvironmentVariable] = token;
        _harness.Clock.Now = _harness.Clock.Now.AddHours(1);

        _harness.AssertExit(CliApp.ExitInternalError, _harness.Run("run", "--bundle", BundlePath, "--", "deploy"));

        Assert.Empty(_harness.ProcessLauncher.Started);
        Assert.Contains("expired", _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Bundle_SeveralPairsUnnamed_IsUsage()
    {
        var token = Bundled("read:acme-api/staging/*,read:web/staging/*");
        _harness.Environment[RunWithToken.EnvironmentVariable] = token;

        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("run", "--bundle", BundlePath, "--", "deploy"));
        Assert.Contains("several sets; name one: acme-api -p staging, web -p staging", _harness.Err, StringComparison.Ordinal);
        Assert.Empty(_harness.ProcessLauncher.Started);

        _harness.AssertExit(0, _harness.Run("run", "--bundle", BundlePath, "web", "--", "deploy"));
        Assert.Equal("web-key", _harness.ProcessLauncher.Environment["KEY"]);

        _harness.AssertExit(CliApp.ExitNotFound, _harness.Run("run", "--bundle", BundlePath, "-p", "dev", "web", "--", "deploy"));
        Assert.Single(_harness.ProcessLauncher.Started);
    }

    [Fact]
    public async Task Session_Releases_AndTheOwnerAudited()
    {
        var token = TokenVerbTests.Mint(_harness, "ci-staging", "read:acme-api/staging/*");
        await using var owner = Owner.Start(this, audited: true);
        RunAsARunner(token);

        _harness.AssertExit(0, Run("-p", "staging", "acme-api"));

        Assert.Equal(TokenVerbTests.Value, _harness.ProcessLauncher.Environment["DATABASE_URL"]);
        Assert.False(_harness.ProcessLauncher.Environment.ContainsKey(RunWithToken.EnvironmentVariable));
        Assert.Empty(_harness.Prompt.PromptsSeen);
        Assert.Null(owner.Channel.Last);

        var line = Assert.Single(owner.AuditLines());
        Assert.Contains("\"method\":\"token\"", line, StringComparison.Ordinal);
        Assert.Contains("\"decision\":\"granted\"", line, StringComparison.Ordinal);
        Assert.Contains("keypaste run --token", line, StringComparison.Ordinal);
        Assert.False(File.Exists(KeypasteHome.AuditPath(RunnerHome)));
    }

    [Fact]
    public async Task Session_OwnerAuditUnwritable_StartsNothing()
    {
        var token = TokenVerbTests.Mint(_harness, "ci-staging", "read:acme-api/staging/*");
        await using var owner = Owner.Start(this, audited: false);
        RunAsARunner(token);

        _harness.AssertExit(CliApp.ExitInternalError, Run("-p", "staging", "acme-api"));

        Assert.Empty(_harness.ProcessLauncher.Started);
        Assert.Contains("the audit log could not be written, so nothing was started", _harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain(TokenVerbTests.Value, _harness.Out + _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Session_Refused_StartsNothing()
    {
        var token = TokenVerbTests.Mint(_harness, "ci-staging", "read:acme-api/staging/*,read:acme-api/qa/*");
        await using var owner = Owner.Start(this, audited: true);
        RunAsARunner(token);

        _harness.AssertExit(CliApp.ExitInternalError, Run("acme-api"));
        _harness.AssertExit(CliApp.ExitInternalError, Run("-p", "prod", "acme-api"));
        _harness.AssertExit(CliApp.ExitNotFound, Run("-p", "qa", "acme-api"));

        Assert.Empty(_harness.ProcessLauncher.Started);
        Assert.Contains("the token's scope does not cover acme-api/dev, so nothing was started", _harness.Err, StringComparison.Ordinal);
        Assert.Contains("does not cover acme-api/prod", _harness.Err, StringComparison.Ordinal);
        Assert.Contains("'acme-api' has no 'qa' profile", _harness.Err, StringComparison.Ordinal);
        Assert.Null(owner.Channel.Last);
        Assert.Equal(3, owner.AuditLines().Count);
    }

    [Fact]
    public void Session_WithNothingHoldingTheVault_SaysSo()
    {
        RunAsARunner(TokenVerbTests.Mint(_harness, "ci-staging", "read:acme-api/staging/*"));

        _harness.AssertExit(CliApp.ExitInternalError, Run("-p", "staging", "acme-api"));

        Assert.Contains("nothing holds", _harness.Err, StringComparison.Ordinal);
        Assert.Empty(_harness.Prompt.PromptsSeen);
        Assert.Empty(_harness.ProcessLauncher.Started);
    }

    [Fact]
    public void TokenOnArgv_Warns()
    {
        var token = Bundled("read:acme-api/staging/*");

        _harness.AssertExit(0, _harness.Run("run", "--token", token, "--bundle", BundlePath, "--", "deploy"));

        Assert.Contains("a token on the command line is visible to other processes; prefer KEYPASTE_TOKEN", _harness.Err, StringComparison.Ordinal);
        Assert.Single(_harness.ProcessLauncher.Started);
    }

    [Fact]
    public void TokenFromStdin_IsReadAsASecret()
    {
        var token = Bundled("read:acme-api/staging/*");
        _harness.Prompt.Enqueue(token);

        _harness.AssertExit(0, _harness.Run("run", "--token", "-", "--bundle", BundlePath, "--", "deploy"));

        Assert.Contains("Token: ", _harness.Prompt.SecretPrompts);
        Assert.DoesNotContain("visible to other processes", _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedToken_IsNeverEchoed()
    {
        const string pasted = "kpt_zzzz_SECRETISH-password-pasted-here";

        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("run", "--token", pasted, "--vault", _harness.VaultPath, "acme-api", "--", "deploy"));
        _harness.Environment[RunWithToken.EnvironmentVariable] = pasted;
        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("run", "--bundle", BundlePath, "--", "deploy"));
        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("run", "--token", "env", "--vault", _harness.VaultPath, "acme-api", "--", "deploy"));

        Assert.Contains("that is not a keypaste token", _harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRETISH", _harness.Out + _harness.Err, StringComparison.Ordinal);
        Assert.Empty(_harness.ProcessLauncher.Started);
        Assert.Empty(_harness.Prompt.PromptsSeen);
    }

    private string Bundled(string scopes, params string[] extra)
    {
        var token = TokenVerbTests.Mint(_harness, "ci-staging", scopes, extra);
        _harness.Prompt.Enqueue(_master);
        _harness.Environment[RunWithToken.EnvironmentVariable] = token;
        _harness.AssertExit(0, _harness.Run("token", "bundle", "ci-staging", "-o", BundlePath, "--vault", _harness.VaultPath));
        _harness.Environment.Remove(RunWithToken.EnvironmentVariable);
        _harness.Stdout.GetStringBuilder().Clear();
        _harness.Stderr.GetStringBuilder().Clear();
        _harness.Prompt.PromptsSeen.Clear();
        _harness.Prompt.SecretPrompts.Clear();
        return token;
    }

    /// <summary>The runner's side: its own home, so any audit line it wrote would land there, and the token in its environment.</summary>
    private void RunAsARunner(string token)
    {
        _harness.Environment[KeypasteHome.EnvironmentVariable] = RunnerHome;
        _harness.Environment[RunWithToken.EnvironmentVariable] = token;
        _harness.Stdout.GetStringBuilder().Clear();
        _harness.Stderr.GetStringBuilder().Clear();
        _harness.Prompt.PromptsSeen.Clear();
    }

    private int Run(params string[] target) =>
        _harness.Run(["run", "--token", "env", "--vault", _harness.VaultPath, .. target, "--", "deploy", "--to", "staging"]);

    /// <summary>Answers env prompts with approval and remembers the last one.</summary>
    private sealed class ScriptedChannel : IApprovalChannel
    {
        internal EnvReleasePrompt? Last { get; private set; }

        public ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ApprovalAnswer.Denied);

        public ValueTask<ApprovalAnswer> AskAsync(EnvReleasePrompt prompt, CancellationToken cancellationToken)
        {
            Last = prompt;
            return ValueTask.FromResult(ApprovalAnswer.ApprovedOnce);
        }
    }

    /// <summary>The vault held unlocked and served on the test's pipe, with the owner's own audit log.</summary>
    private sealed class Owner : IAsyncDisposable
    {
        private readonly Vault _vault;
        private readonly GrantCache _grants = new(TimeProvider.System);
        private readonly ApprovalGate _gate;
        private readonly AuditLog? _audit;
        private readonly ApproverListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _running;
        private readonly string _auditPath;

        private Owner(RunTokenTests test, bool audited)
        {
            _vault = Vault.Open(test._harness.VaultPath, _master);
            _gate = new ApprovalGate(Channel, TimeProvider.System, ApprovalLimits.Default);
            _auditPath = Path.Combine(test._harness.Directory, "owner-audit.jsonl");

            if (audited)
            {
                Assert.True(AuditLog.TryOpen(_auditPath, TimeProvider.System, out _audit, out var error), error);
            }

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
                new SessionEnvironments(
                    _gate,
                    asked => ReferenceEquals(asked, Lifetime) && asked.IsLive ? _vault : null,
                    test._harness.Clock,
                    () => _audit));

            _listener = new ApproverListener(test._pipe, authority);
            _running = _listener.RunAsync(_stop.Token);
        }

        internal ScriptedChannel Channel { get; } = new();

        internal SessionLifetime Lifetime { get; } = new();

        internal static Owner Start(RunTokenTests test, bool audited) => new(test, audited);

        internal List<string> AuditLines()
        {
            if (!File.Exists(_auditPath))
            {
                return [];
            }

            using var stream = new FileStream(_auditPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return [.. reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries)];
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
            Lifetime.Dispose();
            _gate.Dispose();
            _grants.Dispose();
            _audit?.Dispose();
            _vault.Dispose();
            _stop.Dispose();
        }
    }
}
