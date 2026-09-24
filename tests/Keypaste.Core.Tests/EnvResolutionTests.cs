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
            Add(v, "dev", "OLD", "old-secret-value");
            Add(v, "dev", "LATER", "later-secret-value");
            Add(v, "dev", "NEVER", "never-secret-value");
            v.SetExpiryUnchecked(new EntryName("env/dev", "OLD"), _now);
            v.SetExpiryUnchecked(new EntryName("env/dev", "LATER"), _now.AddSeconds(1));
        });

        var resolved = EnvResolution.Resolve(vault, "dev", _clock);

        Assert.Equal(EnvOutcome.Unusable, resolved.Outcome);
        Assert.Empty(resolved.Variables);
        var problem = Assert.Single(resolved.Problems);
        Assert.Equal("OLD", problem.Key);
        Assert.Equal("expired 2026-09-24 12:00:00Z", problem.Reason);
        AssertNoValue(resolved, "old-secret-value", "later-secret-value", "never-secret-value");

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(["LATER", "OLD"], EnvResolution.Resolve(vault, "dev", _clock).Problems.Select(p => p.Key));
    }

    [Fact]
    public void Every_unusable_entry_is_named_with_its_reason()
    {
        using var vault = Saved(v =>
        {
            Add(v, "dev", "GOOD", "good-value");
            v.AddEntry(new VaultEntry { GroupPath = "env/dev", Title = "BAD-NAME", Password = "bad-value" });
            v.AddEntry(new VaultEntry { GroupPath = "env/dev", Title = "TWICE", Password = "twice-1" });
            v.AddEntry(new VaultEntry { GroupPath = "env/dev", Title = "TWICE", Password = "twice-2" });
            v.AddEntry(new VaultEntry { GroupPath = "env/dev", Title = "token", Password = "lower-value" });
            v.AddEntry(new VaultEntry { GroupPath = "env/dev", Title = "TOKEN", Password = "upper-value" });
            v.AddEntry(new VaultEntry { GroupPath = "env/dev", Title = string.Empty, Password = "untitled-value" });
        });

        var resolved = EnvResolution.Resolve(vault, "dev", _clock);

        Assert.Equal(EnvOutcome.Unusable, resolved.Outcome);
        Assert.Empty(resolved.Variables);
        Assert.Equal(
            [
                ("", "has no title to be its variable name"),
                ("BAD-NAME", "is not a valid environment variable name: '-' is not allowed"),
                ("TOKEN", "differs only in case from 'token', which Windows treats as one variable"),
                ("TWICE", "is the name of more than one entry"),
                ("token", "differs only in case from 'TOKEN', which Windows treats as one variable"),
            ],
            resolved.Problems.Select(p => (p.Key, p.Reason)));
        AssertNoValue(resolved, "good-value", "bad-value", "twice-1", "twice-2", "lower-value", "upper-value", "untitled-value");
    }

    [Fact]
    public void A_recycled_entry_is_not_part_of_the_set()
    {
        using var vault = Saved(v =>
        {
            Add(v, "dev", "KEPT", "kept-value");
            v.AddEntry(new VaultEntry { GroupPath = "env/dev", Title = "BAD-NAME", Password = "recycled-value" });
            v.SetExpiryUnchecked(new EntryName("env/dev", "BAD-NAME"), _now.AddDays(-1));
            Assert.Equal(DeletionOutcome.Recycled, v.RemoveEntry(new EntryName("env/dev", "BAD-NAME")));
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
            Add(v, "dev", "GONE", "x");
            v.RemoveEntry(new EntryName("env/dev", "GONE"));
            v.CreateGroup("env", "empty", out _);
        });

        Assert.Equal(EnvOutcome.NoProject, EnvResolution.Resolve(vault, "absent", _clock).Outcome);

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

        using (var vault = Saved(v => Add(v, "dev", "TOKEN", "v1"), path))
        {
            vault.SetExpiryUnchecked(new EntryName("env/dev", "TOKEN"), at);
            vault.Save();
        }

        using (var vault = Vault.Open(path, EnvStoreTests.MasterPassword))
        {
            var entry = vault.Find("env/dev/TOKEN")!;
            Assert.Equal(at, entry.Expires);
            Assert.True(vault.UpdateEntry(entry with { Password = "v2", Expires = null }));
            vault.Save();
        }

        using var reopened = Vault.Open(path, EnvStoreTests.MasterPassword);
        var updated = reopened.Find("env/dev/TOKEN")!;
        Assert.Equal("v2", updated.Password);
        Assert.Equal(at, updated.Expires);
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
        Assert.NotEqual(EnvSetOutcome.Rejected, new EnvStore(vault).TrySet(project, key, value, out _));

    private static void AssertNoValue(EnvResolved resolved, params string[] values)
    {
        var said = resolved.Refusal + string.Concat(resolved.Problems.Select(p => p.Key + p.Reason));

        foreach (var value in values)
        {
            Assert.DoesNotContain(value, said, StringComparison.Ordinal);
        }
    }
}
