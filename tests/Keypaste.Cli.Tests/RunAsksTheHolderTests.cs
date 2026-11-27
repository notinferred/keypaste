using System.Security.Cryptography;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Core.Ownership;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// A plain <c>keypaste run</c> asks the app or <c>keypaste approve</c> while its claim holds the vault and it answers
/// unlocked, exactly as <c>--session</c> does, and that owner records it; with nothing answering it opens the vault with
/// the master password as before, and <c>--no-session</c> always does.
/// </summary>
public sealed class RunAsksTheHolderTests : IDisposable
{
    private const string _master = "run-holder-master-pw";
    private const string _value = "sk_live_run_holder_sentinel";

    private readonly CliHarness _harness = new();
    private readonly string _pipe = "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

    public RunAsksTheHolderTests()
    {
        _harness.SeedVault(_master);

        using (var vault = ClaimedVault.Open(_harness.VaultPath, _master))
        {
            ProjectVariables.Set(vault, "dev", "TOKEN", _value);
            vault.Save();
        }

        _harness.Prompt.PromptsSeen.Clear();
        _harness.Environment[ApproverEndpoint.EnvironmentVariable] = _pipe;
        _harness.Environment[KeypasteHome.EnvironmentVariable] = _harness.Directory;
    }

    public void Dispose() => _harness.Dispose();

    private int Run(params string[] flags) =>
        _harness.Run(["run", .. flags, "--vault", _harness.VaultPath, "dev", "--", "deploy", "--to", "staging"]);

    private UnlockedOwner Owner(ApprovalAnswer answer) => UnlockedOwner.Start(_harness, _pipe, _master, answer);

    /// <summary>The vault's claim, taken as the app or <c>keypaste approve</c> takes it when it unlocks.</summary>
    private VaultClaim Hold(OwnerKind holder)
    {
        var home = KeypasteHome.Resolve(_harness.Environment[KeypasteHome.EnvironmentVariable]);
        Assert.True(VaultClaim.TryAcquire(home, _harness.VaultPath, holder, out var claim, out var refusal), refusal);
        return claim;
    }

    [Theory]
    [InlineData(OwnerKind.DesktopApp)]
    [InlineData(OwnerKind.TerminalAgent)]
    public async Task A_plain_run_asks_the_holder_types_no_password_and_is_logged_by_it(OwnerKind holder)
    {
        await using var owner = Owner(ApprovalAnswer.ApprovedOnce);

        using (Hold(holder))
        {
            _harness.AssertExit(0, Run());
        }

        Assert.Equal(_value, _harness.ProcessLauncher.Environment["TOKEN"]);
        Assert.Empty(_harness.Prompt.PromptsSeen);
        Assert.Contains("answer in its prompt", _harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain(_value, _harness.Out + _harness.Err, StringComparison.Ordinal);

        var asked = Assert.IsType<EnvReleasePrompt>(owner.Channel.Last);
        Assert.Equal("deploy --to staging", asked.Command);
        Assert.Contains("\"entries\":[\"env/dev/.env\"", Assert.Single(owner.AuditLines()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_plain_run_the_holder_refuses_starts_nothing_and_asks_no_password()
    {
        await using var owner = Owner(ApprovalAnswer.Denied);

        using (Hold(OwnerKind.DesktopApp))
        {
            _harness.AssertExit(CliApp.ExitInternalError, Run());
        }

        Assert.Empty(_harness.ProcessLauncher.Started);
        Assert.Empty(_harness.Prompt.PromptsSeen);
        Assert.Contains("said no", _harness.Err, StringComparison.Ordinal);
        Assert.Single(owner.AuditLines());
    }

    [Fact]
    public void With_nothing_holding_the_vault_a_plain_run_opens_it_with_the_master_password()
    {
        _harness.Prompt.Enqueue(_master);

        _harness.AssertExit(0, Run());

        Assert.Equal([CliHarness.MasterPasswordPrompt], _harness.Prompt.PromptsSeen);
        Assert.Equal(_value, _harness.ProcessLauncher.Environment["TOKEN"]);
        Assert.DoesNotContain("answer in its prompt", _harness.Err, StringComparison.Ordinal);
    }

    /// <summary>A claim whose holder no longer answers, as after the app was killed, is no holder.</summary>
    [Fact]
    public void A_holder_that_does_not_answer_leaves_a_plain_run_to_the_master_password()
    {
        _harness.Prompt.Enqueue(_master);

        using (Hold(OwnerKind.DesktopApp))
        {
            _harness.AssertExit(0, Run());
        }

        Assert.Equal([CliHarness.MasterPasswordPrompt], _harness.Prompt.PromptsSeen);
        Assert.Equal(_value, _harness.ProcessLauncher.Environment["TOKEN"]);
    }

    [Fact]
    public async Task A_holder_with_the_vault_locked_leaves_a_plain_run_to_the_master_password()
    {
        await using var owner = Owner(ApprovalAnswer.ApprovedOnce);
        owner.Lifetime.End();
        _harness.Prompt.Enqueue(_master);

        using (Hold(OwnerKind.DesktopApp))
        {
            _harness.AssertExit(0, Run());
        }

        Assert.Equal([CliHarness.MasterPasswordPrompt], _harness.Prompt.PromptsSeen);
        Assert.Null(owner.Channel.Last);
    }

    [Fact]
    public async Task No_session_opens_the_vault_here_even_while_the_holder_answers()
    {
        await using var owner = Owner(ApprovalAnswer.ApprovedOnce);
        _harness.Prompt.Enqueue(_master);

        using (Hold(OwnerKind.DesktopApp))
        {
            _harness.AssertExit(0, Run("--no-session"));
        }

        Assert.Equal([CliHarness.MasterPasswordPrompt], _harness.Prompt.PromptsSeen);
        Assert.Equal(_value, _harness.ProcessLauncher.Environment["TOKEN"]);
        Assert.Null(owner.Channel.Last);
        Assert.Empty(owner.AuditLines());
    }

    [Fact]
    public void Session_and_no_session_together_are_a_usage_error()
    {
        _harness.AssertExit(CliApp.ExitUsageError, Run("--session", "--no-session"));

        Assert.Contains("pass one of them", _harness.Err, StringComparison.Ordinal);
        Assert.Empty(_harness.Prompt.PromptsSeen);
        Assert.Empty(_harness.ProcessLauncher.Started);
    }

    /// <summary>Item references resolve only in a vault opened here, so the holder is not asked and the run says why it asks for the password.</summary>
    [Fact]
    public async Task A_reference_file_the_holder_cannot_be_asked_for_opens_the_vault_here()
    {
        using (var vault = ClaimedVault.Open(_harness.VaultPath, _master))
        {
            vault.AddEntry(new VaultEntry { GroupPath = "work", Title = "github", Password = "gh-token" });
            vault.Save();
        }

        _harness.WorkingDirectory = _harness.Directory;
        var file = Path.Combine(_harness.Directory, "refs.env");
        File.WriteAllText(file, "API_TOKEN=kp://dev/dev/TOKEN\nGH=kp:///work/github\n");
        await using var owner = Owner(ApprovalAnswer.ApprovedOnce);
        _harness.Prompt.Enqueue(_master);

        using (Hold(OwnerKind.DesktopApp))
        {
            _harness.AssertExit(0, _harness.Run("run", "--env-file", file, "--vault", _harness.VaultPath, "--", "deploy"));
        }

        Assert.Contains("can't be asked for this run, so it is opened here: item references", _harness.Err, StringComparison.Ordinal);
        Assert.Equal([CliHarness.MasterPasswordPrompt], _harness.Prompt.PromptsSeen);
        Assert.Equal("gh-token", _harness.ProcessLauncher.Environment["GH"]);
        Assert.Null(owner.Channel.Last);
    }
}
