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
/// While the app or <c>keypaste approve</c> holds the vault, a verb that edits items sends its change to
/// the holder, which asks the person and saves it (D-0464): no master password is asked for or sent, a
/// refusal keeps the verb's exit code, an old script's leading master password sends nothing, and
/// <c>access</c> and <c>import</c> stay refused.
/// </summary>
public sealed class RoutedEditTests : IDisposable
{
    private const string _master = "routed-edit-master";

    private readonly CliHarness _harness = new();
    private readonly string _pipe = "keypaste-tests-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

    public RoutedEditTests()
    {
        _harness.SeedVault(_master, ("Work/github", "gh-before"));

        using (var vault = Vault.Open(_harness.VaultPath, _master))
        {
            vault.SetFields(new EntryName("Work", "github"), [new FieldWrite("KEY", "field-before")]);
            ProjectVariables.Set(vault, "ci", "DEPLOY_KEY", "deploy-before");
            vault.Save();
        }

        _harness.Prompt.PromptsSeen.Clear();
        _harness.Environment[ApproverEndpoint.EnvironmentVariable] = _pipe;
        _harness.Environment[KeypasteHome.EnvironmentVariable] = _harness.Directory;
    }

    public void Dispose() => _harness.Dispose();

    public static TheoryData<string, OwnerKind> Verbs()
    {
        var data = new TheoryData<string, OwnerKind>();

        foreach (var verb in new[] { "add", "set", "set --field", "rm", "rotate", "field rm", "env set", "env rm", "env pull", "env tag" })
        {
            data.Add(verb, OwnerKind.DesktopApp);
            data.Add(verb, OwnerKind.TerminalAgent);
        }

        return data;
    }

    private UnlockedOwner Owner(ApprovalAnswer answer) => UnlockedOwner.Start(_harness, _pipe, _master, answer);

    private VaultClaim Hold(OwnerKind holder)
    {
        var home = KeypasteHome.Resolve(_harness.Environment[KeypasteHome.EnvironmentVariable]);
        Assert.True(VaultClaim.TryAcquire(home, _harness.VaultPath, holder, out var claim, out var refusal), refusal);
        return claim;
    }

    private (string[] Args, string[] Piped) Arguments(string verb)
    {
        var vault = new[] { "--vault", _harness.VaultPath };

        return verb switch
        {
            "add" => (["add", "Work/new", .. vault], ["new-password"]),
            "set" => (["set", "Work/github", .. vault], ["gh-after"]),
            "set --field" => (["set", "Work/github", "--field", "OTHER", .. vault], ["other-after"]),
            "rm" => (["rm", "Work/github", "--yes", .. vault], []),
            "rotate" => (["rotate", "Work/github", .. vault], []),
            "field rm" => (["field", "rm", "Work/github", "KEY", .. vault], []),
            "env set" => (["env", "set", "ci", "DEPLOY_KEY", .. vault], ["deploy-after"]),
            "env rm" => (["env", "rm", "ci", "DEPLOY_KEY", "--yes", .. vault], []),
            "env pull" => (["env", "pull", "ci", DotEnv("PULLED_KEY=pulled\n"), "--yes", "--keep", .. vault], []),
            "env tag" => (["env", "tag", "ci", "Work/github", "--yes", .. vault], []),
            _ => throw new ArgumentOutOfRangeException(nameof(verb), verb, null),
        };
    }

    private string DotEnv(string text)
    {
        var path = Path.Combine(_harness.Directory, ".env");
        File.WriteAllText(path, text);
        return path;
    }

    [Theory]
    [MemberData(nameof(Verbs))]
    public async Task An_allowed_edit_is_saved_by_the_holder_and_no_master_password_is_asked_for(string verb, OwnerKind holder)
    {
        var (args, piped) = Arguments(verb);
        var before = File.ReadAllBytes(_harness.VaultPath);
        await using var owner = Owner(ApprovalAnswer.ApprovedOnce);

        using (Hold(holder))
        {
            _harness.Prompt.Enqueue(piped);
            _harness.AssertExit(CliApp.ExitSuccess, _harness.Run(args));
        }

        Assert.DoesNotContain("Master password: ", _harness.Prompt.PromptsSeen);
        Assert.Equal(1, owner.Channel.EditsAsked);
        Assert.Contains("holds this vault; it will ask you to allow this change", _harness.Err, StringComparison.Ordinal);
        Assert.NotEqual(before, File.ReadAllBytes(_harness.VaultPath));
        Assert.Contains(owner.AuditLines(), line => line.Contains("\"terminal_edit\"", StringComparison.Ordinal) && line.Contains("\"granted\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_allowed_set_writes_the_value_the_terminal_sent()
    {
        await using (var owner = Owner(ApprovalAnswer.ApprovedOnce))
        {
            using (Hold(OwnerKind.DesktopApp))
            {
                _harness.Prompt.Enqueue("gh-after");
                _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("set", "Work/github", "--vault", _harness.VaultPath));
            }

            Assert.DoesNotContain("gh-after", owner.Channel.LastEdit!.Changes.Concat([owner.Channel.LastEdit.Item]), StringComparer.Ordinal);
        }

        using var vault = Vault.Open(_harness.VaultPath, _master);
        Assert.Equal("gh-after", vault.Find(new EntryName("Work", "github"))!.Password);
        Assert.Contains("updated Work/github", _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_denied_edit_changes_nothing()
    {
        var before = File.ReadAllBytes(_harness.VaultPath);
        await using var owner = Owner(ApprovalAnswer.Denied);

        using (Hold(OwnerKind.DesktopApp))
        {
            _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("rm", "Work/github", "--yes", "--vault", _harness.VaultPath));
        }

        Assert.Contains("keypaste rm: not saved: a person refused this change", _harness.Err, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(_harness.VaultPath));
    }

    [Fact]
    public async Task A_master_password_piped_before_the_value_sends_nothing()
    {
        var before = File.ReadAllBytes(_harness.VaultPath);
        await using var owner = Owner(ApprovalAnswer.ApprovedOnce);

        using (Hold(OwnerKind.DesktopApp))
        {
            _harness.Prompt.Enqueue(_master, "gh-after");
            _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("set", "Work/github", "--vault", _harness.VaultPath));
        }

        Assert.Contains("stdin held more lines than this command reads", _harness.Err, StringComparison.Ordinal);
        Assert.Equal(0, owner.Channel.EditsAsked);
        Assert.Equal(before, File.ReadAllBytes(_harness.VaultPath));
        Assert.DoesNotContain(owner.AuditLines(), line => line.Contains("\"terminal_edit\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_refusal_the_vault_gives_keeps_the_verbs_exit_code()
    {
        await using var owner = Owner(ApprovalAnswer.ApprovedOnce);

        using (Hold(OwnerKind.TerminalAgent))
        {
            _harness.AssertExit(CliApp.ExitNotFound, _harness.Run("field", "rm", "Work/absent", "KEY", "--vault", _harness.VaultPath));
        }

        Assert.Contains("keypaste field rm: no item 'Work/absent'", _harness.Err, StringComparison.Ordinal);
        Assert.Equal(0, owner.Channel.EditsAsked);
    }

    [Fact]
    public async Task An_env_pull_too_large_for_one_request_sends_nothing()
    {
        var lines = string.Concat(Enumerable.Range(0, 3000).Select(i => $"KEY_{i}={new string('v', 24)}\n"));
        await using var owner = Owner(ApprovalAnswer.ApprovedOnce);

        using (Hold(OwnerKind.DesktopApp))
        {
            _harness.AssertExit(
                CliApp.ExitInternalError,
                _harness.Run("env", "pull", "ci", DotEnv(lines), "--yes", "--keep", "--vault", _harness.VaultPath));
        }

        Assert.Contains("is too large to hand to", _harness.Err, StringComparison.Ordinal);
        Assert.Equal(0, owner.Channel.EditsAsked);
    }

    [Theory]
    [InlineData("access")]
    [InlineData("import")]
    public async Task Access_and_import_are_refused_even_with_a_holder_listening(string verb)
    {
        var source = Path.Combine(_harness.Directory, "source.kdbx");
        using (var created = Vault.Create(source, "source-pw"))
        {
            created.Save();
        }

        string[] args = verb == "access"
            ? ["access", "--password", "--vault", _harness.VaultPath]
            : ["import", source, "--vault", _harness.VaultPath];

        await using var owner = Owner(ApprovalAnswer.ApprovedOnce);

        using (Hold(OwnerKind.DesktopApp))
        {
            _harness.AssertExit(CliApp.ExitInternalError, _harness.Run(args));
        }

        Assert.Contains("this vault is already unlocked in", _harness.Err, StringComparison.Ordinal);
        Assert.Equal(0, owner.Channel.EditsAsked);
    }

    [Fact]
    public async Task Another_command_holding_the_vault_is_still_waited_for()
    {
        await using var owner = Owner(ApprovalAnswer.ApprovedOnce);

        using (Hold(OwnerKind.CommandLine))
        {
            _harness.Prompt.Enqueue("gh-after");
            _harness.AssertExit(CliApp.ExitInternalError, _harness.Run("set", "Work/github", "--vault", _harness.VaultPath));
        }

        Assert.Contains("this vault is in use by", _harness.Err, StringComparison.Ordinal);
        Assert.Equal(0, owner.Channel.EditsAsked);
    }
}
