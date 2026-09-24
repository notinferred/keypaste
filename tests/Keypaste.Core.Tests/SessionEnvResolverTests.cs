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

    private SessionEnvResolver Resolver() => new(
        () => _lifetime,
        lifetime => ReferenceEquals(lifetime, _lifetime) && lifetime.IsLive ? _vault : null,
        TimeProvider.System);
}
