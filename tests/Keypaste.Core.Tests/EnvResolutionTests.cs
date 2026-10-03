using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// A project's env set is released whole or not at all, and a refusal names each entry and why
/// without its value (E.1a).
/// </summary>
public sealed class EnvResolutionTests : IDisposable
{
    private static readonly DateTimeOffset _now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-env-resolution-").FullName;
    private readonly ManualClock _clock = new(_now);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_usable_set_is_released_whole_and_sorted()
    {
        using var vault = Saved(v =>
        {
            Add(v, "dev", "ZED", "z-value");
            Add(v, "dev", "ALPHA", "a-value");
            Add(v, "other", "ALPHA", "not-this-one");
        });

        var resolved = EnvResolution.Resolve(vault, "dev", _clock);

        Assert.Equal(EnvOutcome.Resolved, resolved.Outcome);
        Assert.Equal([new EnvVariable("ALPHA", "a-value"), new EnvVariable("ZED", "z-value")], resolved.Variables);
        Assert.Empty(resolved.Problems);
    }

    [Fact]
    public void An_entry_that_has_expired_refuses_the_set_and_one_that_has_not_does_not()
    {
        using var vault = Saved(v =>
        {
            v.SetExpiryUnchecked(Own(v, "Old", "OLD", "old-secret-value"), _now);
            v.SetExpiryUnchecked(Own(v, "Later", "LATER", "later-secret-value"), _now.AddSeconds(1));
            Own(v, "Never", "NEVER", "never-secret-value");
        });

        var resolved = EnvResolution.Resolve(vault, "dev", _clock);

        Assert.Equal(EnvOutcome.Unusable, resolved.Outcome);
        Assert.Empty(resolved.Variables);
        var problem = Assert.Single(resolved.Problems);
        Assert.Equal("OLD", problem.Key);
        Assert.Equal("expired 2026-09-24 12:00:00Z (services/Old)", problem.Reason);
        AssertNoValue(resolved, "old-secret-value", "later-secret-value", "never-secret-value");

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(["LATER", "OLD"], EnvResolution.Resolve(vault, "dev", _clock).Problems.Select(p => p.Key));
    }

    [Fact]
    public void Every_unusable_variable_is_named_and_an_untagged_entry_under_env_refuses_nothing()
    {
        using var vault = Saved(v =>
        {
            Add(v, "dev", "GOOD", "good-value");
            Own(v, "One", "TWICE", "twice-1");
            Own(v, "Two", "TWICE", "twice-2");
            v.AddEntry(new VaultEntry { GroupPath = "env/dev", Title = "BAD-NAME", Password = "bad-value" });
            v.AddEntry(new VaultEntry { GroupPath = "env/dev", Title = "GOOD", Password = "untagged-value" });
            v.AddEntry(new VaultEntry { GroupPath = "env/dev", Title = string.Empty, Password = "untitled-value" });
        });

        var resolved = EnvResolution.Resolve(vault, "dev", _clock);

        Assert.Equal(EnvOutcome.Unusable, resolved.Outcome);
        Assert.Empty(resolved.Variables);
        Assert.Equal(
            [("TWICE", "is on more than one entry (services/One, services/Two)")],
            resolved.Problems.Select(p => (p.Key, p.Reason)));
        AssertNoValue(resolved, "good-value", "bad-value", "twice-1", "twice-2", "untagged-value", "untitled-value");
    }

    [Fact]
    public void A_recycled_entry_is_not_part_of_the_set()
    {
        using var vault = Saved(v =>
        {
            Add(v, "dev", "KEPT", "kept-value");
            var gone = Own(v, "Gone", "GONE", "recycled-value");
            v.SetExpiryUnchecked(gone, _now.AddDays(-1));
            Assert.Equal(DeletionOutcome.Recycled, v.RemoveEntry(gone));
        });

        var resolved = EnvResolution.Resolve(vault, "dev", _clock);

        Assert.Equal(EnvOutcome.Resolved, resolved.Outcome);
        Assert.Equal([new EnvVariable("KEPT", "kept-value")], resolved.Variables);
    }

    [Fact]
    public void A_missing_project_is_told_apart_from_an_empty_one()
    {
        using var vault = Saved(v =>
        {
            Add(v, "empty", "GONE", "x");
            Assert.Equal(EnvRemoveOutcome.FieldRemoved, new EnvStore(v).Remove("empty", "dev", "GONE").Outcome);
            v.CreateGroup("env", "grouped", out _);
        });

        Assert.Equal(EnvOutcome.NoProject, EnvResolution.Resolve(vault, "absent", _clock).Outcome);
        Assert.Equal(EnvOutcome.NoProject, EnvResolution.Resolve(vault, "grouped", _clock).Outcome);

        var empty = EnvResolution.Resolve(vault, "empty", _clock);
        Assert.Equal(EnvOutcome.Resolved, empty.Outcome);
        Assert.Empty(empty.Variables);
    }

    [Fact]
    public void An_unsaved_edit_and_another_programs_save_release_nothing()
    {
        var path = Path.Combine(_directory, "shared.kdbx");
        using var vault = Saved(v => Add(v, "dev", "TOKEN", "v1"), path);

        Add(vault, "dev", "TOKEN", "v2-unsaved");
        Assert.Equal(EnvOutcome.Unsaved, EnvResolution.Resolve(vault, "dev", _clock).Outcome);
        vault.Save();
        Assert.Equal("v2-unsaved", EnvResolution.Resolve(vault, "dev", _clock).Variables.Single().Value);

        using (var other = Vault.Open(path, EnvStoreTests.MasterPassword))
        {
            Add(other, "dev", "TOKEN", "v3-elsewhere");
            other.Save();
        }

        var resolved = EnvResolution.Resolve(vault, "dev", _clock);
        Assert.Equal(EnvOutcome.ChangedOnDisk, resolved.Outcome);
        Assert.Empty(resolved.Variables);
    }

    [Fact]
    public void Expiry_is_read_from_the_file_and_left_alone_by_an_update()
    {
        var path = Path.Combine(_directory, "expiry.kdbx");
        var at = new DateTimeOffset(2027, 1, 2, 3, 4, 5, TimeSpan.Zero);

        using (var vault = Saved(v => v.AddEntry(new VaultEntry { GroupPath = "services", Title = "TOKEN", Password = "v1" }), path))
        {
            vault.SetExpiryUnchecked(new EntryName("services", "TOKEN"), at);
            vault.Save();
        }

        using (var vault = Vault.Open(path, EnvStoreTests.MasterPassword))
        {
            var entry = vault.Find("services/TOKEN")!;
            Assert.Equal(at, entry.Expires);
            Assert.True(vault.UpdateEntry(entry with { Password = "v2", Expires = null }));
            vault.Save();
        }

        using var reopened = Vault.Open(path, EnvStoreTests.MasterPassword);
        var updated = reopened.Find("services/TOKEN")!;
        Assert.Equal("v2", updated.Password);
        Assert.Equal(at, updated.Expires);
    }

    [Fact]
    public void AProfile_ResolvesOnlyItsOwnEntries()
    {
        using var vault = Saved(v =>
        {
            Add(v, "acme", "DATABASE_URL", "dev-db");
            Add(v, "acme", "staging", "DATABASE_URL", "staging-db");
            Add(v, "acme", "prod", "DATABASE_URL", "prod-db");
        });

        var resolved = EnvResolution.Resolve(vault, "acme", "staging", _clock);

        Assert.Equal(EnvOutcome.Resolved, resolved.Outcome);
        Assert.Equal("staging", resolved.Profile);
        Assert.Equal([new EnvVariable("DATABASE_URL", "staging-db")], resolved.Variables);
        Assert.Equal("staging", resolved.Preview.Profile);
    }

    [Fact]
    public void TheDevSet_ExcludesAnotherProfilesEntries()
    {
        using var vault = Saved(v =>
        {
            Add(v, "acme", "DATABASE_URL", "dev-db");
            Add(v, "acme", "staging", "ONLY_STAGING", "staging-only");
        });

        var resolved = EnvResolution.Resolve(vault, "acme", _clock);

        Assert.Equal(EnvOutcome.Resolved, resolved.Outcome);
        Assert.Equal("dev", resolved.Profile);
        Assert.Equal([new EnvVariable("DATABASE_URL", "dev-db")], resolved.Variables);
    }

    [Fact]
    public void AMissingProfile_IsNoProfile()
    {
        using var vault = Saved(v => Add(v, "acme", "DATABASE_URL", "dev-db"));

        var resolved = EnvResolution.Resolve(vault, "acme", "qa", _clock);

        Assert.Equal(EnvOutcome.NoProfile, resolved.Outcome);
        Assert.Equal("qa", resolved.Profile);
        Assert.Equal("'acme' has no 'qa' profile", resolved.Refusal);
    }

    [Fact]
    public void AMissingProject_IsNoProject()
    {
        using var vault = Saved(v => Add(v, "acme", "DATABASE_URL", "dev-db"));

        var resolved = EnvResolution.Resolve(vault, "other", "staging", _clock);

        Assert.Equal(EnvOutcome.NoProject, resolved.Outcome);
        Assert.Equal("staging", resolved.Profile);
    }

    [Fact]
    public void UnusableNamesTheEnvironment()
    {
        using var vault = Saved(v =>
        {
            Add(v, "acme", "staging", "OLD", "old-staging-value");
            v.SetExpiryUnchecked(ProjectVariables.Home("acme", "staging"), _now.AddDays(-1));
        });

        var resolved = EnvResolution.Resolve(vault, "acme", "staging", _clock);

        Assert.Equal(EnvOutcome.Unusable, resolved.Outcome);
        Assert.StartsWith("'acme/staging' cannot be used: OLD expired", resolved.Refusal, StringComparison.Ordinal);
        AssertNoValue(resolved, "old-staging-value");
    }

    [Fact]
    public void AKeySubset_ComesOutInTheOrderAsked_AndAMissingKeyRefusesTheWhole()
    {
        using var vault = Saved(v =>
        {
            Add(v, "acme", "A", "a-value");
            Add(v, "acme", "B", "b-value");
        });

        var subset = EnvResolution.Resolve(vault, "acme", "dev", ["B", "A"], _clock);
        Assert.Equal([new EnvVariable("B", "b-value"), new EnvVariable("A", "a-value")], subset.Variables);

        var missing = EnvResolution.Resolve(vault, "acme", "dev", ["A", "C"], _clock);
        Assert.Equal(EnvOutcome.Unusable, missing.Outcome);
        Assert.Equal(new EnvProblem("C", "is not in this profile's set"), Assert.Single(missing.Problems));
        Assert.Empty(missing.Variables);
    }

    private Vault Saved(Action<Vault> build, string? path = null)
    {
        path ??= Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".kdbx");

        using (var created = Vault.Create(path, EnvStoreTests.MasterPassword))
        {
            build(created);
            created.Save();
        }

        return Vault.Open(path, EnvStoreTests.MasterPassword);
    }

    private static void Add(Vault vault, string project, string key, string value) =>
        ProjectVariables.Set(vault, project, key, value);

    private static void Add(Vault vault, string project, string profile, string key, string value) =>
        ProjectVariables.Set(vault, project, profile, key, value);

    /// <summary>An entry of its own, <c>services/&lt;title&gt;</c>, tagged into project <c>dev</c> and holding one variable.</summary>
    private static EntryName Own(Vault vault, string title, string key, string value)
    {
        var name = new EntryName("services", title);
        vault.AddEntry(new VaultEntry { GroupPath = name.GroupPath, Title = name.Title });
        Assert.True(vault.SetFields(name, [new FieldWrite(key, value)]));
        Assert.True(vault.AddTag(name, "env:dev"));
        return name;
    }

    private static void AssertNoValue(EnvResolved resolved, params string[] values)
    {
        var said = resolved.Refusal + string.Concat(resolved.Problems.Select(p => p.Key + p.Reason));

        foreach (var value in values)
        {
            Assert.DoesNotContain(value, said, StringComparison.Ordinal);
        }
    }
}
