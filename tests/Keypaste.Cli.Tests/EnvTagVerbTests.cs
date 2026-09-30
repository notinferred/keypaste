using Keypaste.Core;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>env tag</c>, <c>env untag</c> and the project listing <c>env ls</c> builds from tags and
/// legacy groups. Tags are checked through the core, and against KeePassXC by
/// <c>verify-keepassxc-projects.sh</c>.
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
    public void Tag_writes_the_environments_tag_and_names_the_env_named_fields_that_join()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "tag", "billing", "services/Stripe", "-p", "prod"));

        Assert.Equal(["env:billing:prod"], Tags(_stripe));
        Assert.Contains("tagged services/Stripe env:billing:prod", _harness.Err, StringComparison.Ordinal);
        Assert.Contains("joins billing/prod: STRIPE_SECRET_KEY", _harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("Region", _harness.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("sk_test_tag", _harness.Err + _harness.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Tag_without_an_environment_writes_the_bare_project_tag_for_dev()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "tag", "billing", "services/Stripe"));

        Assert.Equal(["env:billing"], Tags(_stripe));
    }

    [Fact]
    public void Tagging_twice_or_untagging_an_entry_not_in_the_environment_writes_nothing()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "tag", "billing", "services/Stripe", "-p", "dev"));
        var before = Bytes();

        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "tag", "billing", "services/Stripe"));
        Assert.Contains("already in billing/dev", _harness.Err, StringComparison.Ordinal);
        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "untag", "billing", "services/Stripe", "-p", "prod"));
        Assert.Contains("not in billing/prod", _harness.Err, StringComparison.Ordinal);

        Assert.Equal(before, Bytes());
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
        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "untag", "billing", "services/Stripe"));

        Assert.Equal(["finance"], Tags(_stripe));
        Assert.Equal(before + 1, Revisions());
        Assert.Contains("leaves billing/dev: STRIPE_SECRET_KEY", _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Tagging_an_entry_that_is_not_there_is_not_found_and_writes_nothing()
    {
        var before = Bytes();

        _harness.AssertExit(CliApp.ExitNotFound, Run("env", "tag", "billing", "services/Nobody"));

        Assert.Equal(before, Bytes());
    }

    [Fact]
    public void Ls_lists_projects_environments_protection_and_exactly_the_tagged_entries()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "tag", "billing", "services/Stripe"));

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
            """[{"project":"billing","legacy":false,"environments":[{"name":"prod","protected":true,"members":[{"path":"services/Database","group":"services","title":"Database"}]}]}]""" + "\n",
            Out);
    }

    [Fact]
    public void Ls_of_a_project_known_only_from_tags_lists_its_environments_and_entries()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "tag", "billing", "services/Stripe"));

        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "ls", "billing"));
        Assert.Equal("  dev\n    services/Stripe\n      STRIPE_SECRET_KEY\n  prod  protected\n    services/Database\n", Out);

        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "ls", "billing", "-p", "prod"));
        Assert.Equal("  prod  protected\n    services/Database\n", Out);

        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "ls", "billing", "--profiles"));
        Assert.Equal("dev\nprod\n", Out);

        _harness.AssertExit(CliApp.ExitNotFound, Run("env", "ls", "billing", "-p", "staging"));
    }

    [Fact]
    public void Ls_of_a_legacy_project_lists_its_variables_as_before_then_its_tagged_entries()
    {
        using (var vault = Vault.Open(_harness.VaultPath, _master))
        {
            new EnvStore(vault).TrySet("billing", "TOKEN", "legacy-token", out _);
            vault.Save();
        }

        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "ls", "billing"));

        Assert.Equal("TOKEN\n\ntagged entries\n  prod  protected\n    services/Database\n", Out);

        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "ls"));
        Assert.Equal("billing  legacy\n  dev\n  prod  protected\n    services/Database\n", Out);
    }
}
