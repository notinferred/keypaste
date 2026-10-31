using Keypaste.Core;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>env tag</c>, <c>env untag</c> and the project listing <c>env ls</c> builds from tags alone.
/// Tags are checked through the core, and against KeePassXC by <c>verify-keepassxc-projects.sh</c>.
/// </summary>
public sealed class EnvTagVerbTests : IDisposable
{
    private const string _master = "env-tag-master";

    private static readonly EntryName _stripe = new("services", "Stripe");

    private readonly CliHarness _harness = new();

    public EnvTagVerbTests()
    {
        _harness.SeedVault(_master, ("services/Stripe", "stripe-login"), ("services/Database", "database-password"), ("services/Odd", "odd"));

        using var vault = Vault.Open(_harness.VaultPath, _master);
        vault.SetFields(_stripe, [new FieldWrite("STRIPE_SECRET_KEY", "sk_test_tag"), new FieldWrite("Region", "eu", Protect: false)]);
        vault.AddTag(new EntryName("services", "Database"), "env:billing:prod");
        vault.AddTag(new EntryName("services", "Odd"), "env:billing:Prod");
        vault.Save();
    }

    public void Dispose() => _harness.Dispose();

    private int Run(params string[] args)
    {
        _harness.Stdout.GetStringBuilder().Clear();
        _harness.Stderr.GetStringBuilder().Clear();
        _harness.Prompt.Enqueue(_master);
        return _harness.Run([.. args, "--vault", _harness.VaultPath]);
    }

    private IReadOnlyList<string> Tags(EntryName name)
    {
        using var vault = Vault.Open(_harness.VaultPath, _master);
        return vault.Tags(name)!;
    }

    private string Out => _harness.Out.ReplaceLineEndings("\n");

    private byte[] Bytes() => File.ReadAllBytes(_harness.VaultPath);

    [Fact]
    public void Tag_names_the_environment_and_the_env_named_fields_that_join_then_writes_the_tag()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "tag", "billing", "services/Stripe", "-p", "prod", "--yes"));

        Assert.Equal(["env:billing:prod"], Tags(_stripe));
        Assert.Contains("tagged services/Stripe env:billing:prod", _harness.Err, StringComparison.Ordinal);
        Assert.Contains("Joining billing/prod: STRIPE_SECRET_KEY.", _harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("Region", _harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("sk_test_tag", _harness.Err + _harness.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Tag_asks_first_and_declined_writes_nothing()
    {
        var before = Bytes();
        _harness.Prompt.Interactive = true;
        _harness.Stderr.GetStringBuilder().Clear();
        _harness.Prompt.Enqueue(_master, "n");

        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("env", "tag", "billing", "services/Stripe", "-p", "prod", "--vault", _harness.VaultPath));

        var said = _harness.Err.ReplaceLineEndings("\n");
        Assert.Contains("env:billing:prod puts services/Stripe in billing/prod, a protected environment whose every release is asked live.", said, StringComparison.Ordinal);
        Assert.Contains("Joining billing/prod: STRIPE_SECRET_KEY.", said, StringComparison.Ordinal);
        Assert.True(said.IndexOf("Joining", StringComparison.Ordinal) < said.IndexOf("Cancelled; nothing was written.", StringComparison.Ordinal));
        Assert.Contains("Tag services/Stripe? [y/N] ", _harness.Prompt.PromptsSeen);
        Assert.Equal(before, Bytes());

        _harness.Prompt.Enqueue(_master, "y");
        _harness.AssertExit(CliApp.ExitSuccess, _harness.Run("env", "tag", "billing", "services/Stripe", "-p", "prod", "--vault", _harness.VaultPath));
        Assert.Equal(["env:billing:prod"], Tags(_stripe));
    }

    [Fact]
    public void Tag_and_untag_without_a_terminal_need_yes_and_write_nothing_without_it()
    {
        var before = Bytes();

        _harness.AssertExit(CliApp.ExitUsageError, Run("env", "tag", "billing", "services/Stripe"));
        Assert.Contains("--yes is required when stdin is not a terminal", _harness.Err, StringComparison.Ordinal);
        _harness.AssertExit(CliApp.ExitUsageError, Run("env", "untag", "billing", "services/Database", "-p", "prod"));

        Assert.Equal(before, Bytes());
    }

    [Fact]
    public void Tag_without_an_environment_writes_the_bare_project_tag_for_dev()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "tag", "billing", "services/Stripe", "--yes"));

        Assert.Equal(["env:billing"], Tags(_stripe));
    }

    [Fact]
    public void Untag_removes_every_tag_that_puts_the_entry_in_that_environment_in_one_revision()
    {
        using (var vault = Vault.Open(_harness.VaultPath, _master))
        {
            vault.AddTag(_stripe, "env:billing");
            vault.AddTag(_stripe, "env:billing:dev");
            vault.AddTag(_stripe, "finance");
            vault.Save();
        }

        int Revisions()
        {
            using var vault = Vault.Open(_harness.VaultPath, _master);
            return vault.ReadHistory(_stripe)!.Count;
        }

        var before = Revisions();
        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "untag", "billing", "services/Stripe", "--yes"));

        Assert.Equal(["finance"], Tags(_stripe));
        Assert.Equal(before + 1, Revisions());
        Assert.Contains("Leaving billing/dev: STRIPE_SECRET_KEY.", _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Ls_lists_projects_environments_protection_and_exactly_the_tagged_entries()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "tag", "billing", "services/Stripe", "--yes"));

        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "ls"));

        Assert.Equal("billing\n  dev\n    services/Stripe\n  prod  protected\n    services/Database\n", Out);
        Assert.Contains("'services/Odd' has the tag 'env:billing:Prod', which puts it in no project", _harness.Err, StringComparison.Ordinal);
        Assert.Contains("still asked about live", _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Ls_json_carries_each_environment_and_member()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "ls", "--json"));

        Assert.Equal(
            """[{"project":"billing","environments":[{"name":"prod","protected":true,"members":[{"path":"services/Database","group":"services","title":"Database"}]}]}]""" + "\n",
            Out);
    }
}
