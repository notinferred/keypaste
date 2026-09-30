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
/// fields beside one legacy <c>env/&lt;project&gt;</c> variable (C.1b). Against KeePassXC by
/// <c>verify-keepassxc-projects.sh</c>.
/// </summary>
public sealed class TaggedProjectRunTests : IDisposable
{
    private const string _master = "tagged-run-master";

    private readonly CliHarness _harness = new();

    public TaggedProjectRunTests()
    {
        _harness.SeedVault(_master, ("services/Stripe", "stripe-login-c1b"), ("services/Database", "database-login-c1b"));

        using var vault = Vault.Open(_harness.VaultPath, _master);
        Assert.NotEqual(EnvSetOutcome.Rejected, new EnvStore(vault).TrySet("billing", "LEGACY_TOKEN", "legacy-value-c1b", out _));
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
    public void Run_gives_the_child_exactly_the_tagged_fields_and_the_legacy_variable()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("run", "billing", "--", "node"));

        var environment = _harness.ProcessLauncher.Environment;
        Assert.Equal("stripe-value-c1b", environment["STRIPE_SECRET_KEY"]);
        Assert.Equal("dev-db-c1b", environment["DATABASE_URL"]);
        Assert.Equal("legacy-value-c1b", environment["LEGACY_TOKEN"]);
        Assert.False(environment.ContainsKey("Region"));
        Assert.DoesNotContain("-c1b", _harness.Out + _harness.Err, StringComparison.Ordinal);

        _harness.AssertExit(CliApp.ExitSuccess, Run("run", "billing", "-p", "staging", "--", "node"));
        Assert.Equal("stripe-value-c1b", _harness.ProcessLauncher.Environment["STRIPE_SECRET_KEY"]);
        Assert.False(_harness.ProcessLauncher.Environment.ContainsKey("DATABASE_URL"));
    }

    public static TheoryData<string, string[]> Refusals => new()
    {
        { "two-entries", ["STRIPE_SECRET_KEY is on more than one entry (services/Database, services/Stripe)"] },
        { "case", ["Api_Key differs only in case from 'API_KEY'", "(env/billing/Api_Key, services/Stripe)"] },
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
                case "case":
                    new EnvStore(vault).TrySet("billing", "Api_Key", "legacy-api-c1b", out _);
                    vault.SetFields(new EntryName("services", "Stripe"), [new FieldWrite("API_KEY", "tagged-api-c1b")]);
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
    public void Export_writes_a_reference_for_each_tagged_field_and_the_legacy_variable()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "export", "billing", "--stdout"));

        foreach (var key in new[] { "DATABASE_URL", "LEGACY_TOKEN", "STRIPE_SECRET_KEY" })
        {
            Assert.Contains($"{key}=", _harness.Out, StringComparison.Ordinal);
            Assert.Contains($"kp://billing/dev/{key}", _harness.Out, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("Region", _harness.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("-c1b", _harness.Out + _harness.Err, StringComparison.Ordinal);

        var file = Path.Combine(Path.GetDirectoryName(_harness.VaultPath)!, ".env.keypaste");
        File.WriteAllText(file, _harness.Out);
        _harness.AssertExit(CliApp.ExitSuccess, Run("run", "--env-file", file, "--", "node"));
        Assert.Equal("stripe-value-c1b", _harness.ProcessLauncher.Environment["STRIPE_SECRET_KEY"]);
        Assert.Equal("legacy-value-c1b", _harness.ProcessLauncher.Environment["LEGACY_TOKEN"]);
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
            "LEGACY_TOKEN\n\ntagged entries\n  dev\n    services/Database\n      DATABASE_URL\n    services/Stripe\n      STRIPE_SECRET_KEY\n  staging\n    services/Stripe\n      STRIPE_SECRET_KEY\n",
            _harness.Out.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Ls_takes_a_tag_only_environment_of_a_legacy_project_and_lists_it_among_its_profiles()
    {
        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "ls", "billing", "--profiles"));
        Assert.Equal("dev\nstaging\n", _harness.Out.ReplaceLineEndings("\n"));

        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "ls", "billing", "-p", "staging"));
        Assert.DoesNotContain("LEGACY_TOKEN", _harness.Out, StringComparison.Ordinal);
        Assert.Contains("  staging\n    services/Stripe\n      STRIPE_SECRET_KEY\n", _harness.Out.ReplaceLineEndings("\n"), StringComparison.Ordinal);

        _harness.AssertExit(CliApp.ExitSuccess, Run("env", "ls", "billing", "-p", "staging", "--json"));
        Assert.Contains("\"key\":\"STRIPE_SECRET_KEY\"", _harness.Out.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);

        _harness.AssertExit(CliApp.ExitNotFound, Run("env", "ls", "billing", "-p", "qa"));
    }

    [Fact]
    public void Ls_lists_a_tagged_entry_inside_the_legacy_group_by_its_fields_not_as_a_variable()
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
        Assert.StartsWith("LEGACY_TOKEN\n\ntagged entries\n", listed, StringComparison.Ordinal);
        Assert.Contains("    env/billing/.env\n      HOME_KEY\n", listed, StringComparison.Ordinal);
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
