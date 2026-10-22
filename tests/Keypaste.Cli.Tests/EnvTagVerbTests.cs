using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Audit;
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

    /// <summary>
    /// Before y/N the verb names each variable joining whose value holds a KeePass placeholder, with its keyword, since
    /// it fills in from the item wherever the variable leaves (D-0465); never the value or what fills in.
    /// </summary>
    [Fact]
    public void Tag_names_each_joining_variable_holding_a_placeholder_by_keyword_before_asking()
    {
        using (var vault = Vault.Open(_harness.VaultPath, _master))
        {
            Assert.True(vault.SetFields(_stripe, [new FieldWrite("DATABASE_URL", "postgres://app:{PASSWORD}@db")]));
            vault.Save();
        }

        _harness.Prompt.Interactive = true;
        _harness.Stderr.GetStringBuilder().Clear();
        _harness.Prompt.Enqueue(_master, "n");

        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("env", "tag", "billing", "services/Stripe", "--vault", _harness.VaultPath));

        var said = _harness.Err.ReplaceLineEndings("\n");
        const string named = "DATABASE_URL fills in {PASSWORD} from services/Stripe when it leaves.";
        Assert.Contains("Joining billing/dev: DATABASE_URL, STRIPE_SECRET_KEY.", said, StringComparison.Ordinal);
        Assert.Contains(named, said, StringComparison.Ordinal);
        Assert.True(said.IndexOf(named, StringComparison.Ordinal) < said.IndexOf("Cancelled; nothing was written.", StringComparison.Ordinal));
        Assert.DoesNotContain("STRIPE_SECRET_KEY fills", said, StringComparison.Ordinal);
        Assert.DoesNotContain("stripe-login", said + _harness.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("postgres://", said + _harness.Out, StringComparison.Ordinal);
        Assert.Empty(Tags(_stripe));
    }

    /// <summary>
    /// Before y/N, as the app does, the verb names an agent set to Automatic where the tag puts the entry,
    /// and with this computer's <c>clients.toml</c> unreadable it names the agent the vault's settings
    /// would release it to once the file reads again.
    /// </summary>
    [Theory]
    [InlineData(null, "Automatic: claude-code would get services/Stripe without asking, each release recorded in history.")]
    [InlineData("this is not a clients file\n", "Automatic: by the vault's settings, claude-code would get services/Stripe without asking, each release recorded in history, once this computer's clients.toml can be read again; until then every agent request is refused.")]
    public void Tag_names_an_agent_set_to_Automatic_there_before_asking(string? clients, string said)
    {
        using (var vault = Vault.Open(_harness.VaultPath, _master))
        {
            Assert.True(ProjectScope.TryCreate("billing", "dev", out var dev, out var error), error);
            Assert.Equal(
                AgentAccessWrite.Written,
                new AgentAccessStore(vault).Write("claude-code", AgentAccess.Defaults with { Projects = [new ProjectAccess(dev, AgentMode.Automatic)] }));
            vault.Save();
        }

        if (clients is not null)
        {
            var path = KeypasteHome.ClientsPath(_harness.Environment[KeypasteHome.EnvironmentVariable]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, clients);
        }

        _harness.Prompt.Interactive = true;
        _harness.Stderr.GetStringBuilder().Clear();
        _harness.Prompt.Enqueue(_master, "n");

        _harness.AssertExit(CliApp.ExitUsageError, _harness.Run("env", "tag", "billing", "services/Stripe", "--vault", _harness.VaultPath));

        var shown = _harness.Err.ReplaceLineEndings("\n");
        Assert.Contains(said, shown, StringComparison.Ordinal);
        Assert.True(shown.IndexOf(said, StringComparison.Ordinal) < shown.IndexOf("Cancelled; nothing was written.", StringComparison.Ordinal));
        Assert.Empty(Tags(_stripe));
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
    public void Tagging_twice_or_untagging_an_entry_not_in_the_environment_writes_nothing()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "tag", "billing", "services/Stripe", "-p", "dev", "--yes"));
        var before = Bytes();

        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "tag", "billing", "services/Stripe", "--yes"));
        Assert.Contains("already in billing/dev", _harness.Err, StringComparison.Ordinal);
        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "untag", "billing", "services/Stripe", "-p", "prod", "--yes"));
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
        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "untag", "billing", "services/Stripe", "--yes"));

        Assert.Equal(["finance"], Tags(_stripe));
        Assert.Equal(before + 1, Revisions());
        Assert.Contains("Leaving billing/dev: STRIPE_SECRET_KEY.", _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Tagging_an_entry_that_is_not_there_is_not_found_and_writes_nothing()
    {
        var before = Bytes();

        _harness.AssertExit(CliApp.ExitNotFound, Run("env", "tag", "billing", "services/Nobody", "--yes"));

        Assert.Equal(before, Bytes());
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

    [Fact]
    public void Ls_of_a_project_known_only_from_tags_lists_its_environments_and_entries()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "tag", "billing", "services/Stripe", "--yes"));

        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "ls", "billing"));
        Assert.Equal("  dev\n    services/Stripe\n      STRIPE_SECRET_KEY\n  prod  protected\n    services/Database\n", Out);

        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "ls", "billing", "-p", "prod"));
        Assert.Equal("  prod  protected\n    services/Database\n", Out);

        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "ls", "billing", "--profiles"));
        Assert.Equal("dev\nprod\n", Out);

        _harness.AssertExit(CliApp.ExitNotFound, Run("env", "ls", "billing", "-p", "staging"));
    }

    /// <summary>An untagged entry under <c>env/</c>, as 0.3.0 wrote a variable, makes no project and joins none (D-0416).</summary>
    [Fact]
    public void Ls_reads_no_project_from_an_untagged_entry_under_env()
    {
        using (var vault = Vault.Open(_harness.VaultPath, _master))
        {
            vault.AddEntry(new VaultEntry { GroupPath = "env/billing", Title = "TOKEN", Password = "old-token" });
            vault.AddEntry(new VaultEntry { GroupPath = "env/acme", Title = "OLD_KEY", Password = "old-key" });
            vault.Save();
        }

        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "ls"));
        Assert.Equal("billing\n  prod  protected\n    services/Database\n", Out);

        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "ls", "billing"));
        Assert.Equal("  prod  protected\n    services/Database\n", Out);

        _harness.AssertExit(CliApp.ExitNotFound, Run("env", "ls", "acme"));
        Assert.Contains("no env set for 'acme'", _harness.Err, StringComparison.Ordinal);
    }
}
