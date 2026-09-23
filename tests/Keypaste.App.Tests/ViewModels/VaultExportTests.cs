using System.Security.Cryptography;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// The Settings screen's backups section: where the copies are, and an encrypted copy of the open
/// vault written wherever the person says.
/// </summary>
/// <remarks>
/// The app moves no bytes (<c>TheAppSharesTheWriterTests</c>), so what is asserted here is that the
/// button reaches <see cref="Vault.ExportTo"/> and reports what it said. Each "nothing was written"
/// asserts first that the picker was reached, for <see cref="FakeVaultFilePicker"/>'s reason.
/// </remarks>
public sealed class VaultExportTests
{
    [Fact]
    public async Task An_export_is_the_open_vaults_bytes_and_opens_with_its_password()
    {
        using var fixture = new TempVault();
        using var session = Unlocked(fixture);
        var picker = new FakeVaultFilePicker { ExportPath = Path.Combine(fixture.Home, "elsewhere.kdbx") };
        using var model = Screen(session, fixture, picker);

        await model.ExportAsync();

        Assert.Equal(1, picker.ExportCalls);
        Assert.Equal(File.ReadAllBytes(fixture.Path_), File.ReadAllBytes(picker.ExportPath));

        using var copy = Vault.Open(picker.ExportPath, TempVault.Password);
        Assert.Equal("entry-secret", copy.Find("example")?.Password);
        Assert.Contains("elsewhere.kdbx", model.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_suggested_name_is_dated_and_is_not_one_a_backup_would_have()
    {
        using var fixture = new TempVault();
        using var session = Unlocked(fixture, new ManualClock(new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero)));
        var picker = new FakeVaultFilePicker();

        using var screen = Screen(session, fixture, picker);
        await screen.ExportAsync();

        Assert.Matches("^test-copy-202609(19|20|21)\\.kdbx$", picker.SuggestedExportName);
    }

    [Fact]
    public async Task A_cancelled_picker_writes_nothing()
    {
        using var fixture = new TempVault();
        using var session = Unlocked(fixture);
        var picker = new FakeVaultFilePicker();
        using var model = Screen(session, fixture, picker);
        var before = Directory.GetFileSystemEntries(fixture.Home);

        await model.ExportAsync();

        Assert.Equal(1, picker.ExportCalls);
        Assert.Equal(before, Directory.GetFileSystemEntries(fixture.Home));
        Assert.False(model.HasMessage);
    }

    [Fact]
    public async Task An_existing_file_is_refused_and_left_alone()
    {
        using var fixture = new TempVault();
        using var session = Unlocked(fixture);
        var taken = Path.Combine(fixture.Home, "taken.kdbx");
        File.WriteAllText(taken, "somebody's file");
        var picker = new FakeVaultFilePicker { ExportPath = taken };
        using var model = Screen(session, fixture, picker);

        await model.ExportAsync();

        Assert.Equal(1, picker.ExportCalls);
        Assert.Equal("somebody's file", File.ReadAllText(taken));
        Assert.Contains("already exists", model.Message, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Under the very name the picker suggests, which the backup parser does not recognise: the
    /// refusal is about the folder, so the suggestion is never what keeps an export out of it.
    /// </remarks>
    [Fact]
    public async Task The_backup_directory_is_refused_whatever_the_file_is_called()
    {
        using var fixture = new TempVault();
        using var session = Unlocked(fixture);
        var picker = new FakeVaultFilePicker();
        using var model = Screen(session, fixture, picker);

        await model.ExportAsync();
        var inside = Path.Combine(VaultBackups.DirectoryFor(fixture.Path_), picker.SuggestedExportName!);

        picker.ExportPath = inside;
        await model.ExportAsync();

        Assert.Equal(2, picker.ExportCalls);
        Assert.False(File.Exists(inside));
        Assert.Contains("backup directory", model.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_vault_itself_is_refused_and_unchanged()
    {
        using var fixture = new TempVault();
        using var session = Unlocked(fixture);
        var digest = Digest(fixture.Path_);
        using var model = Screen(session, fixture, new FakeVaultFilePicker { ExportPath = fixture.Path_ });

        await model.ExportAsync();

        Assert.Equal(digest, Digest(fixture.Path_));
        Assert.Contains("the vault itself", model.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_vault_changed_on_disk_is_not_exported()
    {
        using var fixture = new TempVault();
        using var session = Unlocked(fixture);
        var destination = Path.Combine(fixture.Home, "stale.kdbx");
        using var model = Screen(session, fixture, new FakeVaultFilePicker { ExportPath = destination });

        using (var other = Vault.Open(fixture.Path_, TempVault.Password))
        {
            other.AddEntry(new VaultEntry { Title = "somebody else's", Password = "edit" });
            other.Save();
        }

        await model.ExportAsync();

        Assert.False(File.Exists(destination));
        Assert.Contains("Lock and unlock", model.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_section_says_where_the_copies_are_and_counts_them_as_they_are_now()
    {
        using var fixture = new TempVault();
        using var session = Unlocked(fixture);
        using var model = Screen(session, fixture, new FakeVaultFilePicker());

        Assert.Equal(VaultBackups.DirectoryFor(fixture.Path_), model.BackupsPath);
        Assert.StartsWith("No copies yet", model.BackupsSummary, StringComparison.Ordinal);

        session.Unlocked!.AddEntry(new VaultEntry { Title = "second", Password = "p" });
        session.Unlocked.Save();

        Assert.StartsWith("1 copy, from ", model.BackupsSummary, StringComparison.Ordinal);
        Assert.Contains("fifteen minutes", model.BackupsRule, StringComparison.Ordinal);
        Assert.Contains("password it was made under", model.BackupsRule, StringComparison.Ordinal);
        Assert.Contains("unlock screen", model.RestoreHint, StringComparison.Ordinal);
        Assert.Contains("another disk", model.ElsewhereAdvice, StringComparison.Ordinal);
    }

    [Fact]
    public void After_the_lock_the_section_names_no_vault()
    {
        using var fixture = new TempVault();
        using var session = Unlocked(fixture);
        using var model = Screen(session, fixture, new FakeVaultFilePicker());

        session.Lock(VaultLockReason.Manual);

        Assert.Equal("none", model.BackupsPath);
        Assert.Empty(model.BackupsSummary);
    }

    private static AppVaultSession Unlocked(TempVault fixture, ManualClock? clock = null)
    {
        var session = new AppVaultSession(clock ?? new ManualClock());

        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(fixture.Path_, master.Value));

        return session;
    }

    private static SettingsViewModel Screen(AppVaultSession session, TempVault fixture, FakeVaultFilePicker picker) =>
        new(session, fixture.Home, new DesktopPreferences(fixture.Home), _ => { }, picker);

    private static string Digest(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
