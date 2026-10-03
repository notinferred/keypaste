using System.Text;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;
using Keypaste.Core;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>run</c>, <c>env export</c> and <c>env diff</c> on a project whose variables are tagged entries'
/// fields (C.1b), beside an untagged <c>env/&lt;project&gt;/OLD_KEY</c> as 0.3.0 wrote a variable, which
/// is no variable (D-0416). Against KeePassXC by <c>verify-keepassxc-projects.sh</c>.
/// </summary>
public sealed class TaggedProjectRunTests : IDisposable
{
    private const string _master = "tagged-run-master";

    private readonly CliHarness _harness = new();

    public TaggedProjectRunTests()
    {
        _harness.SeedVault(_master, ("services/Stripe", "stripe-login-c1b"), ("services/Database", "database-login-c1b"));

        using var vault = Vault.Open(_harness.VaultPath, _master);
        vault.AddEntry(new VaultEntry { GroupPath = "env/billing", Title = "OLD_KEY", Password = "old-value-c1b" });
        vault.SetFields(new EntryName("services", "Stripe"), [new FieldWrite("STRIPE_SECRET_KEY", "stripe-value-c1b"), new FieldWrite("Region", "eu", Protect: false)]);
        vault.SetFields(new EntryName("services", "Database"), [new FieldWrite("DATABASE_URL", "dev-db-c1b")]);
        vault.AddTag(new EntryName("services", "Stripe"), "env:billing");
        vault.AddTag(new EntryName("services", "Stripe"), "env:billing:staging");
        vault.AddTag(new EntryName("services", "Database"), "env:billing");
        vault.Save();
    }

    public void Dispose() => _harness.Dispose();

    private int Run(params string[] args)
    {
        _harness.Stdout.GetStringBuilder().Clear();
        _harness.Stderr.GetStringBuilder().Clear();
        _harness.Prompt.Enqueue(_master);

        // keypaste's own options go before run's --, which hands everything after it to the child.
        var separator = Array.IndexOf(args, "--");
        return _harness.Run(separator < 0
            ? [.. args, "--vault", _harness.VaultPath]
            : [.. args[..separator], "--vault", _harness.VaultPath, .. args[separator..]]);
    }

    private void Edit(Action<Vault> change)
    {
        using var vault = Vault.Open(_harness.VaultPath, _master);
        change(vault);
        vault.Save();
    }

    [Fact]
    public void Run_gives_the_child_exactly_the_tagged_fields_and_nothing_from_an_untagged_env_entry()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("run", "billing", "--", "node"));

        var environment = _harness.ProcessLauncher.Environment;
        Assert.Equal("stripe-value-c1b", environment["STRIPE_SECRET_KEY"]);
        Assert.Equal("dev-db-c1b", environment["DATABASE_URL"]);
        Assert.False(environment.ContainsKey("OLD_KEY"));
        Assert.DoesNotContain("old-value-c1b", environment.Values);
        Assert.False(environment.ContainsKey("Region"));
        Assert.DoesNotContain("-c1b", _harness.Out + _harness.Err, StringComparison.Ordinal);

        _harness.AssertExit(CliApp.ExitSuccess, Run("run", "billing", "-p", "staging", "--", "node"));
        Assert.Equal("stripe-value-c1b", _harness.ProcessLauncher.Environment["STRIPE_SECRET_KEY"]);
        Assert.False(_harness.ProcessLauncher.Environment.ContainsKey("DATABASE_URL"));
    }

    [Fact]
    public void Get_reads_the_untagged_env_entry_as_an_ordinary_entry()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("get", "env/billing/OLD_KEY", "--show"));

        Assert.Equal("old-value-c1b", _harness.Out.Trim());
    }

    public static TheoryData<string, string[]> Refusals => new()
    {
        { "two-entries", ["STRIPE_SECRET_KEY is on more than one entry (services/Database, services/Stripe)"] },
        { "expired", ["DATABASE_URL expired 2020-01-02 03:04:05Z (services/Database)"] },
        { "placeholder", ["DATABASE_URL holds the KeePass placeholder {PASSWORD}, which keypaste does not resolve (services/Database)"] },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void Each_refusal_starts_nothing_and_names_its_entries(string cause, string[] said)
    {
        Edit(vault =>
        {
            switch (cause)
            {
                case "two-entries":
                    vault.SetFields(new EntryName("services", "Database"), [new FieldWrite("STRIPE_SECRET_KEY", "second-value-c1b")]);
                    break;
                case "expired":
                    vault.SetExpiryUnchecked(new EntryName("services", "Database"), new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero));
                    break;
                default:
                    vault.SetFields(new EntryName("services", "Database"), [new FieldWrite("DATABASE_URL", "postgres://u:{PASSWORD}@db-c1b")]);
                    break;
            }
        });

        _harness.AssertExit(CliApp.ExitInternalError, Run("run", "billing", "--", "node"));

        Assert.Empty(_harness.ProcessLauncher.Started);
        Assert.Contains("'billing/dev' cannot be used, so nothing was started", _harness.Err, StringComparison.Ordinal);

        foreach (var text in said)
        {
            Assert.Contains(text, _harness.Err, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("-c1b", _harness.Out + _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Export_writes_a_reference_for_each_tagged_field_and_none_for_an_untagged_env_entry()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "export", "billing", "--stdout"));

        foreach (var key in new[] { "DATABASE_URL", "STRIPE_SECRET_KEY" })
        {
            Assert.Contains($"{key}=", _harness.Out, StringComparison.Ordinal);
            Assert.Contains($"kp://billing/dev/{key}", _harness.Out, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("OLD_KEY", _harness.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("Region", _harness.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("-c1b", _harness.Out + _harness.Err, StringComparison.Ordinal);

        var file = Path.Combine(Path.GetDirectoryName(_harness.VaultPath)!, ".env.keypaste");
        File.WriteAllText(file, _harness.Out);
        _harness.AssertExit(CliApp.ExitSuccess, Run("run", "--env-file", file, "--", "node"));
        Assert.Equal("stripe-value-c1b", _harness.ProcessLauncher.Environment["STRIPE_SECRET_KEY"]);
        Assert.False(_harness.ProcessLauncher.Environment.ContainsKey("OLD_KEY"));
    }

    [Fact]
    public void Export_refuses_a_key_two_entries_hold()
    {
        Edit(vault => vault.SetFields(new EntryName("services", "Database"), [new FieldWrite("STRIPE_SECRET_KEY", "second-value-c1b")]));

        _harness.AssertExit(CliApp.ExitInternalError, Run("env", "export", "billing", "--stdout"));

        Assert.Contains("STRIPE_SECRET_KEY is on more than one entry (services/Database, services/Stripe)", _harness.Err, StringComparison.Ordinal);
        Assert.Empty(_harness.Out);
    }

    [Fact]
    public void A_plaintext_export_refuses_a_custom_field_named_like_a_standard_one()
    {
        AddCustomString("Stripe", "PASSWORD", "custom-password-c1b");
        var path = Path.Combine(Path.GetDirectoryName(_harness.VaultPath)!, "billing.env");
        const string said = "PASSWORD is a custom field named like a standard one, which keypaste never releases (services/Stripe); nothing was written.";

        _harness.AssertExit(CliApp.ExitInternalError, Run("env", "export", "billing", "--dotenv", "--stdout"));
        Assert.Contains(said, _harness.Err, StringComparison.Ordinal);
        Assert.Empty(_harness.Out);

        _harness.AssertExit(CliApp.ExitInternalError, Run("env", "export", "billing", path, "--dotenv", "--yes"));
        Assert.Contains(said, _harness.Err, StringComparison.Ordinal);
        Assert.False(File.Exists(path));
        Assert.DoesNotContain("-c1b", _harness.Out + _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Diff_compares_the_tagged_environments_key_names()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "diff", "billing", "dev", "staging"));

        Assert.Contains("DATABASE_URL", _harness.Out, StringComparison.Ordinal);
        Assert.Contains("missing in staging", _harness.Out, StringComparison.Ordinal);
        Assert.Contains("same value in dev and staging", _harness.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("-c1b", _harness.Out + _harness.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Ls_names_each_tagged_entry_with_its_variables()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "ls", "billing"));

        Assert.Equal(
            "  dev\n    services/Database\n      DATABASE_URL\n    services/Stripe\n      STRIPE_SECRET_KEY\n  staging\n    services/Stripe\n      STRIPE_SECRET_KEY\n",
            _harness.Out.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Ls_lists_the_profiles_and_one_environment_from_tags()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "ls", "billing", "--profiles"));
        Assert.Equal("dev\nstaging\n", _harness.Out.ReplaceLineEndings("\n"));

        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "ls", "billing", "-p", "staging"));
        Assert.Equal("  staging\n    services/Stripe\n      STRIPE_SECRET_KEY\n", _harness.Out.ReplaceLineEndings("\n"));

        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "ls", "billing", "-p", "staging", "--json"));
        Assert.Contains("\"keys\":[\"STRIPE_SECRET_KEY\"]", _harness.Out.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);

        _harness.AssertExit(CliApp.ExitNotFound, Run("env", "ls", "billing", "-p", "qa"));
    }

    [Fact]
    public void Ls_lists_a_tagged_entry_under_env_by_its_fields_and_not_the_untagged_one_beside_it()
    {
        Edit(vault =>
        {
            var home = new EntryName("env/billing", ".env");
            vault.AddEntry(new VaultEntry { GroupPath = "env/billing", Title = ".env", Password = string.Empty });
            vault.SetFields(home, [new FieldWrite("HOME_KEY", "home-value-c1b")]);
            vault.AddTag(home, "env:billing");
        });

        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "ls", "billing"));

        var listed = _harness.Out.ReplaceLineEndings("\n");
        Assert.StartsWith("  dev\n    env/billing/.env\n      HOME_KEY\n", listed, StringComparison.Ordinal);
        Assert.DoesNotContain("OLD_KEY", listed, StringComparison.Ordinal);
        Assert.DoesNotContain("warning", _harness.Err, StringComparison.Ordinal);
    }

    // keypaste never writes a custom field named like a standard one (D-0369), but KeePassXC can.
    private void AddCustomString(string title, string field, string value)
    {
        CompositeKey key = new();
        key.AddUserKey(new KcpPassword(Encoding.UTF8.GetBytes(_master), false));
        PwDatabase database = new();

        try
        {
            database.Open(IOConnectionInfo.FromPath(_harness.VaultPath), key, null);
            var entry = database.RootGroup.GetEntries(true).Single(candidate => candidate.Strings.ReadSafe(PwDefs.TitleField) == title);
            entry.Strings.Set(field, new ProtectedString(true, value));
            database.Save(null);
        }
        finally
        {
            database.Close();
        }
    }
}
