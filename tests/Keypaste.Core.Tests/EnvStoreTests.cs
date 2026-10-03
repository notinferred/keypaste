using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Where a project's keys are written (D-0413), and the <c>env/&lt;project&gt;</c> layout of earlier
/// releases that stays readable and is still updated and removed in place.
/// </summary>
/// <remarks>
/// The tests that matter most here are the ones covering input keypaste would never produce
/// itself. Every one of them describes something a user can do in KeePassXC — an entry with a
/// name that is not a legal variable, two entries with the same name, an untagged home entry —
/// and keypaste has to have an answer for each that does not involve pretending the file says
/// something other than what it says (docs/PRODUCT.md law 4.6).
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
    /// Setting a legacy variable carries the entry's other fields across rather than rebuilding it
    /// from the two things env knows about. A user who annotated a variable in KeePassXC should not
    /// lose the note by rotating the secret, and the value it replaced stays in history (D-0014).
    /// </summary>
    [Fact]
    public void Set_ALegacyVariable_UpdatesItInPlace_KeepingTheOtherFields_AndHistory()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        vault.AddEntry(new VaultEntry
        {
            Title = "TOKEN",
            Password = "first",
            Username = "set-in-keepassxc",
            Url = "https://example.invalid/rotate",
            Notes = "rotate this quarterly",
            GroupPath = "env/billing",
        });
        var store = new EnvStore(vault);
        var token = new EntryName("env/billing", "TOKEN");

        var plan = store.Set("billing", "dev", "TOKEN", "second");
        store.Set("billing", "dev", "TOKEN", "third");

        Assert.Equal(new EnvKeyWrite("TOKEN", EnvWriteChange.Replaces, token, EnvSource.LegacyField), Assert.Single(plan.Keys));
        var entry = vault.Find(token);
        Assert.Equal("third", entry?.Password, StringComparer.Ordinal);
        Assert.Equal("set-in-keepassxc", entry?.Username, StringComparer.Ordinal);
        Assert.Equal("https://example.invalid/rotate", entry?.Url, StringComparer.Ordinal);
        Assert.Equal("rotate this quarterly", entry?.Notes, StringComparer.Ordinal);
        Assert.Equal(2, HistoryCount(vault, token));
        Assert.Null(vault.Find(_home));
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
        LegacyVariables.Set(vault, "billing", "Token", "first");

        var plan = new EnvStore(vault).Set("billing", "dev", "TOKEN", "second");

        Assert.Equal("'billing/dev' already has 'Token', which differs from 'TOKEN' only in case (env/billing/Token)", plan.Refusal);
        Assert.Null(vault.Find(_home));
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
    /// An entry titled <c>billing/TOKEN</c> sitting directly in <c>env</c> shares the path of the real
    /// <c>env/billing/TOKEN</c>, so a set could write the project's secret into an entry outside the
    /// project and report success.
    /// </summary>
    [Fact]
    public void Set_UpdatesTheProjectsOwnEntry_NotAnEntryWhosePathCollidesWithIt()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        vault.AddEntry(new VaultEntry { Title = "billing/TOKEN", Password = "foreign", GroupPath = "env" });
        vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "mine", GroupPath = "env/billing" });

        var plan = new EnvStore(vault).Set("billing", "dev", "TOKEN", "rotated");

        Assert.Equal(EnvWriteChange.Replaces, Assert.Single(plan.Keys).Change);
        Assert.Equal("rotated", vault.Find(new EntryName("env/billing", "TOKEN"))?.Password, StringComparer.Ordinal);
        Assert.Equal("foreign", vault.Find(new EntryName("env", "billing/TOKEN"))?.Password, StringComparer.Ordinal);
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
        var legacy = new EntryName("env/billing", "LEGACY_TOKEN");
        Tagged(vault, _stripe, ["env:billing"], ("STRIPE_KEY", "s1", true), ("STRIPE_WEBHOOK", "w1", true));
        Tagged(vault, database, ["env:billing"], ("DATABASE_URL", "d1", true));
        LegacyVariables.Set(vault, "billing", "LEGACY_TOKEN", "l1");
        var stripeBefore = HistoryCount(vault, _stripe);
        var databaseBefore = HistoryCount(vault, database);
        var store = new EnvStore(vault);

        var plan = store.Plan(
            "billing",
            "dev",
            [new("STRIPE_KEY", "s2"), new("STRIPE_WEBHOOK", "w2"), new("DATABASE_URL", "d1"), new("LEGACY_TOKEN", "l2"), new("NEW_ONE", "n"), new("NEW_TWO", "m")]);

        Assert.Equal(["NEW_ONE", "NEW_TWO"], plan.Created);
        Assert.Equal(["LEGACY_TOKEN", "STRIPE_KEY", "STRIPE_WEBHOOK"], plan.Updated);
        Assert.Equal(1, plan.Unchanged);
        Assert.True(store.TryApply(plan, out var rejection), rejection);

        Assert.Equal(stripeBefore + 1, HistoryCount(vault, _stripe));
        Assert.Equal(databaseBefore, HistoryCount(vault, database));
        Assert.Equal(1, HistoryCount(vault, legacy));
        Assert.Equal(0, HistoryCount(vault, _home));
        Assert.Equal(["NEW_ONE", "NEW_TWO"], vault.Fields(_home)!.Select(field => field.Name));
        Assert.Equal("l2", vault.Find(legacy)?.Password, StringComparer.Ordinal);
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

    /// <summary>
    /// Removing a legacy variable takes its history out of the project with it, and purging what
    /// it recycled is what erases a rotated value.
    /// </summary>
    [Fact]
    public void Remove_ALegacyVariable_RecyclesItsEntry_AndPurgingIsWhatErasesIt()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        var token = new EntryName("env/billing", "TOKEN");
        LegacyVariables.Set(vault, "billing", "TOKEN", "first");
        LegacyVariables.Set(vault, "billing", "TOKEN", "second");

        var removal = new EnvStore(vault).Remove("billing", "dev", "TOKEN");

        Assert.Equal(new EnvRemoval(EnvRemoveOutcome.Recycled, new EnvSource("TOKEN", token, EnvSource.LegacyField), string.Empty), removal);
        Assert.Equal(-1, HistoryCount(vault, token));

        var recycled = Assert.Single(vault.ReadRecycled());
        Assert.Equal("TOKEN", recycled.Title, StringComparer.Ordinal);
        Assert.Equal("env/billing", recycled.OriginalGroupPath, StringComparer.Ordinal);

        Assert.True(vault.PurgeRecycled(recycled.Id));
        Assert.Empty(vault.ReadRecycled());
    }

    /// <summary>
    /// An entry titled <c>nested/TOKEN</c> in <c>env/dev</c> and an entry titled <c>TOKEN</c> in
    /// <c>env/dev/nested</c> have the same <see cref="VaultEntry.Path"/>. KeePassXC makes the first
    /// of those; keypaste has to remove the one it was asked for and leave the other alone.
    /// </summary>
    [Fact]
    public void Remove_TheProjectsOwnVariable_LeavesANestedEntrySharingItsPath_AcrossAReopen()
    {
        var path = NewVaultPath();

        using (var vault = Vault.Create(path, MasterPassword))
        {
            vault.AddEntry(new VaultEntry { Title = "nested/TOKEN", Password = "slashed", GroupPath = "env/dev" });
            vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "nested", GroupPath = "env/dev/nested" });
            vault.AddEntry(new VaultEntry { Title = "KEEP", Password = "keep", GroupPath = "env/dev" });
            Assert.Equal(EnvRemoveOutcome.Recycled, new EnvStore(vault).Remove("dev", "dev", "nested/TOKEN").Outcome);
            vault.Save();
        }

        using var reopened = Vault.Open(path, MasterPassword);
        var rows = reopened.ReadEntries()
            .Select(entry => (entry.GroupPath, entry.Title, entry.Password))
            .Order();

        Assert.Equal(
            new[] { ("env/dev", "KEEP", "keep"), ("env/dev/nested", "TOKEN", "nested") },
            rows);
    }

    /// <summary>
    /// A project holding two entries with one title: KDBX permits it and KeePassXC will make it.
    /// There is no answer to which was meant, so removal refuses rather than delete whichever came first.
    /// </summary>
    [Fact]
    public void Remove_ADuplicatedVariableName_IsAmbiguous_AndRemovesNothing()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "first", GroupPath = "env/billing" });
        vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "second", GroupPath = "env/billing" });

        var removal = new EnvStore(vault).Remove("billing", "dev", "TOKEN");

        Assert.Equal(EnvRemoveOutcome.Ambiguous, removal.Outcome);
        Assert.Contains("is on more than one entry", removal.Refusal, StringComparison.Ordinal);
        Assert.Equal(2, vault.ReadEntries().Count);
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

    [Fact]
    public void Projects_ListsImmediateChildrenOfEnvOnly_OrdinalSorted()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        LegacyVariables.Set(vault, "web", "A", "v");
        new EnvStore(vault).Set("api", "dev", "A", "v");

        vault.AddEntry(new VaultEntry { Title = "A", Password = "v", GroupPath = "env/api/nested" });
        vault.AddEntry(new VaultEntry { Title = "A", Password = "v", GroupPath = "unrelated" });

        Assert.Equal(["api", "web"], new EnvStore(vault).Projects());
    }

    [Fact]
    public void Projects_IsEmpty_WhenTheVaultHasNoEnvGroup()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        vault.AddEntry(new VaultEntry { Title = "github", Password = "v" });

        Assert.Empty(new EnvStore(vault).Projects());
        Assert.False(new EnvStore(vault).ProjectExists("billing"));
    }

    /// <summary>
    /// A project whose variables were all removed still exists, and saying so is what lets
    /// <c>env ls</c> tell "no variables yet" apart from "no such project".
    /// </summary>
    [Fact]
    public void ProjectExists_IsTrue_ForAProjectWithNoVariablesLeft()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        LegacyVariables.Set(vault, "billing", "TOKEN", "v");
        var store = new EnvStore(vault);
        store.Remove("billing", "dev", "TOKEN");

        Assert.True(store.ProjectExists("billing"));
        Assert.Empty(EnvResolution.List(vault, "billing", "dev").Variables);
    }

    [Fact]
    public void Profiles_DevFirst_ProtectedLast()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        LegacyVariables.Set(vault, "acme-api", "A", "v");

        foreach (var profile in new[] { "prod", "staging", "production-eu", "alpha" })
        {
            LegacyVariables.Set(vault, "acme-api", profile, "A", "v");
        }

        var store = new EnvStore(vault);
        Assert.Equal(
            [
                new EnvProfileInfo("dev", false),
                new EnvProfileInfo("alpha", false),
                new EnvProfileInfo("staging", false),
                new EnvProfileInfo("prod", true),
                new EnvProfileInfo("production-eu", true),
            ],
            store.Profiles("acme-api"));
        Assert.Empty(store.Profiles("absent"));
        Assert.Equal(["acme-api"], store.Projects());
    }

    [Fact]
    public void ASubgroupNamedDev_IsReportedAndNeverRead()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        LegacyVariables.Set(vault, "acme-api", "A", "flat");
        vault.AddEntry(new VaultEntry { Title = "B", Password = "nested", GroupPath = "env/acme-api/dev" });
        var store = new EnvStore(vault);

        Assert.Equal([new EnvVariable("A", "flat")], EnvResolution.List(vault, "acme-api", "dev").Variables);
        Assert.Equal(["dev"], store.Profiles("acme-api").Select(profile => profile.Name));
        Assert.Equal(
            ["'env/acme-api/dev' is ignored: the dev profile is the project group itself; move its entries up"],
            store.ProfileProblems("acme-api"));
    }

    [Fact]
    public void AnInvalidSubgroup_IsReportedAndNeverRead()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        LegacyVariables.Set(vault, "acme-api", "A", "flat");
        vault.AddEntry(new VaultEntry { Title = "B", Password = "shouted", GroupPath = "env/acme-api/Prod" });
        var store = new EnvStore(vault);

        Assert.False(store.ProfileExists("acme-api", "Prod"));
        Assert.Equal(["dev"], store.Profiles("acme-api").Select(profile => profile.Name));

        var problem = Assert.Single(store.ProfileProblems("acme-api"));
        Assert.StartsWith("'env/acme-api/Prod' is ignored: ", problem, StringComparison.Ordinal);
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
