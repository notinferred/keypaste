using Keypaste.Core.Editing;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Restoring a checkpoint takes back an edit whose save failed (D-0464): items and groups it made go,
/// items it changed, moved or deleted come back as they were, and a vault that matched its file does again.
/// </summary>
public sealed class VaultCheckpointTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private static readonly EntryName _github = new("work", "github");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-checkpoint-tests-").FullName;
    private readonly Vault _vault;
    private readonly EditScene _scene;

    public VaultCheckpointTests()
    {
        _vault = Vault.Create(Path.Combine(_directory, "vault.kdbx"), _master);
        _vault.AddEntry(new VaultEntry { GroupPath = _github.GroupPath, Title = _github.Title, Password = "old", Notes = "kept" });
        _vault.Save();
        _scene = new EditScene(Path.Combine(_directory, "clients.toml"), DateTimeOffset.UnixEpoch);
    }

    public void Dispose()
    {
        _vault.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private void ApplyThenRestore(TerminalEdit edit)
    {
        var checkpoint = _vault.Checkpoint();
        Assert.NotNull(TerminalEdits.Apply(_vault, edit, _scene, shown: null).Done);
        _vault.Restore(checkpoint);
    }

    [Fact]
    public void An_item_and_the_groups_it_made_go()
    {
        ApplyThenRestore(new AddItem(new EntryName("new/deep", "item"), string.Empty, string.Empty, string.Empty, NewSecret.Of("v")));

        Assert.Null(_vault.Find(new EntryName("new/deep", "item")));
        Assert.Equal(["work"], _vault.ReadGroupPaths());
        Assert.Equal(VaultSaveStatus.Saved, _vault.SaveState().Status);
    }

    [Fact]
    public void A_changed_password_and_its_history_come_back()
    {
        var history = _vault.ReadHistory(_github)!.Count;

        ApplyThenRestore(new SetPassword(_github, NewSecret.Of("new")));

        Assert.Equal("old", _vault.Find(_github)!.Password);
        Assert.Equal(history, _vault.ReadHistory(_github)!.Count);
    }

    [Fact]
    public void A_recycled_item_comes_back_and_the_bin_it_made_goes()
    {
        ApplyThenRestore(new RemoveItem("work/github"));

        Assert.Equal("kept", _vault.Find(_github)!.Notes);
        Assert.Empty(_vault.ReadRecycled());
    }

    [Fact]
    public void A_deleted_item_comes_back_without_its_tombstone()
    {
        _vault.SetRecyclesDeletedEntries(false);
        _vault.Save();

        ApplyThenRestore(new RemoveItem("work/github"));

        Assert.Equal("old", _vault.Find(_github)!.Password);
        _vault.Save();
        using var reopened = Vault.Open(_vault.Path, _master);
        Assert.NotNull(reopened.Find(_github));
    }

    [Fact]
    public void A_vault_holding_an_unsaved_change_still_holds_it_as_unsaved()
    {
        _vault.SetFields(_github, [new FieldWrite("KEY", "pending")]);

        ApplyThenRestore(new SetPassword(_github, NewSecret.Of("new")));

        Assert.Equal("pending", _vault.ReadField(_github, "KEY"));
        Assert.Equal(VaultSaveStatus.Unsaved, _vault.SaveState().Status);
    }

    [Fact]
    public void Another_vaults_checkpoint_is_refused()
    {
        using var other = Vault.Create(Path.Combine(_directory, "other.kdbx"), _master);

        Assert.Throws<ArgumentException>(() => _vault.Restore(other.Checkpoint()));
    }
}
