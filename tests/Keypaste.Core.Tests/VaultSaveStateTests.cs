using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>Whether an open vault's file holds what it holds, as the titlebar says it.</summary>
public sealed class VaultSaveStateTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-save-state-").FullName;

    private string VaultPath => Path.Combine(_directory, "vault.kdbx");

    public VaultSaveStateTests()
    {
        using var created = ClaimedVault.Create(VaultPath, VaultHistoryTests.MasterPassword);
        created.AddEntry(new VaultEntry { GroupPath = "env/app", Title = "KEY", Password = "value" });
        created.Save();
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>Changes one byte of the file and puts its last-write time back, as a write within one tick of a coarse file time leaves it.</summary>
    internal static void RewriteKeepingSizeAndTime(string path)
    {
        var written = File.GetLastWriteTimeUtc(path);
        var bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 0xFF;
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, written);
    }

    private void RewriteKeepingSizeAndTime() => RewriteKeepingSizeAndTime(VaultPath);

    [Fact]
    public void AfterOpen_Saved_WithTheFilesTime()
    {
        using var vault = ClaimedVault.Open(VaultPath, VaultHistoryTests.MasterPassword);

        var state = vault.SaveState();

        Assert.Equal(VaultSaveStatus.Saved, state.Status);
        Assert.Equal(new DateTimeOffset(File.GetLastWriteTimeUtc(VaultPath), TimeSpan.Zero), state.SavedAt);
    }

    [Fact]
    public void TheQuickState_ReadsTheFileAgainOnlyOnceItsSizeOrTimeChanged()
    {
        using var vault = ClaimedVault.Open(VaultPath, VaultHistoryTests.MasterPassword);
        Assert.Equal(VaultSaveStatus.Saved, vault.QuickSaveState().Status);

        RewriteKeepingSizeAndTime();

        Assert.Equal(VaultSaveStatus.Saved, vault.QuickSaveState().Status);
        Assert.Equal(VaultSaveStatus.ChangedOnDisk, vault.SaveState().Status);
        Assert.Equal(VaultSaveStatus.ChangedOnDisk, vault.QuickSaveState().Status);
    }

    [Fact]
    public void TheQuickState_SeesAWriteThatMovedTheTime()
    {
        using var vault = ClaimedVault.Open(VaultPath, VaultHistoryTests.MasterPassword);
        Assert.Equal(VaultSaveStatus.Saved, vault.QuickSaveState().Status);

        RewriteKeepingSizeAndTime();
        File.SetLastWriteTimeUtc(VaultPath, File.GetLastWriteTimeUtc(VaultPath).AddSeconds(2));

        Assert.Equal(VaultSaveStatus.ChangedOnDisk, vault.QuickSaveState().Status);
    }

    [Fact]
    public void TheQuickState_SeesAWriteThatChangedTheSize()
    {
        using var vault = ClaimedVault.Open(VaultPath, VaultHistoryTests.MasterPassword);
        Assert.Equal(VaultSaveStatus.Saved, vault.QuickSaveState().Status);
        var written = File.GetLastWriteTimeUtc(VaultPath);

        File.AppendAllText(VaultPath, "x");
        File.SetLastWriteTimeUtc(VaultPath, written);

        Assert.Equal(VaultSaveStatus.ChangedOnDisk, vault.QuickSaveState().Status);
    }

    [Fact]
    public void TheQuickState_OfAFileOnlyTouched_IsSaved()
    {
        using var vault = ClaimedVault.Open(VaultPath, VaultHistoryTests.MasterPassword);
        Assert.Equal(VaultSaveStatus.Saved, vault.QuickSaveState().Status);

        File.SetLastWriteTimeUtc(VaultPath, File.GetLastWriteTimeUtc(VaultPath).AddSeconds(2));

        Assert.Equal(VaultSaveStatus.Saved, vault.QuickSaveState().Status);
    }

    [Fact]
    public void AnEdit_Unsaved()
    {
        using var vault = ClaimedVault.Open(VaultPath, VaultHistoryTests.MasterPassword);

        vault.AddEntry(new VaultEntry { GroupPath = "env/app", Title = "OTHER", Password = "x" });

        Assert.Equal(VaultSaveStatus.Unsaved, vault.SaveState().Status);
    }

    [Fact]
    public void ASave_Saved_AndRaisesSaved()
    {
        using var vault = ClaimedVault.Open(VaultPath, VaultHistoryTests.MasterPassword);
        var raised = 0;
        vault.Saved += (_, _) => raised++;

        vault.AddEntry(new VaultEntry { GroupPath = "env/app", Title = "OTHER", Password = "x" });
        vault.Save();

        Assert.Equal(VaultSaveStatus.Saved, vault.SaveState().Status);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void AnotherWriter_ChangedOnDisk()
    {
        using var vault = ClaimedVault.Open(VaultPath, VaultHistoryTests.MasterPassword);

        using (var other = ClaimedVault.Open(VaultPath, VaultHistoryTests.MasterPassword))
        {
            other.AddEntry(new VaultEntry { GroupPath = "env/app", Title = "ELSEWHERE", Password = "x" });
            other.Save();
        }

        var state = vault.SaveState();
        Assert.Equal(VaultSaveStatus.ChangedOnDisk, state.Status);
        Assert.False(state.Unwritten);
    }

    [Fact]
    public void AnEditAfterAnotherWriter_ChangedOnDisk_WithTheChangeUnwritten_UntilASaveSucceeds()
    {
        using var vault = ClaimedVault.Open(VaultPath, VaultHistoryTests.MasterPassword);

        using (var other = ClaimedVault.Open(VaultPath, VaultHistoryTests.MasterPassword))
        {
            other.AddEntry(new VaultEntry { GroupPath = "env/app", Title = "ELSEWHERE", Password = "x" });
            other.Save();
        }

        vault.AddEntry(new VaultEntry { GroupPath = "env/app", Title = "OTHER", Password = "x" });

        var pending = vault.SaveState();
        Assert.Equal(VaultSaveStatus.ChangedOnDisk, pending.Status);
        Assert.True(pending.Unwritten);

        Assert.Throws<VaultChangedOnDiskException>(vault.Save);
        Assert.Equal(pending, vault.SaveState());

        vault.SaveOverwriting();

        Assert.Equal(VaultSaveStatus.Saved, vault.SaveState().Status);
        Assert.False(vault.SaveState().Unwritten);
    }

    [Fact]
    public void ADeletedFile_Unreadable()
    {
        using var vault = ClaimedVault.Open(VaultPath, VaultHistoryTests.MasterPassword);

        File.Delete(VaultPath);

        Assert.Equal(VaultSaveStatus.Unreadable, vault.SaveState().Status);
    }
}
