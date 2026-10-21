using System.Security.Cryptography;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Tests;
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

    private static readonly string[] _deploy = ["deploy", "--to", "staging"];
    private static readonly string[] _launched = UnlockedOwner.InTerminal("deploy --to staging");

    private readonly CliHarness _harness = new();
    private readonly string _pipe = "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

    public RunSessionTests()
    {
        _harness.SeedVault(_master);

        using (var vault = Vault.Open(_harness.VaultPath, _master))
        {
            ProjectVariables.Set(vault, "dev", "TOKEN", _value);
            ExpiredHolder(vault, new EntryName("services", "Broken"), "env:broken", "BROKEN_KEY");
            vault.Save();
        }

        _harness.Prompt.PromptsSeen.Clear();
        _harness.Environment[ApproverEndpoint.EnvironmentVariable] = _pipe;
        _harness.Environment[KeypasteHome.EnvironmentVariable] = _harness.Directory;
    }

    public void Dispose() => _harness.Dispose();

    private int Run(params string[] project) =>
        _harness.Run(["run", "--session", "--vault", _harness.VaultPath, .. project, "--", .. _deploy]);

    /// <summary>Runs as the window Terminal opens for a Run in the app does.</summary>
    private int RunLaunched(params string[] project) =>
        _harness.Run(["run", "--session", "--vault", _harness.VaultPath, .. project, "--", .. _launched]);

    private UnlockedOwner Owner(ApprovalAnswer answer) => UnlockedOwner.Start(_harness, _pipe, _master, answer);

    /// <summary>Confirms <c>dev</c>'s run of the deploy command in Terminal in this directory, as the app's card does, and hands the pass to the next run.</summary>
    private string Confirmed(UnlockedOwner owner)
    {
        var pass = owner.Confirm("dev", EnvProfileNames.Default, _harness.WorkingDirectory, _launched);
        _harness.Environment[LaunchPasses.EnvironmentVariable] = pass;
        return pass;
    }

    [Fact]
    public async Task An_approved_run_starts_the_child_with_the_set_and_asks_no_password()
    {
        await using var owner = Owner(ApprovalAnswer.Approved);

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

    /// <summary>
    /// A set past one ordinary frame reaches the child whole, in the reply only a runner reads (D-0454).
    /// Its values are each under the 32,767 characters Windows lets a variable hold and the 128 KiB
    /// Linux lets one string of <c>execve</c> hold, so the set would start anywhere.
    /// </summary>
    [Fact]
    public async Task A_set_of_200_KiB_reaches_the_child_whole()
    {
        var large = Enumerable.Range(0, 7).Select(i => new EnvVariable($"BLOB_{i}", new string((char)('a' + i), 30_000))).ToList();
        using (var vault = Vault.Open(_harness.VaultPath, _master))
        {
            foreach (var variable in large)
            {
                ProjectVariables.Set(vault, "dev", variable.Key, variable.Value);
            }

            vault.Save();
        }

        await using var owner = Owner(ApprovalAnswer.Approved);

        _harness.AssertExit(0, Run("dev"));

        var child = _harness.ProcessLauncher.Environment;
        Assert.All(large, variable => Assert.Equal(variable.Value, child[variable.Key]));
        Assert.Equal(_value, child["TOKEN"]);
        Assert.Contains("\"entries\":[\"env/dev/.env\"", Assert.Single(owner.AuditLines()), StringComparison.Ordinal);
        Assert.DoesNotContain(large[0].Value, _harness.Out + _harness.Err, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ApprovalAnswer.Denied, "said no")]
    [InlineData(ApprovalAnswer.TimedOut, "in time")]
    [InlineData(ApprovalAnswer.Failed, "could not be shown")]
    public async Task A_refused_run_starts_nothing_and_says_why(ApprovalAnswer answer, string why)
    {
        await using var owner = Owner(answer);

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
        Assert.Contains(
            $"nothing holds {_harness.VaultPath} unlocked; unlock it in the keypaste app or start `keypaste approve`, so nothing was started",
            _harness.Err,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_locked_owner_refuses_and_nobody_is_asked()
    {
        await using var owner = Owner(ApprovalAnswer.Approved);
        owner.Lifetime.End();

        _harness.AssertExit(CliApp.ExitInternalError, Run("dev"));

        Assert.Empty(_harness.ProcessLauncher.Started);
        Assert.Empty(_harness.Prompt.PromptsSeen);
        Assert.Contains("locked", _harness.Err, StringComparison.Ordinal);
        Assert.Null(owner.Channel.Last);
    }

    /// <summary>The owner records a request it refuses as invalid, which the runner, the side that could skip it, never does (T-30).</summary>
    [Fact]
    public async Task A_command_the_prompt_cannot_show_whole_is_refused_and_recorded_by_the_owner()
    {
        await using var owner = Owner(ApprovalAnswer.Approved);

        _harness.AssertExit(
            CliApp.ExitInternalError,
            _harness.Run("run", "--session", "--vault", _harness.VaultPath, "dev", "--", "deploy", new string('a', EnvReleasePrompt.MaximumCommandLength)));

        Assert.Contains(
            $"the command is longer than the {EnvReleasePrompt.MaximumCommandLength} characters a prompt shows whole, so nothing was started",
            _harness.Err,
            StringComparison.Ordinal);
        Assert.Empty(_harness.ProcessLauncher.Started);
        Assert.Null(owner.Channel.Last);

        var line = Assert.Single(owner.AuditLines());
        Assert.Contains("keypaste run --session", line, StringComparison.Ordinal);
        Assert.Contains("\"decision\":\"denied\"", line, StringComparison.Ordinal);
        Assert.Contains("\"method\":\"invalid-request\"", line, StringComparison.Ordinal);
        Assert.Contains($"\"session\":\"{owner.Lifetime.Id}\"", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_project_exits_3_and_an_unusable_set_names_its_entries_unasked()
    {
        await using var owner = Owner(ApprovalAnswer.Approved);

        _harness.AssertExit(CliApp.ExitNotFound, Run("missing"));
        _harness.AssertExit(CliApp.ExitInternalError, Run("broken"));

        Assert.Contains("no env set for 'missing'", _harness.Err, StringComparison.Ordinal);
        Assert.Contains("BROKEN_KEY expired", _harness.Err, StringComparison.Ordinal);
        Assert.Contains("services/Broken", _harness.Err, StringComparison.Ordinal);
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

        await using (var owner = Owner(ApprovalAnswer.Approved))
        {
            _harness.AssertExit(0, RunProfile("staging", "dev"));

            Assert.Equal("staging-token", _harness.ProcessLauncher.Environment["TOKEN"]);
            Assert.Equal("staging", Assert.IsType<EnvReleasePrompt>(owner.Channel.Last).Profile);
            Assert.Contains("environment 'staging'", _harness.Err, StringComparison.Ordinal);
        }

        await using var liar = Liar.Start(this, EnvResolved.Released("dev", [new EnvVariable("TOKEN", _value)]));

        _harness.AssertExit(CliApp.ExitInternalError, RunProfile("staging", "dev"));

        Assert.Equal("staging", liar.Asked?.Profile);
        Assert.Contains("released the 'dev' environment when 'staging' was asked for", _harness.Err, StringComparison.Ordinal);
        Assert.Single(_harness.ProcessLauncher.Started);
        Assert.DoesNotContain(_value, _harness.Out + _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunSession_ARefusalForAProfile_ShowsItsOwnReason()
    {
        AddStaging(("TOKEN", "staging-token"));

        await using (var declined = Owner(ApprovalAnswer.Denied))
        {
            _harness.AssertExit(CliApp.ExitInternalError, RunProfile("staging", "dev"));
            Assert.Contains("said no", _harness.Err, StringComparison.Ordinal);
        }

        await using (var locked = Owner(ApprovalAnswer.Approved))
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
        await using var owner = Owner(ApprovalAnswer.Approved);

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
    public async Task RunSession_AnOwnerThatHangsUp_StartsNothing()
    {
        await using var silent = Liar.Start(this, reply: null);

        _harness.AssertExit(CliApp.ExitInternalError, RunProfile("staging", "dev"));

        Assert.Contains("did not answer, so nothing was started", _harness.Err, StringComparison.Ordinal);
        Assert.Empty(_harness.ProcessLauncher.Started);
        Assert.Empty(_harness.Prompt.PromptsSeen);
    }

    [Fact]
    public async Task RunSession_ALiteralThePromptWouldShorten_IsRefusedUnasked()
    {
        var file = WriteFile($"API_TOKEN=kp://dev/dev/TOKEN\nNODE_OPTIONS=\"{new string(' ', 1100)}--require ./x.js\"\n");
        await using var owner = Owner(ApprovalAnswer.Approved);

        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("run", "--session", "--env-file", file, "--vault", _harness.VaultPath, "--", "deploy"));

        Assert.Contains("line 2: the value of NODE_OPTIONS is too long", _harness.Err, StringComparison.Ordinal);
        Assert.Null(owner.Channel.Last);
        Assert.Empty(_harness.ProcessLauncher.Started);
    }

    [Fact]
    public async Task RunSession_AFileTooLargeForOneFrame_IsRefusedAsTooLong_NotAsAnUnansweredRequest()
    {
        var literals = string.Concat(Enumerable.Range(0, 200).Select(i => $"LITERAL_{i}={new string('x', 400)}\n"));
        var file = WriteFile($"API_TOKEN=kp://dev/dev/TOKEN\n{literals}");
        await using var owner = Owner(ApprovalAnswer.Approved);

        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("run", "--session", "--env-file", file, "--vault", _harness.VaultPath, "--", "deploy"));

        Assert.Contains("is too long for the prompt to show whole; run without --session", _harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("did not answer", _harness.Err, StringComparison.Ordinal);
        Assert.Null(owner.Channel.Last);
        Assert.Empty(_harness.ProcessLauncher.Started);
    }

    [Fact]
    public async Task RunSession_EntryReferences_AreRefused()
    {
        var file = WriteFile("API_TOKEN=kp://dev/dev/TOKEN\nGH=kp:///work/github\n");
        await using var owner = Owner(ApprovalAnswer.Approved);

        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("run", "--session", "--env-file", file, "--vault", _harness.VaultPath, "--", "deploy"));

        Assert.Contains("item references and several projects need the vault opened directly; run without --session", _harness.Err, StringComparison.Ordinal);
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
            ExpiredHolder(vault, new EntryName("services", "Stale"), "env:dev:staging", "STALE_KEY");
            vault.Save();
        }

        var file = WriteFile("A=kp://dev/staging/TOKEN\nB=kp://dev/staging/TOKEN\n");
        await using var owner = Owner(ApprovalAnswer.Approved);

        _harness.AssertExit(0, _harness.Run("run", "--session", "--env-file", file, "--vault", _harness.VaultPath, "--", "deploy"));

        Assert.Equal(["TOKEN"], Assert.IsType<EnvReleasePrompt>(owner.Channel.Last).Keys);
        Assert.Equal("staging-token", _harness.ProcessLauncher.Environment["A"]);
        Assert.Equal("staging-token", _harness.ProcessLauncher.Environment["B"]);
        Assert.False(_harness.ProcessLauncher.Environment.ContainsKey("UNUSED"));
    }

    /// <summary>The run Terminal starts for the app's Run confirmation: the owner releases it unasked, and nothing the child starts inherits the pass (D-0452).</summary>
    [Fact]
    public async Task A_run_confirmed_in_the_app_is_released_unasked_and_its_child_gets_no_pass()
    {
        await using var owner = Owner(ApprovalAnswer.Denied);
        var pass = Confirmed(owner);

        _harness.AssertExit(0, RunLaunched("dev"));

        var child = _harness.ProcessLauncher.Environment;
        Assert.Equal(_value, child["TOKEN"]);
        Assert.False(child.ContainsKey(LaunchPasses.EnvironmentVariable));
        Assert.DoesNotContain(child.Values, value => value.Contains(pass, StringComparison.Ordinal));
        Assert.Null(owner.Channel.Last);
        Assert.Empty(_harness.Prompt.PromptsSeen);
        Assert.Contains("keypaste run: starting with dev (dev) as you confirmed in the keypaste app", _harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("answer in its prompt", _harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain(pass, _harness.Out + _harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain(_value, _harness.Out + _harness.Err, StringComparison.Ordinal);

        var line = Assert.Single(owner.AuditLines());
        Assert.Contains("\"method\":\"prompt\"", line, StringComparison.Ordinal);
        Assert.Contains("confirmed in the app", line, StringComparison.Ordinal);
        Assert.DoesNotContain(pass, line, StringComparison.Ordinal);
    }

    /// <summary><c>--project</c> names a project whose name starts with <c>-</c>, which is the one asked for and released, at the prompt and under the app's pass alike (D-0453).</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_project_the_project_option_names_is_the_one_released_even_when_it_starts_with_a_dash(bool confirmedInTheApp)
    {
        using (var vault = Vault.Open(_harness.VaultPath, _master))
        {
            ProjectVariables.Set(vault, "--help", "HELP_TOKEN", _value);
            vault.Save();
        }

        await using var owner = Owner(confirmedInTheApp ? ApprovalAnswer.Denied : ApprovalAnswer.Approved);

        if (confirmedInTheApp)
        {
            _harness.Environment[LaunchPasses.EnvironmentVariable] = owner.Confirm("--help", EnvProfileNames.Default, _harness.WorkingDirectory, _launched);
        }

        _harness.AssertExit(0, confirmedInTheApp ? RunLaunched("--project=--help") : Run("--project=--help"));

        var child = _harness.ProcessLauncher.Environment;
        Assert.Equal(_value, child["HELP_TOKEN"]);
        Assert.False(child.ContainsKey("TOKEN"));
        Assert.Equal(confirmedInTheApp ? "/bin/sh" : "deploy", Assert.Single(_harness.ProcessLauncher.Started).FileName);
        Assert.Equal(confirmedInTheApp ? null : "--help", owner.Channel.Last?.Project);
        Assert.Contains("\"entries\":[\"env/--help/.env\"", Assert.Single(owner.AuditLines()), StringComparison.Ordinal);

        if (confirmedInTheApp)
        {
            Assert.Contains("keypaste run: starting with --help (dev) as you confirmed in the keypaste app", _harness.Err, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_spent_pass_starts_nothing_and_says_so_unasked()
    {
        await using var owner = Owner(ApprovalAnswer.Approved);
        Confirmed(owner);
        _harness.AssertExit(0, RunLaunched("dev"));
        _harness.Stderr.GetStringBuilder().Clear();

        _harness.AssertExit(CliApp.ExitInternalError, RunLaunched("dev"));

        Assert.Contains(
            "keypaste run: the run you confirmed in the app expired or was already used, so nothing was started",
            _harness.Err,
            StringComparison.Ordinal);
        Assert.DoesNotContain("starting with", _harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("answer in its prompt", _harness.Err, StringComparison.Ordinal);
        Assert.Single(_harness.ProcessLauncher.Started);
        Assert.Null(owner.Channel.Last);
    }

    [Fact]
    public async Task A_set_changed_after_the_confirmation_starts_nothing_unasked()
    {
        await using var owner = Owner(ApprovalAnswer.Approved);
        Confirmed(owner);
        owner.Edit(vault => ProjectVariables.Set(vault, "dev", "ADDED", "added-value"));

        _harness.AssertExit(CliApp.ExitInternalError, RunLaunched("dev"));

        Assert.Contains(
            "keypaste run: dev's variables changed after you confirmed in the app, so nothing was started",
            _harness.Err,
            StringComparison.Ordinal);
        Assert.Empty(_harness.ProcessLauncher.Started);
        Assert.Null(owner.Channel.Last);
    }

    /// <summary>
    /// A refusal in the window Terminal opened for the app's Run waits for Return when someone can type in it, so the reason
    /// stays on screen whatever the Terminal profile does as the window's process ends; with nothing to type it ends at once.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_refused_run_from_the_app_waits_for_Return_only_when_someone_can_type(bool terminal)
    {
        await using var owner = Owner(ApprovalAnswer.Approved);
        Confirmed(owner);
        owner.Lifetime.End();
        _harness.Prompt.Interactive = terminal;

        _harness.AssertExit(CliApp.ExitInternalError, RunLaunched("dev"));

        string[] waited = terminal ? ["Press Return to finish."] : [];
        Assert.Equal(waited, _harness.Prompt.PromptsSeen);
        Assert.Contains("locked", _harness.Err, StringComparison.Ordinal);
        Assert.Contains("nothing was started", _harness.Err, StringComparison.Ordinal);
        Assert.Empty(_harness.ProcessLauncher.Started);
    }

    /// <summary>
    /// A run from the app that finds nothing answering, the app having locked or quit after Start, says so and to press
    /// Run again, since its pass belonged to that unlock and only the app listens where it asks; unlocking alone or
    /// starting <c>keypaste approve</c> could not serve it.
    /// </summary>
    [Fact]
    public async Task A_run_from_the_app_finding_nothing_answering_says_to_press_Run_in_the_app_again()
    {
        await using (var owner = Owner(ApprovalAnswer.Approved))
        {
            Confirmed(owner);
        }

        _harness.Prompt.Interactive = true;

        _harness.AssertExit(CliApp.ExitInternalError, RunLaunched("dev"));

        Assert.Contains(
            "keypaste run: the keypaste app locked or quit before this window could start, so nothing was started. Press Run in the app again.",
            _harness.Err,
            StringComparison.Ordinal);
        Assert.DoesNotContain("keypaste approve", _harness.Err, StringComparison.Ordinal);
        Assert.Equal(["Press Return to finish."], _harness.Prompt.PromptsSeen);
        Assert.Empty(_harness.ProcessLauncher.Started);
    }

    /// <summary>A run from the app that starts, and a refusal of a run typed in a terminal, never wait.</summary>
    [Fact]
    public async Task Only_a_refused_run_from_the_app_waits_for_Return()
    {
        await using var owner = Owner(ApprovalAnswer.Denied);
        _harness.Prompt.Interactive = true;
        Confirmed(owner);

        _harness.AssertExit(0, RunLaunched("dev"));
        _harness.Environment.Remove(LaunchPasses.EnvironmentVariable);
        _harness.AssertExit(CliApp.ExitInternalError, Run("dev"));

        Assert.Contains("said no", _harness.Err, StringComparison.Ordinal);
        Assert.Single(_harness.ProcessLauncher.Started);
        Assert.Empty(_harness.Prompt.PromptsSeen);
    }

    /// <summary>A pass the launcher could not have written never becomes an ordinary request, which would ask the person a second time.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("not-a-pass")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task A_malformed_pass_fails_before_any_owner_is_asked(string pass)
    {
        await using var liar = Liar.Start(this, EnvResolved.Released("dev", [new EnvVariable("TOKEN", _value)]));
        _harness.Environment[LaunchPasses.EnvironmentVariable] = pass;

        _harness.AssertExit(CliApp.ExitUsageError, Run("dev"));

        Assert.Contains(
            "keypaste run: KEYPASTE_LAUNCH does not hold a run confirmed in the keypaste app, so nothing was started",
            _harness.Err,
            StringComparison.Ordinal);
        Assert.Equal(0, liar.Attaches);
        Assert.Null(liar.Asked);
        Assert.Empty(_harness.ProcessLauncher.Started);
        Assert.Empty(_harness.Prompt.PromptsSeen);
    }

    [Fact]
    public async Task Without_session_a_pass_is_never_sent_and_the_child_does_not_inherit_it()
    {
        await using var liar = Liar.Start(this, EnvResolved.Released("dev", [new EnvVariable("TOKEN", "from-the-liar")]));
        _harness.Environment[LaunchPasses.EnvironmentVariable] = new string('A', LaunchPasses.Length);
        _harness.Prompt.Enqueue(_master);

        _harness.AssertExit(0, _harness.Run(["run", "--vault", _harness.VaultPath, "dev", "--", .. _deploy]));

        Assert.Equal(_value, _harness.ProcessLauncher.Environment["TOKEN"]);
        Assert.False(_harness.ProcessLauncher.Environment.ContainsKey(LaunchPasses.EnvironmentVariable));
        Assert.Equal(0, liar.Attaches);
    }

    private int RunProfile(string profile, string project) =>
        _harness.Run(["run", "--session", "-p", profile, "--vault", _harness.VaultPath, project, "--", "deploy"]);

    private void AddStaging(params (string Key, string Value)[] variables)
    {
        using var vault = Vault.Open(_harness.VaultPath, _master);

        foreach (var (key, value) in variables)
        {
            ProjectVariables.Set(vault, "dev", "staging", key, value);
        }

        vault.Save();
    }

    /// <summary>Adds an expired entry tagged into an environment and holding one key, which makes that key unusable.</summary>
    private static void ExpiredHolder(Vault vault, EntryName name, string tag, string key)
    {
        vault.AddEntry(new VaultEntry { GroupPath = name.GroupPath, Title = name.Title, Password = "login" });
        Assert.True(vault.SetFields(name, [new FieldWrite(key, _value)]));
        Assert.True(vault.AddTag(name, tag));
        vault.SetExpiryUnchecked(name, new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero));
    }

    private string WriteFile(string text)
    {
        _harness.WorkingDirectory = _harness.Directory;
        var path = Path.Combine(_harness.Directory, "refs.env");
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>Attaches anyone and answers every env request with one scripted reply, or hangs up unanswered.</summary>
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

        internal int Attaches { get; private set; }

        internal static Liar Start(RunSessionTests test, EnvResolved? reply, string reason = "") => new(test, reply, reason);

        public ValueTask<AttachReply> AttachAsync(AttachRequest request, string connectionId, CancellationToken cancellationToken)
        {
            Attaches++;
            return ValueTask.FromResult(AttachReply.To("liar-session"));
        }

        public ValueTask<NamesReply> ListAsync(NamesRequest request, string connectionId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("not asked");

        public ValueTask<CredentialReply> RequestAsync(CredentialRequest request, string connectionId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("not asked");

        public ValueTask<EnvReply> ReleaseEnvAsync(EnvRequest request, string connectionId, CancellationToken cancellationToken)
        {
            Asked = request;

            // Throwing ends the connection unanswered.
            return _reply is { } reply
                ? ValueTask.FromResult(new EnvReply(reply, _reason))
                : throw new InvalidOperationException("hangs up unanswered");
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
}
