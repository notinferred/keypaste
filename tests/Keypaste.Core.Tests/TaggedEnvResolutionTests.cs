using System.Text;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// A project's environment is its tagged entries' variable fields together with its legacy
/// <c>env/&lt;project&gt;</c> variables, released whole or refused naming each cause and entry (C.1b).
/// </summary>
public sealed class TaggedEnvResolutionTests : IDisposable
{
    private static readonly DateTimeOffset _now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-tagged-env-").FullName;
    private readonly ManualClock _clock = new(_now);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Tagged_fields_and_the_legacy_group_are_one_set_each_with_its_source()
    {
        using var vault = Saved(v =>
        {
            Legacy(v, "billing", "LEGACY_KEY", "legacy-value");
            Tagged(v, "services", "Stripe", ["env:billing", "finance"], ("STRIPE_SECRET_KEY", "stripe-value"), ("Region", "eu-value"));
            Tagged(v, "services", "Database", ["env:billing"], ("DATABASE_URL", "db-value"));
            Tagged(v, "services", "Prod", ["env:billing:prod"], ("PROD_ONLY", "prod-value"));
            Tagged(v, "services", "Other", ["env:other"], ("OTHER_KEY", "other-value"));
            Tagged(v, "services", "Odd", ["env:billing:Prod"], ("ODD_KEY", "odd-value"));
            Tagged(v, "services", "Untagged", [], ("LOOSE_KEY", "loose-value"));
        });

        var resolved = EnvResolution.Resolve(vault, "billing", _clock);

        Assert.Equal(EnvOutcome.Resolved, resolved.Outcome);
        Assert.Equal(
            [new EnvVariable("DATABASE_URL", "db-value"), new EnvVariable("LEGACY_KEY", "legacy-value"), new EnvVariable("STRIPE_SECRET_KEY", "stripe-value")],
            resolved.Variables);
        Assert.Equal(
            [
                new EnvSource("DATABASE_URL", new EntryName("services", "Database"), "DATABASE_URL"),
                new EnvSource("LEGACY_KEY", new EntryName("env/billing", "LEGACY_KEY"), EnvSource.LegacyField),
                new EnvSource("STRIPE_SECRET_KEY", new EntryName("services", "Stripe"), "STRIPE_SECRET_KEY"),
            ],
            resolved.Sources);
        Assert.Equal(resolved.Sources, resolved.Preview.Sources);
        Assert.False(resolved.RequiresLiveApproval);
        Assert.Equal([new EnvVariable("PROD_ONLY", "prod-value")], EnvResolution.Resolve(vault, "billing", "prod", _clock).Variables);
    }

    [Fact]
    public void Each_refusal_names_its_cause_and_entries_and_never_a_value()
    {
        using var vault = Saved(v =>
        {
            Legacy(v, "billing", "Api_Key", "legacy-api-value");
            Tagged(v, "services", "Stripe", ["env:billing"], ("API_KEY", "tagged-api-value"), ("SHARED", "shared-one-value"));
            Tagged(v, "services", "Twin", ["env:billing"], ("SHARED", "shared-two-value"));
            Tagged(v, "services", "Old", ["env:billing"], ("OLD_KEY", "old-value"));
            v.SetExpiryUnchecked(new EntryName("services", "Old"), _now.AddDays(-1));
            Tagged(v, "services", "Ref", ["env:billing"], ("REF_KEY", "prefix-{PASSWORD}-suffix"));
            Tagged(v, "services", "Json", ["env:billing"], ("JSON_KEY", "{\"json\":\"value\"}"));
        });

        var resolved = EnvResolution.Resolve(vault, "billing", _clock);

        Assert.Equal(EnvOutcome.Unusable, resolved.Outcome);
        Assert.Empty(resolved.Variables);
        Assert.Empty(resolved.Sources);
        Assert.Equal(
            [
                ("API_KEY", "differs only in case from 'Api_Key', which Windows treats as one variable (env/billing/Api_Key, services/Stripe)"),
                ("Api_Key", "differs only in case from 'API_KEY', which Windows treats as one variable (env/billing/Api_Key, services/Stripe)"),
                ("OLD_KEY", "expired 2026-09-23 12:00:00Z (services/Old)"),
                ("REF_KEY", "holds the KeePass placeholder {PASSWORD}, which keypaste does not resolve (services/Ref)"),
                ("SHARED", "is on more than one entry (services/Stripe, services/Twin)"),
            ],
            resolved.Problems.Select(problem => (problem.Key, problem.Reason)));

        var said = resolved.Refusal + string.Concat(resolved.Problems.Select(problem => problem.Key + problem.Reason));

        foreach (var value in new[] { "legacy-api-value", "tagged-api-value", "shared-one-value", "shared-two-value", "old-value", "prefix-", "-suffix", "json" })
        {
            Assert.DoesNotContain(value, said, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_custom_field_KeePassXC_named_like_a_standard_one_refuses_its_set_naming_the_entry()
    {
        var path = Path.Combine(_directory, "custom-password.kdbx");

        using (var created = Vault.Create(path, EnvStoreTests.MasterPassword))
        {
            Tagged(created, "services", "Stripe", ["env:billing"], ("STRIPE_KEY", "stripe-value"));
            created.Save();
        }

        AddCustomString(path, "Stripe", "PASSWORD", "custom-password-value");
        using var vault = Vault.Open(path, EnvStoreTests.MasterPassword);

        var resolved = EnvResolution.Resolve(vault, "billing", _clock);

        Assert.Equal(EnvOutcome.Unusable, resolved.Outcome);
        Assert.Equal(
            ("PASSWORD", "is a custom field named like a standard one, which keypaste never releases (services/Stripe)"),
            (Assert.Single(resolved.Problems).Key, resolved.Problems[0].Reason));
        Assert.Equal(["PASSWORD", "STRIPE_KEY"], EnvResolution.List(vault, "billing", "dev").Sources.Select(source => source.Field));
        Assert.DoesNotContain("custom-password-value", resolved.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_subset_judges_only_the_keys_asked_for()
    {
        using var vault = Saved(v =>
        {
            Tagged(v, "services", "Stripe", ["env:billing"], ("STRIPE_KEY", "stripe-value"));
            Tagged(v, "services", "Ref", ["env:billing"], ("REF_KEY", "{S:other}"));
        });

        var subset = EnvResolution.Resolve(vault, "billing", "dev", ["STRIPE_KEY"], _clock);

        Assert.Equal([new EnvVariable("STRIPE_KEY", "stripe-value")], subset.Variables);
        Assert.Equal([new EnvSource("STRIPE_KEY", new EntryName("services", "Stripe"), "STRIPE_KEY")], subset.Sources);
        Assert.Equal(
            "holds the KeePass placeholder {S:…}, which keypaste does not resolve (services/Ref)",
            Assert.Single(EnvResolution.Resolve(vault, "billing", "dev", ["REF_KEY"], _clock).Problems).Reason);
    }

    [Theory]
    [InlineData("env:billing:prod")]
    [InlineData("env:billing:Prod")]
    [InlineData("env:billing:production-eu")]
    public void A_member_also_tagged_into_a_protected_environment_makes_its_set_asked_live(string protecting)
    {
        using var vault = Saved(v =>
        {
            Tagged(v, "services", "Shared", ["env:billing", protecting], ("SHARED_KEY", "shared-value"));
            Tagged(v, "services", "Plain", ["env:billing:staging"], ("PLAIN_KEY", "plain-value"));
        });

        var dev = EnvResolution.Resolve(vault, "billing", _clock);

        Assert.Equal(EnvOutcome.Resolved, dev.Outcome);
        Assert.True(dev.RequiresLiveApproval);
        Assert.True(dev.Preview.RequiresLiveApproval);
        Assert.False(EnvResolution.Resolve(vault, "billing", "staging", _clock).RequiresLiveApproval);
    }

    [Fact]
    public void A_protected_environment_and_a_protected_legacy_group_are_asked_live()
    {
        using var vault = Saved(v =>
        {
            Tagged(v, "services", "Database", ["env:billing:prod"], ("DATABASE_URL", "prod-db"));
            Legacy(v, "shop", "prod", "SHOP_KEY", "shop-prod");
        });

        Assert.True(EnvResolution.Resolve(vault, "billing", "prod", _clock).RequiresLiveApproval);
        Assert.True(EnvResolution.Resolve(vault, "shop", "prod", _clock).RequiresLiveApproval);
    }

    [Fact]
    public void A_project_known_only_from_tags_has_exactly_its_tagged_environments()
    {
        using var vault = Saved(v =>
        {
            Tagged(v, "services", "Stripe", ["env:billing:staging"], ("STRIPE_KEY", "stripe-value"));
            Tagged(v, "services", "Empty", ["env:billing"]);
        });

        Assert.Equal([new EnvVariable("STRIPE_KEY", "stripe-value")], EnvResolution.Resolve(vault, "billing", "staging", _clock).Variables);

        var dev = EnvResolution.Resolve(vault, "billing", _clock);
        Assert.Equal(EnvOutcome.Resolved, dev.Outcome);
        Assert.Empty(dev.Variables);

        Assert.Equal(EnvOutcome.NoProfile, EnvResolution.Resolve(vault, "billing", "prod", _clock).Outcome);
        Assert.Equal(EnvOutcome.NoProject, EnvResolution.Resolve(vault, "absent", _clock).Outcome);
    }

    [Fact]
    public void An_entry_tagged_inside_the_legacy_group_gives_its_fields_and_not_its_title()
    {
        using var vault = Saved(v =>
        {
            Legacy(v, "billing", "LEGACY_KEY", "legacy-value");
            Tagged(v, "env/billing", ".env", ["env:billing"], ("HOME_KEY", "home-value"));
            Tagged(v, "env/billing", "ELSEWHERE", ["env:other"], ("OTHER_KEY", "other-value"));
        });

        Assert.Equal(
            [new EnvVariable("HOME_KEY", "home-value"), new EnvVariable("LEGACY_KEY", "legacy-value")],
            EnvResolution.Resolve(vault, "billing", _clock).Variables);
        Assert.Equal([new EnvVariable("OTHER_KEY", "other-value")], EnvResolution.Resolve(vault, "other", _clock).Variables);
    }

    [Fact]
    public void Recycled_entries_and_keypastes_own_groups_hold_no_project()
    {
        using var vault = Saved(v =>
        {
            Tagged(v, "services", "Kept", ["env:billing"], ("KEPT_KEY", "kept-value"));
            var gone = Tagged(v, "services", "Gone", ["env:billing"], ("GONE_KEY", "gone-value"));
            Assert.Equal(DeletionOutcome.Recycled, v.RemoveEntry(gone));
            Tagged(v, ".keypaste/tokens", "Hidden", ["env:billing"], ("HIDDEN_KEY", "hidden-value"));
        });

        Assert.Equal([new EnvVariable("KEPT_KEY", "kept-value")], EnvResolution.Resolve(vault, "billing", _clock).Variables);
    }

    [Fact]
    public void The_matrix_and_the_listing_read_tagged_environments_with_their_sources()
    {
        using var vault = Saved(v =>
        {
            Tagged(v, "services", "Stripe", ["env:billing", "env:billing:staging"], ("STRIPE_KEY", "stripe-value"));
            Tagged(v, "services", "Database", ["env:billing:staging"], ("DATABASE_URL", "db-value"));
        });

        var matrix = EnvMatrix.Build(vault, "billing", _clock);

        Assert.Equal(["dev", "staging"], matrix.Profiles.Select(profile => profile.Name));
        Assert.Equal(["DATABASE_URL", "STRIPE_KEY"], matrix.Rows.Select(row => row.Key));
        Assert.Equal([EnvCellState.Missing, EnvCellState.Set], matrix.Row("DATABASE_URL")!.Cells.Select(cell => cell.State));
        Assert.Equal(["staging"], matrix.Row("STRIPE_KEY")!.Cells[0].SameValueAs);
        Assert.Equal([new EntryName("services", "Stripe")], matrix.Row("STRIPE_KEY")!.Cells[1].Sources);
        Assert.Equal(
            [new EnvDiffLine("DATABASE_URL", EnvDiffKind.Missing, "dev", null), new EnvDiffLine("STRIPE_KEY", EnvDiffKind.SameValue, "dev", "staging")],
            EnvDiff.Compare(matrix, "dev", "staging"));

        var listing = EnvResolution.List(vault, "billing", "staging");
        Assert.Equal(EnvOutcome.Resolved, listing.Outcome);
        Assert.Equal(["DATABASE_URL", "STRIPE_KEY"], listing.Sources.Select(source => source.Key));
        Assert.Equal(EnvOutcome.NoProfile, EnvResolution.List(vault, "billing", "prod").Outcome);
    }

    [Fact]
    public void A_reference_to_a_tagged_field_resolves_with_its_source()
    {
        using var vault = Saved(v => Tagged(v, "services", "Stripe", ["env:billing:prod"], ("STRIPE_KEY", "stripe-value")));

        Assert.True(EnvReferenceFile.TryParse("STRIPE=kp://billing/prod/STRIPE_KEY\n"u8.ToArray(), out var document));

        var resolved = EnvReferenceResolution.Resolve(vault, document, _clock);

        Assert.Equal([new EnvVariable("STRIPE", "stripe-value")], resolved.Variables);
        Assert.Equal([new EnvSource("STRIPE", new EntryName("services", "Stripe"), "STRIPE_KEY")], resolved.Sources);
        Assert.True(resolved.RequiresLiveApproval);
    }

    [Theory]
    [InlineData("{PASSWORD}", "{PASSWORD}")]
    [InlineData("a{password}b", "{PASSWORD}")]
    [InlineData("{USERNAME}:{URL}", "{USERNAME}")]
    [InlineData("{S:API Key}", "{S:…}")]
    [InlineData("{REF:P@I:0123}", "{REF:…}")]
    [InlineData("{DT_SIMPLE}", "{DT_SIMPLE}")]
    [InlineData("{t-conv:/x/upper/}", "{T-CONV:…}")]
    [InlineData("{\"a\":{\"b\":1}}", null)]
    [InlineData("{}", null)]
    [InlineData("{PASSWORDS}", null)]
    [InlineData("${HOME}", null)]
    [InlineData("{PASSWORD", null)]
    public void A_placeholder_is_found_by_name_and_other_braces_are_not(string value, string? found) =>
        Assert.Equal(found, KeePassPlaceholders.Find(value));

    private Vault Saved(Action<Vault> build)
    {
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".kdbx");

        using (var created = Vault.Create(path, EnvStoreTests.MasterPassword))
        {
            build(created);
            created.Save();
        }

        return Vault.Open(path, EnvStoreTests.MasterPassword);
    }

    private static void Legacy(Vault vault, string project, string key, string value) =>
        LegacyVariables.Set(vault, project, key, value);

    private static void Legacy(Vault vault, string project, string profile, string key, string value) =>
        LegacyVariables.Set(vault, project, profile, key, value);

    private static EntryName Tagged(Vault vault, string group, string title, string[] tags, params (string Name, string Value)[] fields)
    {
        var name = new EntryName(group, title);
        vault.AddEntry(new VaultEntry { GroupPath = group, Title = title, Password = "password-of-" + title });

        if (fields.Length > 0)
        {
            Assert.True(vault.SetFields(name, [.. fields.Select(field => new FieldWrite(field.Name, field.Value))]));
        }

        foreach (var tag in tags)
        {
            Assert.True(vault.AddTag(name, tag));
        }

        return name;
    }

    /// <summary>Adds a custom string as KeePassXC can, under a name keypaste refuses to write.</summary>
    private static void AddCustomString(string path, string title, string field, string value)
    {
        CompositeKey key = new();
        key.AddUserKey(new KcpPassword(Encoding.UTF8.GetBytes(EnvStoreTests.MasterPassword), false));
        PwDatabase database = new();

        try
        {
            database.Open(IOConnectionInfo.FromPath(path), key, null);
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
