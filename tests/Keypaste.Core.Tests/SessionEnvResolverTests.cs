using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// An env set resolved through an owner's session is released only while the lifetime that asked
/// is live, from the vault as its file holds it, and only under the names a person confirmed (E.1a).
/// </summary>
public sealed class SessionEnvResolverTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-session-env-").FullName;
    private readonly string _path;
    private readonly Vault _vault;
    private SessionLifetime? _lifetime = new();

    public SessionEnvResolverTests()
    {
        _path = Path.Combine(_directory, "vault.kdbx");

        using (var created = Vault.Create(_path, EnvStoreTests.MasterPassword))
        {
            new EnvStore(created).TrySet("dev", "TOKEN", "v1", out _);
            created.Save();
        }

        _vault = Vault.Open(_path, EnvStoreTests.MasterPassword);
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _lifetime?.Dispose();
        _vault.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task A_live_session_releases_the_set()
    {
        var resolved = await Resolver().ResolveAsync("dev", null, Cancel);

        Assert.Equal(EnvOutcome.Resolved, resolved.Outcome);
        Assert.Equal("v1", resolved.Variables.Single().Value);
    }

    [Fact]
    public async Task A_lock_while_the_person_is_asked_releases_nothing()
    {
        var lifetime = _lifetime!;
        var asked = new TaskCompletionSource<EnvPreview>(TaskCreationOptions.RunContinuationsAsynchronously);

        var pending = Resolver().ResolveAsync(
            "dev",
            async (preview, token) =>
            {
                asked.SetResult(preview);
                await Task.Delay(Timeout.Infinite, token);
                return true;
            },
            Cancel).AsTask();

        Assert.Equal(["TOKEN"], (await asked.Task.WaitAsync(Cancel)).Keys);
        lifetime.End();

        var resolved = await pending.WaitAsync(Cancel);
        Assert.Equal(EnvOutcome.Locked, resolved.Outcome);
        Assert.Empty(resolved.Variables);
    }

    [Fact]
    public async Task A_lock_after_confirming_commits_nothing()
    {
        var lifetime = _lifetime!;

        var resolved = await Resolver().ResolveAsync(
            "dev",
            (_, _) =>
            {
                lifetime.End();
                return ValueTask.FromResult(true);
            },
            Cancel);

        Assert.Equal(EnvOutcome.Locked, resolved.Outcome);
        Assert.Empty(resolved.Variables);
    }

    [Fact]
    public async Task A_lock_after_the_set_was_read_again_commits_nothing()
    {
        var lifetime = _lifetime!;
        var reads = 0;

        // The lock lands between the second read and the commit, which only TryCommit can catch.
        var resolver = new SessionEnvResolver(
            () => _lifetime,
            _ =>
            {
                if (++reads == 2)
                {
                    lifetime.End();
                }

                return _vault;
            },
            TimeProvider.System);

        var resolved = await resolver.ResolveAsync("dev", (_, _) => ValueTask.FromResult(true), Cancel);

        Assert.Equal(2, reads);
        Assert.Equal(EnvOutcome.Locked, resolved.Outcome);
        Assert.Empty(resolved.Variables);
    }

    [Fact]
    public async Task A_file_another_program_saved_while_the_person_was_asked_is_refused()
    {
        var resolved = await Resolver().ResolveAsync(
            "dev",
            (_, _) =>
            {
                using var other = Vault.Open(_path, EnvStoreTests.MasterPassword);
                new EnvStore(other).TrySet("dev", "TOKEN", "elsewhere", out _);
                other.Save();
                return ValueTask.FromResult(true);
            },
            Cancel);

        Assert.Equal(EnvOutcome.ChangedOnDisk, resolved.Outcome);
        Assert.Empty(resolved.Variables);
    }

    [Fact]
    public async Task An_edit_saved_while_asked_is_what_leaves_and_new_names_are_refused()
    {
        var edited = await Resolver().ResolveAsync(
            "dev",
            (_, _) =>
            {
                new EnvStore(_vault).TrySet("dev", "TOKEN", "v2", out _);
                _vault.Save();
                return ValueTask.FromResult(true);
            },
            Cancel);

        Assert.Equal("v2", edited.Variables.Single().Value);

        var renamed = await Resolver().ResolveAsync(
            "dev",
            (_, _) =>
            {
                new EnvStore(_vault).TrySet("dev", "EXTRA", "x", out _);
                _vault.Save();
                return ValueTask.FromResult(true);
            },
            Cancel);

        Assert.Equal(EnvOutcome.ChangedWhileAsked, renamed.Outcome);
        Assert.Empty(renamed.Variables);
    }

    [Fact]
    public async Task A_refused_set_is_refused_before_anybody_is_asked_and_a_no_releases_nothing()
    {
        var asked = false;
        _vault.AddEntry(new VaultEntry { GroupPath = "env/dev", Title = "BAD-NAME", Password = "x" });
        _vault.Save();

        var refused = await Resolver().ResolveAsync(
            "dev",
            (_, _) =>
            {
                asked = true;
                return ValueTask.FromResult(true);
            },
            Cancel);

        Assert.Equal(EnvOutcome.Unusable, refused.Outcome);
        Assert.False(asked);

        _vault.RemoveEntry(new EntryName("env/dev", "BAD-NAME"));
        _vault.Save();

        var declined = await Resolver().ResolveAsync("dev", (_, _) => ValueTask.FromResult(false), Cancel);
        Assert.Equal(EnvOutcome.Declined, declined.Outcome);
        Assert.Empty(declined.Variables);
    }

    [Fact]
    public async Task No_live_lifetime_is_a_lock()
    {
        _lifetime!.End();

        Assert.Equal(EnvOutcome.Locked, (await Resolver().ResolveAsync("dev", null, Cancel)).Outcome);

        _lifetime = null;
        Assert.Equal(EnvOutcome.Locked, (await Resolver().ResolveAsync("dev", null, Cancel)).Outcome);
    }

    [Fact]
    public async Task AKeysSubset_PreviewsAndReleasesOnlyThose()
    {
        AddToStaging(("A", "a1"), ("B", "b1"), ("C", "c1"));
        EnvPreview? asked = null;

        var resolved = await Resolver().ResolveAsync(
            "dev",
            "staging",
            ["C", "A"],
            (preview, _) =>
            {
                asked = preview;
                return ValueTask.FromResult(true);
            },
            Cancel);

        Assert.Equal(["C", "A"], asked?.Keys);
        Assert.Equal("staging", asked?.Profile);
        Assert.Equal([new EnvVariable("C", "c1"), new EnvVariable("A", "a1")], resolved.Variables);
        Assert.Equal("staging", resolved.Profile);
    }

    [Fact]
    public async Task ARequestedKeyMissing_RefusesTheWhole()
    {
        AddToStaging(("A", "a1"));
        var asked = false;

        var resolved = await Resolver().ResolveAsync(
            "dev",
            "staging",
            ["A", "MISSING"],
            (_, _) =>
            {
                asked = true;
                return ValueTask.FromResult(true);
            },
            Cancel);

        Assert.Equal(EnvOutcome.Unusable, resolved.Outcome);
        Assert.Equal("MISSING", Assert.Single(resolved.Problems).Key);
        Assert.Empty(resolved.Variables);
        Assert.False(asked);
    }

    [Fact]
    public async Task AKeysSubset_IgnoresAnUnusableUnrequestedKey()
    {
        AddToStaging(("A", "a1"), ("OLD", "old"));
        _vault.AddEntry(new VaultEntry { GroupPath = "env/dev/staging", Title = "BAD-NAME", Password = "x" });
        _vault.SetExpiryUnchecked(new EntryName("env/dev/staging", "OLD"), DateTimeOffset.UtcNow.AddDays(-1));
        _vault.Save();

        var subset = await Resolver().ResolveAsync("dev", "staging", ["A"], null, Cancel);
        var whole = await Resolver().ResolveAsync("dev", "staging", null, null, Cancel);

        Assert.Equal([new EnvVariable("A", "a1")], subset.Variables);
        Assert.Equal(EnvOutcome.Unusable, whole.Outcome);
        Assert.Equal(["BAD-NAME", "OLD"], whole.Problems.Select(problem => problem.Key));
    }

    [Fact]
    public async Task EveryRefusal_CarriesTheRequestedProfile()
    {
        AddToStaging(("A", "a1"));

        var declined = await Resolver().ResolveAsync("dev", "staging", null, (_, _) => ValueTask.FromResult(false), Cancel);

        var changed = await Resolver().ResolveAsync(
            "dev",
            "staging",
            null,
            (_, _) =>
            {
                new EnvStore(_vault).TrySet("dev", "staging", "EXTRA", "x", out _);
                _vault.Save();
                return ValueTask.FromResult(true);
            },
            Cancel);

        var lifetime = _lifetime!;
        var lockedWhileAsked = await Resolver().ResolveAsync(
            "dev",
            "staging",
            null,
            (_, _) =>
            {
                lifetime.End();
                return ValueTask.FromResult(true);
            },
            Cancel);

        var locked = await Resolver().ResolveAsync("dev", "staging", null, null, Cancel);
        var missing = await Resolver().ResolveAsync("dev", "qa", null, null, Cancel);

        Assert.Equal(
            [EnvOutcome.Declined, EnvOutcome.ChangedWhileAsked, EnvOutcome.Locked, EnvOutcome.Locked],
            new[] { declined, changed, lockedWhileAsked, locked }.Select(resolved => resolved.Outcome));
        Assert.All(new[] { declined, changed, lockedWhileAsked, locked }, resolved => Assert.Equal("staging", resolved.Profile));
        Assert.Equal("qa", missing.Profile);
    }

    private void AddToStaging(params (string Key, string Value)[] variables)
    {
        foreach (var (key, value) in variables)
        {
            Assert.NotEqual(EnvSetOutcome.Rejected, new EnvStore(_vault).TrySet("dev", "staging", key, value, out _));
        }

        _vault.Save();
    }

    private SessionEnvResolver Resolver() => new(
        () => _lifetime,
        lifetime => ReferenceEquals(lifetime, _lifetime) && lifetime.IsLive ? _vault : null,
        TimeProvider.System);
}
