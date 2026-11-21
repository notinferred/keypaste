using Keypaste.Core.Internal;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>Copies and renames of vault files and their backups, which reach the disk before they return.</summary>
public sealed class VaultFilesTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-vault-files-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_copy_holds_the_same_bytes_in_a_new_file_only_its_owner_reads()
    {
        var source = Path.Combine(_directory, "vault.kdbx");
        var copy = Path.Combine(_directory, "copy.kdbx");
        File.WriteAllBytes(source, [1, 2, 3, 4]);

        VaultFiles.Copy(source, copy);

        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(copy));
        Assert.Throws<IOException>(() => VaultFiles.Copy(source, copy));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(copy));
        }
    }

    [Fact]
    public void A_move_replaces_only_when_told_to_and_names_a_missing_source()
    {
        var staged = Path.Combine(_directory, "staged");
        var vault = Path.Combine(_directory, "vault.kdbx");
        File.WriteAllBytes(staged, [1]);
        File.WriteAllBytes(vault, [2]);

        Assert.ThrowsAny<IOException>(() => VaultFiles.Move(staged, vault, overwrite: false));
        VaultFiles.Move(staged, vault, overwrite: true);

        Assert.Equal([1], File.ReadAllBytes(vault));
        Assert.False(File.Exists(staged));
        Assert.Throws<FileNotFoundException>(() => VaultFiles.Move(staged, vault, overwrite: true));
    }

    [Fact]
    public void A_move_that_must_not_replace_refuses_an_existing_destination_and_leaves_both_files()
    {
        var staged = Path.Combine(_directory, "vault.kdbx.tmp");
        var vault = Path.Combine(_directory, "vault.kdbx");
        File.WriteAllBytes(staged, [1]);
        File.WriteAllBytes(vault, [2]);

        Assert.ThrowsAny<IOException>(() => VaultFiles.Move(staged, vault, overwrite: false));

        Assert.Equal([1], File.ReadAllBytes(staged));
        Assert.Equal([2], File.ReadAllBytes(vault));
        File.Delete(vault);
        VaultFiles.Move(staged, vault, overwrite: false);
        Assert.Equal([1], File.ReadAllBytes(vault));
        Assert.False(File.Exists(staged));
    }

    [Fact]
    public void A_directory_says_whether_its_entries_are_on_disk()
    {
        Assert.True(VaultFiles.SyncDirectory(_directory));
        Assert.Equal(OperatingSystem.IsWindows(), VaultFiles.SyncDirectory(Path.Combine(_directory, "missing")));
    }

    [Fact]
    public void A_flush_on_macOS_writes_past_the_drive_cache()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Skip("F_FULLFSYNC is macOS's; elsewhere fsync reaches the drive.");
            return;
        }

        using var file = new FileStream(Path.Combine(_directory, "vault.kdbx"), FileMode.CreateNew, FileAccess.Write);
        file.Write([1, 2, 3]);
        file.Flush();

        Assert.True(VaultFiles.FullFsync(file.SafeFileHandle));
    }

    [Fact]
    public void A_flush_the_file_system_refuses_F_FULLFSYNC_for_still_writes_the_file()
    {
        var path = Path.Combine(_directory, "vault.kdbx");
        var asked = false;

        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        {
            file.Write([1, 2, 3]);
            VaultFiles.Flush(file, _ =>
            {
                asked = true;
                return false;
            });
        }

        Assert.Equal(OperatingSystem.IsMacOS(), asked);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(path));
    }
}
