using Keypaste.Core.Agents;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The vault <c>keypaste approve</c> holds, merging another program's save through its owner: a save that save refused is merged and
/// answered as saved, and a file the merge refused is left alone until it changes again.
/// </summary>
public sealed class HeldVaultTests : IDisposable
{
    private const string _master = "correct horse battery staple";
    private static readonly EntryName _github = new("work", "github");
    private static readonly EntryName _stripe = new("work", "stripe");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-held-vault-").FullName;
    private readonly SessionLifetime _lifetime = new("session-one");
    private readonly OwnerLog _log;
    private readonly HeldVault _held;
    private readonly SessionServer _server;

    public HeldVaultTests()
    {
        _log = new OwnerLog(_directory);

        using (var seed = ClaimedVault.Create(VaultPath, _master))
        {
            seed.AddEntry(new VaultEntry { GroupPath = _github.GroupPath, Title = _github.Title, Password = "github-0" });
            seed.AddEntry(new VaultEntry { GroupPath = _stripe.GroupPath, Title = _stripe.Title, Password = "stripe-0" });
            seed.Save();
        }

        // The held vault owns the copy it is given and disposes whichever copy it ends with.
#pragma warning disable CA2000
        _held = new HeldVault(ClaimedVault.Open(VaultPath, _master), _lifetime);
#pragma warning restore CA2000
        _server = SessionServer.Build(new SessionServerOptions
        {
            Vault = VaultIdentity.Of(_directory, VaultPath),
            Lifetime = _lifetime,
            CurrentLifetime = () => _lifetime,
            UnlockedFor = _held.UnlockedFor,
            Channel = new FakeChannel(),
            Rules = new FakeAgentRules(),
            Audit = () => _log.Log,
        });
        _held.MergeThrough(_server.Authority, "keypaste approve");
    }

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    public void Dispose()
    {
        _server.Dispose();
        _held.Dispose();
        _lifetime.Dispose();
        _log.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void ASaveAnotherProgramsSaveRefused_IsMergedWithIt_AndAnsweredAsSaved()
    {
        SaveElsewhere(_github, "theirs");
        var copy = _held.Open;

        var saved = _held.Save(vault => vault.UpdateEntry(new VaultEntry { GroupPath = _stripe.GroupPath, Title = _stripe.Title, Password = "mine" }));

        Assert.Equal(AgentSave.Saved, saved);
        Assert.NotSame(copy, _held.Open);
        Assert.Equal(VaultSaveStatus.Saved, _held.Open!.SaveState().Status);
        using var file = ClaimedVault.Open(VaultPath, _master);
        Assert.Equal(("theirs", "mine"), (file.Find(_github)!.Password, file.Find(_stripe)!.Password));
        Assert.Equal("keypaste approve", Assert.Single(_log.Lines()).GetProperty("client").GetProperty("name").GetString());
    }

    [Fact]
    public void AFileHoldingAnotherVault_IsRefusedOnce_AndTriedAgainOnlyOnceItChanges()
    {
        var copy = _held.Open;
        ReplaceWithAnotherVault();

        var refused = _held.MergeAnotherSave();

        Assert.Equal(VaultMergeSaveOutcome.AnotherVault, refused!.Outcome);
        Assert.False(_held.MergesAnotherSave);
        Assert.Null(_held.MergeAnotherSave());
        Assert.Same(copy, _held.Open);

        ReplaceWithAnotherVault();

        Assert.True(_held.MergesAnotherSave);
        Assert.Equal(AgentSaveOutcome.ChangedOnDisk, _held.Save(vault => vault.UpdateEntry(new VaultEntry { GroupPath = _stripe.GroupPath, Title = _stripe.Title, Password = "mine" })).Outcome);
    }

    [Fact]
    public void AfterTheUnlockEnds_NothingIsOpenOrMerged()
    {
        SaveElsewhere(_github, "theirs");
        _lifetime.End();

        Assert.Null(_held.Open);
        Assert.Null(_held.UnlockedFor(_lifetime));
        Assert.Null(_held.MergeAnotherSave());
        Assert.Equal(AgentSaveOutcome.Locked, _held.Save(_ => true).Outcome);
    }

    [Fact]
    public async Task ItsFileWatch_FindsASyncToolsConflictedCopyAtUnlock()
    {
        var copy = Path.Combine(_directory, "vault.sync-conflict-20261120-090000-ABCDEFG.kdbx");
        File.Copy(VaultPath, copy);
        var found = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _held.ConflictedCopies.Changed += (_, _) => found.TrySetResult();

        using var watch = new VaultFileWatch(_held);

        await found.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        Assert.Equal(copy, Assert.Single(_held.ConflictedCopies.Found));
    }

    private void SaveElsewhere(EntryName name, string password)
    {
        using var other = ClaimedVault.Open(VaultPath, _master);
        Assert.True(other.UpdateEntry(new VaultEntry { GroupPath = name.GroupPath, Title = name.Title, Password = password }));
        other.Save();
    }

    private void ReplaceWithAnotherVault()
    {
        var elsewhere = Path.Combine(_directory, "elsewhere.kdbx");

        using (var other = ClaimedVault.Create(elsewhere, _master))
        {
            other.AddEntry(new VaultEntry { GroupPath = "other", Title = Guid.NewGuid().ToString("N"), Password = "elsewhere" });
            other.Save();
        }

        File.Move(elsewhere, VaultPath, overwrite: true);
    }
}
