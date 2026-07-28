using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The env storage convention (DECISIONS.md D-0014).
/// </summary>
/// <remarks>
/// The tests that matter most here are the ones covering input keypaste would never produce
/// itself. Every one of them describes something a user can do in KeePassXC — an entry with a
/// name that is not a legal variable, two entries with the same name, an entry with no title —
/// and keypaste has to have an answer for each that does not involve pretending the file says
/// something other than what it says (docs/PRODUCT.md law 4.6).
/// </remarks>
public sealed class EnvStoreTests : IDisposable
{
    internal const string MasterPassword = "correct horse battery staple";

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
    public void Set_WritesToEnvProjectKey_AndSurvivesAReopen()
    {
        var path = NewVaultPath();

        using (var vault = Vault.Create(path, MasterPassword))
        {
            var store = new EnvStore(vault);
            Assert.Equal(EnvSetOutcome.Created, store.TrySet("billing", "DATABASE_URL", "postgres://x", out _));
            vault.Save();
        }

        using var reopened = Vault.Open(path, MasterPassword);

        // The convention is a promise about where the value lands, not just that it round-trips:
        // KeePassXC users navigate to this path by hand.
        Assert.Equal("postgres://x", reopened.Find("env/billing/DATABASE_URL")?.Password, StringComparer.Ordinal);
        Assert.Equal(["DATABASE_URL"], new EnvStore(reopened).Read("billing").Select(v => v.Key));
    }

    [Fact]
    public void Set_OnAnExistingKey_ReplacesTheValue_AndReportsItAsAnUpdate()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        var store = new EnvStore(vault);

        store.TrySet("billing", "TOKEN", "first", out _);
        Assert.Equal(EnvSetOutcome.Updated, store.TrySet("billing", "TOKEN", "second", out _));

        Assert.Equal(["TOKEN"], store.Read("billing").Select(v => v.Key));
        Assert.Equal("second", store.Read("billing")[0].Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// Setting a value must carry the entry's other fields across rather than rebuilding the
    /// entry from the two things env knows about. A user who annotated a variable in KeePassXC
    /// should not lose the note by rotating the secret.
    /// </summary>
    /// <remarks>
    /// This covers the fields <see cref="VaultEntry"/> models, which is a property of
    /// <see cref="EnvStore.TrySet"/>, not of the update primitive underneath it — a
    /// remove-and-re-add implementation of <see cref="Vault.UpdateEntry"/> passes this test.
    /// What survives only because the entry is edited in place is asserted by
    /// <see cref="Set_OnAnExistingKey_KeepsThePreviousValueAsHistory"/>, which does fail against
    /// remove-and-re-add; that was confirmed by trying it.
    /// </remarks>
    [Fact]
    public void Set_OnAnExistingKey_KeepsTheOtherFields()
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

        new EnvStore(vault).TrySet("billing", "TOKEN", "second", out _);

        var entry = vault.Find("env/billing/TOKEN");
        Assert.Equal("second", entry?.Password, StringComparer.Ordinal);
        Assert.Equal("set-in-keepassxc", entry?.Username, StringComparer.Ordinal);
        Assert.Equal("https://example.invalid/rotate", entry?.Url, StringComparer.Ordinal);
        Assert.Equal("rotate this quarterly", entry?.Notes, StringComparer.Ordinal);
    }

    /// <summary>
    /// Pins the retention decision in D-0014. keypaste has no feature that reads history, so
    /// nothing else in the codebase would notice if it stopped being written — while SECURITY.md
    /// and the CLI's own output would go on claiming the old value is still there.
    /// </summary>
    [Fact]
    public void Set_OnAnExistingKey_KeepsThePreviousValueAsHistory()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        var store = new EnvStore(vault);

        store.TrySet("billing", "TOKEN", "first", out _);
        Assert.Equal(0, vault.CountHistoryItems("env/billing/TOKEN"));

        store.TrySet("billing", "TOKEN", "second", out _);
        Assert.Equal(1, vault.CountHistoryItems("env/billing/TOKEN"));

        store.TrySet("billing", "TOKEN", "third", out _);
        Assert.Equal(2, vault.CountHistoryItems("env/billing/TOKEN"));
    }

    /// <summary>
    /// Writing a value identical to the one already stored still costs a history item, because
    /// <see cref="EnvStore.TrySet"/> compares nothing — it is told to set, so it sets.
    /// </summary>
    /// <remarks>
    /// This is why <c>keypaste env pull</c> classifies before it writes and skips the unchanged
    /// ones: a bulk import re-run after editing a single line would otherwise burn through the
    /// ten history items the format keeps, evicting the values a user might actually need, and
    /// touch the modification time of every entry they maintain in KeePassXC. Nothing else in the
    /// codebase would notice that happening, which is what makes it worth pinning here.
    /// </remarks>
    [Fact]
    public void Set_WithTheValueItAlreadyHas_StillCostsAHistoryItem()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        var store = new EnvStore(vault);

        store.TrySet("billing", "TOKEN", "same", out _);
        Assert.Equal(0, vault.CountHistoryItems("env/billing/TOKEN"));

        Assert.Equal(EnvSetOutcome.Updated, store.TrySet("billing", "TOKEN", "same", out _));
        Assert.Equal(1, vault.CountHistoryItems("env/billing/TOKEN"));
    }

    /// <summary>Removing takes the history with it — the only way to erase a rotated value.</summary>
    [Fact]
    public void Remove_TakesTheHistoryWithIt()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        var store = new EnvStore(vault);

        store.TrySet("billing", "TOKEN", "first", out _);
        store.TrySet("billing", "TOKEN", "second", out _);

        Assert.True(store.Remove("billing", "TOKEN"));
        Assert.Equal(-1, vault.CountHistoryItems("env/billing/TOKEN"));
        Assert.Empty(store.Read("billing"));
    }

    [Fact]
    public void Remove_ReturnsFalse_WhenTheKeyOrProjectIsAbsent()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        var store = new EnvStore(vault);
        store.TrySet("billing", "TOKEN", "first", out _);

        Assert.False(store.Remove("billing", "NOT_THERE"));
        Assert.False(store.Remove("no-such-project", "TOKEN"));
    }

    [Fact]
    public void Set_AllowsAnEmptyValue()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        var store = new EnvStore(vault);

        Assert.Equal(EnvSetOutcome.Created, store.TrySet("billing", "OPTIONAL", string.Empty, out _));
        Assert.Equal(string.Empty, store.Read("billing")[0].Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// Each of these would otherwise write to a path no read could reach: an empty or
    /// separator-bearing project resolves somewhere the project listing never looks, and a key
    /// containing a slash produces an entry that can be created and found but never removed,
    /// because removal splits the path on its last slash.
    /// </summary>
    [Theory]
    [InlineData("", "TOKEN")]
    [InlineData("   ", "TOKEN")]
    [InlineData("a/b", "TOKEN")]
    [InlineData("a\\b", "TOKEN")]
    [InlineData(" billing", "TOKEN")]
    [InlineData("billing", "")]
    [InlineData("billing", "with/slash")]
    [InlineData("billing", "with space")]
    [InlineData("billing", "with-dash")]
    [InlineData("billing", "2LEADING_DIGIT")]
    [InlineData("billing", "WITH=EQUALS")]
    public void Set_RefusesNamesItCouldNotReadBack(string project, string key)
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);

        var outcome = new EnvStore(vault).TrySet(project, key, "value", out var error);

        Assert.Equal(EnvSetOutcome.Rejected, outcome);
        Assert.NotEmpty(error);
        Assert.Empty(vault.ReadEntries());
    }

    /// <summary>
    /// <c>PATH</c> and <c>Path</c> are two variables on Linux and one on Windows. Allowing both
    /// into a vault would make the injected environment depend on which machine ran the command.
    /// </summary>
    [Fact]
    public void Set_RefusesAKeyDifferingOnlyInCaseFromAnExistingOne()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        var store = new EnvStore(vault);
        store.TrySet("billing", "TOKEN", "first", out _);

        var outcome = store.TrySet("billing", "Token", "second", out var error);

        Assert.Equal(EnvSetOutcome.Rejected, outcome);
        Assert.Contains("TOKEN", error, StringComparison.Ordinal);
        Assert.Equal(["TOKEN"], store.Read("billing").Select(v => v.Key));
    }

    /// <summary>
    /// The permissive half of the rule. keypaste will not create these names, but KeePassXC will,
    /// and a listing that hid them would disagree with what the user sees in the other tool.
    /// </summary>
    [Fact]
    public void Read_ListsNamesKeypasteWouldRefuseToCreate()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        vault.AddEntry(new VaultEntry { Title = "not a key", Password = "v", GroupPath = "env/billing" });
        vault.AddEntry(new VaultEntry { Title = "FINE", Password = "v", GroupPath = "env/billing" });

        var variables = new EnvStore(vault).Read("billing");

        Assert.Equal(["FINE", "not a key"], variables.Select(v => v.Key));
        Assert.True(variables.Single(v => string.Equals(v.Key, "FINE", StringComparison.Ordinal)).IsUsableName);
        Assert.False(variables.Single(v => string.Equals(v.Key, "not a key", StringComparison.Ordinal)).IsUsableName);
    }

    /// <summary>
    /// KDBX permits two entries with the same title in one group. There is no correct value to
    /// return for that name, so reading fails closed instead of picking one (docs/PRODUCT.md law 3.7).
    /// </summary>
    [Fact]
    public void Read_FailsClosed_WhenTwoEntriesShareAName()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "one", GroupPath = "env/billing" });
        vault.AddEntry(new VaultEntry { Title = "TOKEN", Password = "two", GroupPath = "env/billing" });

        var store = new EnvStore(vault);

        var ex = Assert.Throws<VaultException>(() => store.Read("billing"));
        Assert.Contains("TOKEN", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_SkipsAnEntryWithNoTitle()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        vault.AddEntry(new VaultEntry { Title = string.Empty, Password = "v", GroupPath = "env/billing" });
        vault.AddEntry(new VaultEntry { Title = "FINE", Password = "v", GroupPath = "env/billing" });

        Assert.Equal(["FINE"], new EnvStore(vault).Read("billing").Select(v => v.Key));
    }

    [Fact]
    public void Read_IgnoresEntriesOutsideTheProjectGroup()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        var store = new EnvStore(vault);
        store.TrySet("billing", "TOKEN", "mine", out _);
        store.TrySet("other", "TOKEN", "theirs", out _);

        // Directly under env/, and one level deeper than a project: neither is billing's.
        vault.AddEntry(new VaultEntry { Title = "STRAY", Password = "v", GroupPath = "env" });
        vault.AddEntry(new VaultEntry { Title = "DEEP", Password = "v", GroupPath = "env/billing/nested" });

        Assert.Equal(["TOKEN"], store.Read("billing").Select(v => v.Key));
        Assert.Equal("mine", store.Read("billing")[0].Value, StringComparer.Ordinal);
    }

    [Fact]
    public void Projects_ListsImmediateChildrenOfEnvOnly_OrdinalSorted()
    {
        using var vault = Vault.Create(NewVaultPath(), MasterPassword);
        var store = new EnvStore(vault);
        store.TrySet("web", "A", "v", out _);
        store.TrySet("api", "A", "v", out _);

        vault.AddEntry(new VaultEntry { Title = "A", Password = "v", GroupPath = "env/api/nested" });
        vault.AddEntry(new VaultEntry { Title = "A", Password = "v", GroupPath = "unrelated" });

        Assert.Equal(["api", "web"], store.Projects());
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
        var store = new EnvStore(vault);
        store.TrySet("billing", "TOKEN", "v", out _);
        store.Remove("billing", "TOKEN");

        Assert.True(store.ProjectExists("billing"));
        Assert.Empty(store.Read("billing"));
    }

    private string NewVaultPath()
    {
        return System.IO.Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".kdbx");
    }
}
