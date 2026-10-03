using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Where a project's keys are written and removed (D-0413): on the entries tagged into the
/// environment, never on an untagged entry under <c>env/&lt;project&gt;</c> (D-0416).
/// </summary>
/// <remarks>
/// The tests that matter most here are the ones covering input keypaste would never produce
/// itself. Every one of them describes something a user can do in KeePassXC — an untagged entry
/// titled like a key, two entries holding one key, an untagged home entry — and keypaste has to
/// have an answer for each that does not involve pretending the file says something other than
/// what it says (docs/PRODUCT.md law 4.6).
/// </remarks>
public sealed class EnvStoreTests : IDisposable
{
    internal const string MasterPassword = "correct horse battery staple";

    private static readonly EntryName _home = new("env/billing", ".env");
    private static readonly EntryName _stripe = new("services", "Stripe");

    private readonly string _directory;

    public EnvStoreTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-env-tests-").FullName;
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void HomeEntry_IsDotEnvForDev_AndDotEnvDotNameForAnother()
    {
        Assert.Equal(new EntryName("env/billing", ".env"), EnvStore.HomeEntry("billing", "dev"));
        Assert.Equal(new EntryName("env/billing", ".env.staging"), EnvStore.HomeEntry("billing", "staging"));
    }

    [Fact]
    public void Set_ANewKey_CreatesTheHomeEntryTagged_HoldingItProtected_WithNoRevision_AndSurvivesAReopen()
    {
        var path = NewVaultPath();

        using (var vault = Vault.Create(path, MasterPassword))
        {
            var plan = new EnvStore(vault).Set("billing", "dev", "DATABASE_URL", "postgres://x");

            Assert.Null(plan.Refusal);
            Assert.True(plan.CreatesHome);
            Assert.Equal(new EnvKeyWrite("DATABASE_URL", EnvWriteChange.New, _home, "DATABASE_URL"), Assert.Single(plan.Keys));
            vault.Save();
        }

        using var reopened = Vault.Open(path, MasterPassword);

        Assert.Equal(["env:billing"], reopened.Tags(_home));
        Assert.Equal([new EntryField("DATABASE_URL", IsProtected: true, IsReadOnly: false)], reopened.Fields(_home));
        Assert.Equal("postgres://x", reopened.ReadField(_home, "DATABASE_URL"), StringComparer.Ordinal);
        Assert.Equal(0, HistoryCount(reopened, _home));
        Assert.Null(reopened.Find(new EntryName("env/billing", "DATABASE_URL")));

        var resolved = EnvResolution.Resolve(reopened, "billing", TimeProvider.System);
        Assert.Equal([new EnvVariable("DATABASE_URL", "postgres://x")], resolved.Variables);
        Assert.Equal([new EnvSource("DATABASE_URL", _home, "DATABASE_URL")], resolved.Sources);
    }

    [Fact]
    public void Set_ANewKeyInAnotherEnvironment_GoesOnThatEnvironmentsOwnHomeEntry()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        var plan = new EnvStore(vault).Set("billing", "staging", "DATABASE_URL", "staging-db");

        var home = new EntryName("env/billing", ".env.staging");
        Assert.Equal(home, Assert.Single(plan.Keys).Entry);
        Assert.Equal(["env:billing:staging"], vault.Tags(home));
        Assert.DoesNotContain("env/billing/staging", vault.ReadGroupPaths());
        Assert.Empty(EnvResolution.List(vault, "billing", "dev").Variables);
        Assert.Equal([new EnvVariable("DATABASE_URL", "staging-db")], EnvResolution.List(vault, "billing", "staging").Variables);
    }

    [Fact]
    public void Set_ASecondNewKey_GoesOnTheExistingHomeEntry_AsOneRevision()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        var store = new EnvStore(vault);
        store.Set("billing", "dev", "FIRST", "1");

        var plan = store.Set("billing", "dev", "SECOND", "2");

        Assert.False(plan.CreatesHome);
        Assert.Equal(["FIRST", "SECOND"], vault.Fields(_home)!.Select(field => field.Name));
        Assert.Equal(1, HistoryCount(vault, _home));
    }

    [Fact]
    public void Set_AKeyATaggedEntryHolds_UpdatesItThere_Protected_AsOneRevision()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        Tagged(vault, _stripe, ["env:billing"], ("STRIPE_KEY", "first", false));
        var before = HistoryCount(vault, _stripe);

        var plan = new EnvStore(vault).Set("billing", "dev", "STRIPE_KEY", "second");

        Assert.Equal(new EnvKeyWrite("STRIPE_KEY", EnvWriteChange.Replaces, _stripe, "STRIPE_KEY"), Assert.Single(plan.Keys));
        Assert.Equal("second", vault.ReadField(_stripe, "STRIPE_KEY"), StringComparer.Ordinal);
        Assert.True(vault.Fields(_stripe)!.Single().IsProtected);
        Assert.Equal(before + 1, HistoryCount(vault, _stripe));
        Assert.Null(vault.Find(_home));
    }

    /// <summary>
    /// An untagged entry under <c>env/&lt;project&gt;</c> titled like the key is no variable (D-0416),
    /// so the key is new to the environment and the entry is left as it was.
    /// </summary>
    [Fact]
    public void Set_AKeyAnUntaggedEntryUnderEnvIsTitled_GoesOnTheHomeEntry_AndLeavesThatEntryAlone()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        var token = new EntryName("env/billing", "TOKEN");
        vault.AddEntry(new VaultEntry { GroupPath = token.GroupPath, Title = token.Title, Password = "first" });

        var plan = new EnvStore(vault).Set("billing", "dev", "TOKEN", "second");

        Assert.Equal(new EnvKeyWrite("TOKEN", EnvWriteChange.New, _home, "TOKEN"), Assert.Single(plan.Keys));
        Assert.Equal("second", vault.ReadField(_home, "TOKEN"), StringComparer.Ordinal);
        Assert.Equal("first", vault.Find(token)?.Password, StringComparer.Ordinal);
        Assert.Equal(0, HistoryCount(vault, token));
    }

    /// <summary>
    /// A value identical to the one stored is not written, so a set or an import re-run after one
    /// edit does not burn through the ten history items the format keeps.
    /// </summary>
    [Fact]
    public void Set_WithTheValueItAlreadyHas_WritesNothing()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        var store = new EnvStore(vault);
        store.Set("billing", "dev", "TOKEN", "same");
        vault.Save();

        var plan = store.Set("billing", "dev", "TOKEN", "same");

        Assert.Equal(EnvWriteChange.Unchanged, Assert.Single(plan.Keys).Change);
        Assert.False(plan.WritesAnything);
        Assert.Equal(0, HistoryCount(vault, _home));
        Assert.Equal(SavedRead.Current, vault.ReadSaved(out _));
    }

    [Fact]
    public void Set_WithAnEntry_PutsANewKeyOnThatTaggedEntry()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        Tagged(vault, _stripe, ["env:billing"]);

        var plan = new EnvStore(vault).Set("billing", "dev", "WEBHOOK_SECRET", "whsec", _stripe);

        Assert.Equal(new EnvKeyWrite("WEBHOOK_SECRET", EnvWriteChange.New, _stripe, "WEBHOOK_SECRET"), Assert.Single(plan.Keys));
        Assert.Equal("whsec", vault.ReadField(_stripe, "WEBHOOK_SECRET"), StringComparer.Ordinal);
        Assert.Null(vault.Find(_home));
    }

    [Fact]
    public void Set_WithAnEntryNotInTheEnvironment_IsRefused_AndWritesNothing()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        Tagged(vault, _stripe, ["env:billing:prod"]);
        vault.Save();

        var untagged = new EnvStore(vault).Set("billing", "dev", "WEBHOOK_SECRET", "whsec", _stripe);
        var missing = new EnvStore(vault).Set("billing", "dev", "WEBHOOK_SECRET", "whsec", new EntryName("services", "Absent"));

        Assert.Equal("services/Stripe is not in 'billing/dev'; tag it into the environment first", untagged.Refusal);
        Assert.Equal("there is no entry services/Absent", missing.Refusal);
        Assert.Equal(SavedRead.Current, vault.ReadSaved(out _));
    }

    [Fact]
    public void Set_AKeyOnTwoEntries_IsRefusedNamingBoth_UnlessTheEntryIsNamed()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        var twin = new EntryName("services", "Twin");
        Tagged(vault, _stripe, ["env:billing"], ("STRIPE_KEY", "one", true));
        Tagged(vault, twin, ["env:billing"], ("STRIPE_KEY", "two", true));
        var store = new EnvStore(vault);

        Assert.Equal(
            "STRIPE_KEY is on more than one entry (services/Stripe, services/Twin); name the one to write",
            store.Set("billing", "dev", "STRIPE_KEY", "three").Refusal);

        Assert.Null(store.Set("billing", "dev", "STRIPE_KEY", "three", twin).Refusal);
        Assert.Equal("one", vault.ReadField(_stripe, "STRIPE_KEY"), StringComparer.Ordinal);
        Assert.Equal("three", vault.ReadField(twin, "STRIPE_KEY"), StringComparer.Ordinal);
    }

    [Fact]
    public void Set_WithAnEntry_StillUpdatesAKeyOneOtherEntryHoldsWhereItLives()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        var other = new EntryName("services", "Other");
        Tagged(vault, _stripe, ["env:billing"], ("STRIPE_KEY", "one", true));
        Tagged(vault, other, ["env:billing"]);

        var plan = new EnvStore(vault).Set("billing", "dev", "STRIPE_KEY", "two", other);

        Assert.Equal(new EnvKeyWrite("STRIPE_KEY", EnvWriteChange.Replaces, _stripe, "STRIPE_KEY"), Assert.Single(plan.Keys));
        Assert.Equal("two", vault.ReadField(_stripe, "STRIPE_KEY"), StringComparer.Ordinal);
        Assert.Null(vault.ReadField(other, "STRIPE_KEY"));
    }

    /// <summary>
    /// A new key must be a field a project releases (D-0388), and the project and environment
    /// names a tag can hold. Each of these would otherwise be written and then never reach a child.
    /// </summary>
    [Theory]
    [InlineData("billing", "dev", "api_key")]
    [InlineData("billing", "dev", "Mixed_Case")]
    [InlineData("billing", "dev", "KPXC_X")]
    [InlineData("billing", "dev", "URL")]
    [InlineData("billing", "dev", "PASSWORD")]
    [InlineData("billing", "dev", "WITH-DASH")]
    [InlineData("billing", "dev", "2LEADING_DIGIT")]
    [InlineData("billing", "dev", "")]
    [InlineData("", "dev", "TOKEN")]
    [InlineData("a/b", "dev", "TOKEN")]
    [InlineData("bill:ing", "dev", "TOKEN")]
    [InlineData(" billing", "dev", "TOKEN")]
    [InlineData("billing", "Staging", "TOKEN")]
    public void Set_RefusesWhatNoProjectCouldRelease(string project, string environment, string key)
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);

        var plan = new EnvStore(vault).Set(project, environment, key, "value");

        Assert.False(string.IsNullOrEmpty(plan.Refusal));
        Assert.Empty(vault.ReadEntries());
    }

    /// <summary>
    /// <c>PATH</c> and <c>Path</c> are two variables on Linux and one on Windows. Allowing both
    /// into an environment would make the injected set depend on which machine ran the command.
    /// </summary>
    [Fact]
    public void Set_RefusesAKeyDifferingOnlyInCaseFromOneTheEnvironmentHas()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        ProjectVariables.Set(vault, "billing", "TOKEN", "first");

        var plan = new EnvStore(vault).Set("billing", "dev", "Token", "second");

        Assert.Equal("'billing/dev' already has 'TOKEN', which differs from 'Token' only in case (env/billing/.env)", plan.Refusal);
        Assert.Equal(["TOKEN"], vault.Fields(_home)!.Select(field => field.Name));
    }

    [Fact]
    public void CaseCollision_IsCheckedWithinTheEnvironment()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        var store = new EnvStore(vault);
        store.Set("acme-api", "dev", "TOKEN", "dev");

        Assert.Null(store.Set("acme-api", "staging", "TOKEN_TWO", "staging").Refusal);
        Assert.Contains("only in case", store.Set("acme-api", "staging", "Token_Two", "x").Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void Set_AHomeEntryNotTaggedIntoTheEnvironment_IsRefusedNamingIt()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        vault.AddEntry(new VaultEntry { GroupPath = "env/billing", Title = ".env", Password = "kept" });
        vault.AddEntry(new VaultEntry { GroupPath = "env/billing", Title = ".env.staging" });
        Assert.True(vault.AddTag(new EntryName("env/billing", ".env.staging"), "env:other:staging"));
        vault.Save();
        var store = new EnvStore(vault);

        Assert.Equal("env/billing/.env is not tagged env:billing; tag it into 'billing/dev' or rename it", store.Set("billing", "dev", "NEW_KEY", "v").Refusal);
        Assert.Equal(
            "env/billing/.env.staging is not tagged env:billing:staging; tag it into 'billing/staging' or rename it",
            store.Set("billing", "staging", "NEW_KEY", "v").Refusal);
        Assert.Equal(SavedRead.Current, vault.ReadSaved(out _));
    }

    /// <summary>
    /// An entry titled <c>billing/.env</c> sitting directly in <c>env</c> shares the path of the home
    /// entry <c>env/billing/.env</c>, so a set could write the project's secret into an entry outside
    /// the project and report success.
    /// </summary>
    [Fact]
    public void Set_UpdatesTheProjectsOwnEntry_NotAnEntryWhosePathCollidesWithIt()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        ProjectVariables.Set(vault, "billing", "TOKEN", "mine");
        vault.AddEntry(new VaultEntry { Title = "billing/.env", Password = "foreign", GroupPath = "env" });

        var plan = new EnvStore(vault).Set("billing", "dev", "TOKEN", "rotated");

        Assert.Equal(EnvWriteChange.Replaces, Assert.Single(plan.Keys).Change);
        Assert.Equal("rotated", vault.ReadField(_home, "TOKEN"), StringComparer.Ordinal);
        Assert.Null(vault.ReadField(new EntryName("env", "billing/.env"), "TOKEN"));
        Assert.Equal("foreign", vault.Find(new EntryName("env", "billing/.env"))?.Password, StringComparer.Ordinal);
    }

    [Fact]
    public void Set_AllowsAnEmptyValue()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);

        Assert.Null(new EnvStore(vault).Set("billing", "dev", "OPTIONAL", string.Empty).Refusal);
        Assert.Equal(string.Empty, vault.ReadField(_home, "OPTIONAL"), StringComparer.Ordinal);
    }

    /// <summary>One plan touching three entries costs each one revision, and a created home entry none.</summary>
    [Fact]
    public void TryApply_WritesEachEntryOnce()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        var database = new EntryName("services", "Database");
        Tagged(vault, _stripe, ["env:billing"], ("STRIPE_KEY", "s1", true), ("STRIPE_WEBHOOK", "w1", true));
        Tagged(vault, database, ["env:billing"], ("DATABASE_URL", "d1", true));
        var stripeBefore = HistoryCount(vault, _stripe);
        var databaseBefore = HistoryCount(vault, database);
        var store = new EnvStore(vault);

        var plan = store.Plan(
            "billing",
            "dev",
            [new("STRIPE_KEY", "s2"), new("STRIPE_WEBHOOK", "w2"), new("DATABASE_URL", "d1"), new("NEW_ONE", "n"), new("NEW_TWO", "m")]);

        Assert.Equal(["NEW_ONE", "NEW_TWO"], plan.Created);
        Assert.Equal(["STRIPE_KEY", "STRIPE_WEBHOOK"], plan.Updated);
        Assert.Equal(1, plan.Unchanged);
        Assert.True(store.TryApply(plan, out var rejection), rejection);

        Assert.Equal(stripeBefore + 1, HistoryCount(vault, _stripe));
        Assert.Equal(databaseBefore, HistoryCount(vault, database));
        Assert.Equal(0, HistoryCount(vault, _home));
        Assert.Equal(["NEW_ONE", "NEW_TWO"], vault.Fields(_home)!.Select(field => field.Name));
    }

    [Fact]
    public void Remove_AField_TakesItOffItsEntry_AndHistoryKeepsIt()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        var store = new EnvStore(vault);
        store.Set("billing", "dev", "TOKEN", "first");
        store.Set("billing", "dev", "OTHER", "kept");

        var removal = store.Remove("billing", "dev", "TOKEN");

        Assert.Equal(new EnvRemoval(EnvRemoveOutcome.FieldRemoved, new EnvSource("TOKEN", _home, "TOKEN"), string.Empty), removal);
        Assert.Equal(["OTHER"], vault.Fields(_home)!.Select(field => field.Name));
        Assert.Empty(vault.ReadRecycled());

        Assert.True(vault.RestoreRevision(_home, 0));
        Assert.Equal("first", vault.ReadField(_home, "TOKEN"), StringComparer.Ordinal);
    }

    [Fact]
    public void Remove_AKeyOnlyAnUntaggedEntryUnderEnvIsTitled_MatchesNothing_AndLeavesThatEntry()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        var token = new EntryName("env/billing", "TOKEN");
        ProjectVariables.Set(vault, "billing", "OTHER", "kept");
        vault.AddEntry(new VaultEntry { GroupPath = token.GroupPath, Title = token.Title, Password = "untouched" });

        var removal = new EnvStore(vault).Remove("billing", "dev", "TOKEN");

        Assert.Equal(EnvRemoveOutcome.NothingMatched, removal.Outcome);
        Assert.Equal("untouched", vault.Find(token)?.Password, StringComparer.Ordinal);
        Assert.Empty(vault.ReadRecycled());
    }

    [Fact]
    public void Remove_AKeyOnTwoEntries_TakesItOnlyFromTheEntryNamed()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        var twin = new EntryName("services", "Twin");
        Tagged(vault, _stripe, ["env:billing"], ("STRIPE_KEY", "one", true));
        Tagged(vault, twin, ["env:billing"], ("STRIPE_KEY", "two", true));
        var store = new EnvStore(vault);

        Assert.Equal(EnvRemoveOutcome.Ambiguous, store.Remove("billing", "dev", "STRIPE_KEY").Outcome);
        Assert.Equal(EnvRemoveOutcome.FieldRemoved, store.Remove("billing", "dev", "STRIPE_KEY", twin).Outcome);
        Assert.Equal("one", vault.ReadField(_stripe, "STRIPE_KEY"), StringComparer.Ordinal);
        Assert.Null(vault.ReadField(twin, "STRIPE_KEY"));
    }

    [Fact]
    public void Remove_MatchesNothing_WhenTheKeyProjectOrEnvironmentIsAbsent()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        var store = new EnvStore(vault);
        store.Set("billing", "dev", "TOKEN", "first");

        Assert.Equal(EnvRemoveOutcome.NothingMatched, store.Remove("billing", "dev", "NOT_THERE").Outcome);
        Assert.Equal(EnvRemoveOutcome.NothingMatched, store.Remove("no-such-project", "dev", "TOKEN").Outcome);
        Assert.Equal(EnvRemoveOutcome.NothingMatched, store.Remove("billing", "qa", "TOKEN").Outcome);
        Assert.Equal(EnvRemoveOutcome.NothingMatched, store.Remove("billing", "Prod", "TOKEN").Outcome);
        Assert.Equal(["TOKEN"], vault.Fields(_home)!.Select(field => field.Name));
    }

    /// <summary>
    /// The home entry keeps its tag when its last key is removed, so the project still exists, and
    /// saying so is what lets <c>env ls</c> tell "no variables yet" apart from "no such project".
    /// </summary>
    [Fact]
    public void Remove_TheLastKey_LeavesTheProjectWithAnEmptyEnvironment()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        var store = new EnvStore(vault);
        store.Set("billing", "dev", "TOKEN", "v");

        Assert.Equal(EnvRemoveOutcome.FieldRemoved, store.Remove("billing", "dev", "TOKEN").Outcome);

        var listing = EnvResolution.List(vault, "billing", "dev");
        Assert.Equal(EnvOutcome.Resolved, listing.Outcome);
        Assert.Empty(listing.Variables);
        Assert.Equal(["billing"], ProjectCatalog.Read(vault).Projects.Select(project => project.Name));
    }

    private string NewVaultPath()
    {
        return System.IO.Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".kdbx");
    }

    /// <summary>History items on one entry, or -1 when no entry answers to that name.</summary>
    private static int HistoryCount(Vault vault, EntryName name)
    {
        return vault.ReadHistory(name)?.Count ?? -1;
    }

    /// <summary>An ordinary entry carrying tags and fields, as KeePassXC would hold one.</summary>
    private static void Tagged(Vault vault, EntryName name, string[] tags, params (string Name, string Value, bool Protect)[] fields)
    {
        vault.AddEntry(new VaultEntry { GroupPath = name.GroupPath, Title = name.Title, Password = "login" });

        if (fields.Length > 0)
        {
            Assert.True(vault.SetFields(name, [.. fields.Select(field => new FieldWrite(field.Name, field.Value, field.Protect))]));
        }

        foreach (var tag in tags)
        {
            Assert.True(vault.AddTag(name, tag));
        }
    }
}
