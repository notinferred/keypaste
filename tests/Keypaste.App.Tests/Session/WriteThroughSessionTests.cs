using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// The desktop's one write path: an edit, its save, and each way that fails as an outcome a screen answers.
/// </summary>
public sealed class WriteThroughSessionTests
{
    [Fact]
    public void An_edit_that_changes_the_vault_is_saved()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home);
        Unlock(session, fixture);

        var write = session.Write(vault => Add(vault, "MINE"), added => added);

        Assert.Equal(WriteOutcome.Saved, write.Outcome);
        Assert.True(write.Value);
        Assert.False(write.Unwritten);
        Assert.Null(write.Problem("add this again"));
        Assert.Contains("MINE", TitlesOnDisk(fixture));
    }

    [Fact]
    public void An_edit_that_changes_nothing_writes_nothing()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home);
        Unlock(session, fixture);
        var before = File.ReadAllBytes(fixture.Path_);

        var write = session.Write(vault => vault.Find("absent") is not null, found => found);

        Assert.Equal(WriteOutcome.NothingToSave, write.Outcome);
        Assert.False(write.Value);
        Assert.Null(write.Problem("try again"));
        Assert.Equal(before, File.ReadAllBytes(fixture.Path_));
    }

    [Fact]
    public void A_locked_session_runs_no_edit()
    {
        using var session = new AppVaultSession(new ManualClock(AppClock.Start));
        var ran = false;

        var write = session.Write(
            _ =>
            {
                ran = true;
                return true;
            },
            _ => true);

        Assert.Equal(WriteOutcome.Locked, write.Outcome);
        Assert.False(ran);
        Assert.Equal("The vault is locked.", write.Problem("try again"));
    }

    /// <summary>The case every screen caught for itself: the file keeps the other save, and the edit stays open and unwritten.</summary>
    [Fact]
    public void Another_programs_save_is_one_outcome_and_overwrites_nothing()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home);
        Unlock(session, fixture);
        SaveElsewhere(fixture, "ELSEWHERE");

        var write = session.Write(vault => Add(vault, "MINE"), added => added);

        Assert.Equal(WriteOutcome.ChangedOnDisk, write.Outcome);
        Assert.True(write.Value);
        Assert.True(write.Unwritten);
        Assert.Equal(
            "Something else changed this vault since you opened it. Reload to see it, then delete this again.",
            write.Problem("delete this again"));

        var onDisk = TitlesOnDisk(fixture);
        Assert.Contains("ELSEWHERE", onDisk);
        Assert.DoesNotContain("MINE", onDisk);

        var state = session.Unlocked!.SaveState();
        Assert.Equal(VaultSaveStatus.ChangedOnDisk, state.Status);
        Assert.True(state.Unwritten);
        Assert.NotNull(session.Unlocked.Find("MINE"));
    }

    [Fact]
    public void After_a_reload_the_same_edit_is_saved_beside_the_other_programs()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home);
        Unlock(session, fixture);
        SaveElsewhere(fixture, "ELSEWHERE");
        Assert.Equal(WriteOutcome.ChangedOnDisk, session.Write(vault => Add(vault, "MINE"), added => added).Outcome);

        Assert.Equal(ReloadOutcome.Reloaded, session.Reload().Outcome);
        var write = session.Write(vault => Add(vault, "MINE"), added => added);

        Assert.Equal(WriteOutcome.Saved, write.Outcome);
        var onDisk = TitlesOnDisk(fixture);
        Assert.Contains("ELSEWHERE", onDisk);
        Assert.Contains("MINE", onDisk);
    }

    /// <summary>As the settings screen's export meets it: the edit itself finds the change, so nothing of it is in the vault.</summary>
    [Fact]
    public void A_change_on_disk_the_edit_finds_leaves_nothing_unwritten()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home);
        Unlock(session, fixture);
        SaveElsewhere(fixture, "ELSEWHERE");
        var destination = Path.Combine(fixture.Home, "copy.kdbx");

        var write = session.Write(
            vault =>
            {
                vault.ExportTo(destination);
                return true;
            },
            _ => false);

        Assert.Equal(WriteOutcome.ChangedOnDisk, write.Outcome);
        Assert.False(write.Unwritten);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public void An_edit_the_core_refuses_fails_with_its_reason_and_nothing_unwritten()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home);
        Unlock(session, fixture);
        var before = File.ReadAllBytes(fixture.Path_);

        var write = session.Write(
            vault =>
            {
                vault.ExportTo(vault.Path);
                return true;
            },
            _ => false);

        Assert.Equal(WriteOutcome.Failed, write.Outcome);
        Assert.False(write.Value);
        Assert.False(write.Unwritten);
        Assert.StartsWith("That is the vault itself.", write.Reason, StringComparison.Ordinal);
        Assert.Equal(write.Reason, write.Problem("try again"));
        Assert.Equal(before, File.ReadAllBytes(fixture.Path_));
    }

    [Fact]
    public void A_save_the_core_refuses_fails_with_its_reason_and_the_edit_unwritten()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home);
        Unlock(session, fixture);

        // The first save after an unlock keeps a copy first; a file where the backup directory must go fails it.
        File.WriteAllText(VaultBackups.DirectoryFor(fixture.Path_), "not a directory");
        var before = File.ReadAllBytes(fixture.Path_);

        var write = session.Write(vault => Add(vault, "MINE"), added => added);

        Assert.Equal(WriteOutcome.Failed, write.Outcome);
        Assert.True(write.Value);
        Assert.True(write.Unwritten);
        Assert.False(string.IsNullOrEmpty(write.Reason));
        Assert.Equal(write.Reason, write.Problem("try again"));
        Assert.Equal(before, File.ReadAllBytes(fixture.Path_));
        Assert.NotNull(session.Unlocked!.Find("MINE"));
    }

    [Fact]
    public void A_vault_that_locks_during_the_edit_is_reported_locked()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home);
        Unlock(session, fixture);

        var write = session.Write(
            vault =>
            {
                session.Lock(VaultLockReason.Idle);
                return Add(vault, "MINE");
            },
            added => added);

        Assert.Equal(WriteOutcome.Locked, write.Outcome);
        Assert.Equal("The vault is locked.", write.Problem("add this again"));
        Assert.DoesNotContain("MINE", TitlesOnDisk(fixture));
    }

    [Fact]
    public void Something_else_disposed_while_the_vault_stays_open_is_not_taken_for_a_lock()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home);
        Unlock(session, fixture);

        Assert.Throws<ObjectDisposedException>(
            () => session.Write<bool>(_ => throw new ObjectDisposedException("draft"), _ => true));
        Assert.True(session.IsUnlocked);
    }

    private static bool Add(Vault vault, string title)
    {
        vault.AddEntry(new VaultEntry { Title = title, Password = "x" });
        return true;
    }

    private static void Unlock(AppVaultSession session, TempVault fixture)
    {
        using var master = TempVault.Secret(TempVault.Password);
        Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(fixture.Path_, master.Value));
    }

    private static void SaveElsewhere(TempVault fixture, string title)
    {
        using var other = Vault.Open(fixture.Path_, TempVault.Password);
        other.AddEntry(new VaultEntry { Title = title, Password = "y" });
        other.Save();
    }

    private static List<string> TitlesOnDisk(TempVault fixture)
    {
        using var reopened = Vault.Open(fixture.Path_, TempVault.Password);
        return [.. reopened.ReadEntries().Select(entry => entry.Title)];
    }
}
