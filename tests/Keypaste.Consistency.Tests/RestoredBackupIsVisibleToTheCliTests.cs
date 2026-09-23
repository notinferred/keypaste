using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Cli;
using Keypaste.Core;
using Xunit;

namespace Keypaste.Consistency.Tests;

/// <summary>
/// A whole-vault backup restored from the desktop's locked screen is what the CLI then reads.
/// </summary>
/// <remarks>
/// The restore is a byte copy made by the core, so the claim is not that two writers agree but that
/// the app's restore put a vault in place which the other front end opens, under the password the
/// backup was made with, and that the file it replaced is an ordinary vault the CLI opens too. The
/// backup is made by an edit through the Entries screen and never planted.
/// </remarks>
public sealed class RestoredBackupIsVisibleToTheCliTests
{
    private const string _before = "before-the-edit-7c1d";
    private const string _after = "after-the-edit-93aa";

    [Fact]
    public async Task The_cli_reads_the_value_the_desktop_restore_brought_back()
    {
        using var fixture = new VaultFixture(("github", _before));

        // Seeding the fixture was itself a save over the vault `init` made, so a backup seconds old
        // is already there and the app's save would fall inside the floor and keep nothing.
        AgeBackups(fixture.VaultPath);

        EditThroughTheApp(fixture, _after);

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("get", "github", "--show"));
        Assert.Equal(_after, fixture.Cli.Out.Trim());

        var home = Directory.CreateDirectory(
            Path.Combine(Path.GetDirectoryName(fixture.VaultPath)!, "desktop-home")).FullName;

        using (var unlock = new UnlockViewModel(fixture.Session, home, new NoPicker(), () => { }))
        {
            Assert.True(unlock.Offer(fixture.VaultPath));
            unlock.StartRestoreCommand.Execute(null);

            var restore = Assert.IsType<RestoreBackupViewModel>(unlock.Restore);

            foreach (var c in VaultFixture.Master)
            {
                restore.Type(c);
            }

            await restore.CheckAsync();
            Assert.True(restore.IsConfirming, restore.Message);

            await restore.ConfirmAsync();
            Assert.True(fixture.Session.IsUnlocked);
        }

        fixture.Session.Lock(VaultLockReason.Manual);

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("get", "github", "--show"));
        Assert.NotEmpty(fixture.Cli.Out.Trim());
        Assert.Equal(_before, fixture.Cli.Out.Trim());

        // The file the restore replaced is kept as the newest backup, and is a vault like any other.
        var replaced = VaultBackups.List(fixture.VaultPath)[0];

        fixture.Cli.Stdout.GetStringBuilder().Clear();
        fixture.Cli.Prompt.Enqueue(VaultFixture.Master);

        Assert.Equal(CliApp.ExitSuccess, fixture.Cli.Run(["get", "github", "--show", "--vault", replaced.Path]));
        Assert.Equal(_after, fixture.Cli.Out.Trim());
    }

    private static void EditThroughTheApp(VaultFixture fixture, string password)
    {
        Assert.Equal(UnlockOutcome.Opened, fixture.Unlock());

        using var countdown = new ClipboardCountdown(NoClipboard.Instance, TimeProvider.System);
        using var entries = new EntriesViewModel(fixture.Session, countdown);

        entries.Selected = entries.Rows.Single(row => row.Title == "github");
        var detail = entries.Detail!;

        detail.EditCommand.Execute(null);

        foreach (var c in password)
        {
            detail.NewPassword.Type(c);
        }

        detail.SaveCommand.Execute(null);

        fixture.Session.Lock(VaultLockReason.Manual);
    }

    /// <summary>Moves every backup's stamp back an hour; the floor has no knob to turn instead (D-0263).</summary>
    private static void AgeBackups(string vaultPath)
    {
        foreach (var backup in VaultBackups.List(vaultPath).Reverse())
        {
            var stamp = (backup.TakenAt - TimeSpan.FromHours(1))
                .ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);

            File.Move(
                backup.Path,
                Path.Combine(
                    Path.GetDirectoryName(backup.Path)!,
                    $"{Path.GetFileNameWithoutExtension(vaultPath)}.{stamp}{Path.GetExtension(vaultPath)}"));
        }
    }

    /// <summary>The restore asks no picker anything.</summary>
    private sealed class NoPicker : IVaultFilePicker
    {
        public Task<string?> PickExistingAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickNewAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickExportDestinationAsync(string suggestedName) => Task.FromResult<string?>(null);

        public Task<string?> PickKeyfileAsync() => Task.FromResult<string?>(null);
    }
}
