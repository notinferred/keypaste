using Xunit;

namespace Keypaste.Core.Tests.KeePassXC;

/// <summary>Every file keypaste keeps, restores or exports beside a vault is one KeePassXC opens, holding the value it should.</summary>
/// <remarks>When a backup is taken, how many are kept and what is refused are <c>VaultBackupTests</c>' and <c>VaultRestoreTests</c>'.</remarks>
public sealed class BackupTests
{
    private static readonly EntryName _name = new("env/compat-backup", "ROTATED");

    private static string Path_ => $"{_name.GroupPath}/{_name.Title}";

    [Fact]
    public void KeePassXC_opens_the_backup_a_save_took_with_the_value_the_save_replaced_and_a_restore_brings_it_back()
    {
        using var file = new KeePassXcVault();
        using (var vault = file.Create())
        {
            vault.AddEntry(new VaultEntry { GroupPath = _name.GroupPath, Title = _name.Title, Password = "v1-first" });
            vault.Save();
        }

        VaultBackupTests.Age(file.Path_, TimeSpan.FromHours(1));
        Save(file, "v2-second");

        var backup = VaultBackups.List(file.Path_)[0];
        AssertOpens(backup.Path, "v1-first");
        AssertOpens(file.Path_, "v2-second");

        var report = VaultBackups.Restore(VaultBackups.Inspect(file.Path_, backup, KeePassXcCli.Password));

        Assert.Equal(File.ReadAllBytes(backup.Path), File.ReadAllBytes(file.Path_));
        AssertOpens(file.Path_, "v1-first");
        AssertOpens(Assert.IsType<VaultBackup>(report.Preserved).Path, "v2-second");
    }

    [Fact]
    public void KeePassXC_opens_an_export_with_the_vaults_current_value()
    {
        using var file = new KeePassXcVault();
        using (var vault = file.Create())
        {
            vault.AddEntry(new VaultEntry { GroupPath = _name.GroupPath, Title = _name.Title, Password = "v-current" });
            vault.Save();
            vault.ExportTo(Path.Combine(file.Directory_, "copy.kdbx"));
        }

        AssertOpens(Path.Combine(file.Directory_, "copy.kdbx"), "v-current");
    }

    private static void Save(KeePassXcVault file, string password)
    {
        using var vault = file.Open();
        vault.UpdateEntry(_name, new VaultEntry { GroupPath = _name.GroupPath, Title = _name.Title, Password = password });
        vault.Save();
    }

    private static void AssertOpens(string path, string password)
    {
        var info = KeePassXcCli.Run("db-info", path);
        Assert.Matches(@"(?im)^\s*KDF:\s*Argon2", info);
        Assert.Equal([password], KeePassXcCli.Show(path, Path_, "Password"));
    }
}
