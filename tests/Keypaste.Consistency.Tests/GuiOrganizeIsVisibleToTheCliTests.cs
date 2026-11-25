using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Cli;
using Keypaste.Core;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.Consistency.Tests;

/// <summary>
/// A vault organized through the desktop app is the vault the shipped CLI then addresses.
/// </summary>
/// <remarks>
/// <para>
/// A view model asserting over a fixture list does not establish the move that produced it, and
/// reopening with <c>Vault.Open</c> would not close that either:
/// core is the shared path, so a round trip through it proves persistence and assumes agreement.
/// These tests ask the CLI, which is the consumer whose answer changes when an entry is renamed.
/// </para>
/// <para>
/// <b>The mutations that must make this file fail:</b> a rename that writes a copy rather than renaming
/// the entry, so its history is left behind; a rename that defers its <c>vault.Save()</c>, so the CLI's own
/// handle sees the vault as it was; and a rename that moves the entry out of the group the file holds it in.
/// </para>
/// </remarks>
public sealed class GuiOrganizeIsVisibleToTheCliTests
{
    /// <summary>
    /// An entry the app renamed is where <c>keypaste get</c> looks for it, in the group the file held it in,
    /// with its history.
    /// </summary>
    [Fact]
    public void An_entry_renamed_in_the_gui_is_read_by_keypaste_get_in_its_own_group()
    {
        using var fixture = new VaultFixture(("seed", "seed-password"), ("servers/database", "pg-secret"));

        using (var screen = Entries(fixture))
        {
            fixture.Unlocked.UpdateEntry(new VaultEntry { GroupPath = "servers", Title = "database", Password = "pg-second" });
            fixture.Unlocked.Save();
            screen.Model.Reload();
            var entries = screen.Model;

            entries.Selected = entries.Rows.Single(row => row.Title == "database");
            entries.OrganizeCommand.Execute(null);
            entries.DraftTitle = "database-old";
            entries.ConfirmOrganizeCommand.Execute(null);

            Assert.Null(entries.Error);
        }

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("get", "servers/database-old", "--reveal"));
        Assert.Equal("pg-second", fixture.Cli.Out.Trim(), StringComparer.Ordinal);
        Assert.NotEqual(CliApp.ExitSuccess, fixture.Run("get", "servers/database", "--reveal"));

        using var reopened = ClaimedVault.Open(fixture.VaultPath, VaultFixture.Master);
        Assert.Equal(
            new[] { "pg-secret" },
            reopened.ReadHistory(new EntryName("servers", "database-old"))!.Select(revision => revision.Fields.Password));
    }

    /// <summary>
    /// A group moved through the app's session is where <c>keypaste get</c> reads what it holds; deleted, the CLI no
    /// longer reaches it; and an item restored from it is read again at the path it had.
    /// </summary>
    /// <remarks>
    /// Each change goes through the session's write, which every screen of the app saves through, so the
    /// CLI's own handle is the first to read what the app wrote.
    /// </remarks>
    [Fact]
    public void A_group_moved_deleted_and_restored_through_the_session_is_followed_by_keypaste_get()
    {
        using var fixture = new VaultFixture(("seed", "seed-password"), ("servers/database", "pg-secret"));
        Assert.Equal(UnlockOutcome.Opened, fixture.Unlock());

        Assert.Equal(
            WriteOutcome.Saved,
            fixture.Session.Write(vault => vault.CreateGroup(string.Empty, "archive", out _), outcome => outcome == GroupOutcome.Created).Outcome);
        Assert.Equal(
            WriteOutcome.Saved,
            fixture.Session.Write(vault => vault.MoveGroup("servers", "archive", out _), outcome => outcome == GroupOutcome.Moved).Outcome);

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("get", "archive/servers/database", "--reveal"));
        Assert.Equal("pg-secret", fixture.Cli.Out.Trim(), StringComparer.Ordinal);
        Assert.NotEqual(CliApp.ExitSuccess, fixture.Run("get", "servers/database", "--reveal"));

        Assert.Equal(
            WriteOutcome.Saved,
            fixture.Session.Write(vault => vault.DeleteGroup("archive/servers"), outcome => outcome == GroupOutcome.Recycled).Outcome);

        Assert.NotEqual(CliApp.ExitSuccess, fixture.Run("get", "archive/servers/database", "--reveal"));

        var recycled = Assert.Single(fixture.Unlocked.ReadRecycled());
        Assert.Equal("archive/servers", recycled.OriginalGroupPath, StringComparer.Ordinal);
        Assert.Equal(
            WriteOutcome.Saved,
            fixture.Session.Write(vault => vault.RestoreRecycled(recycled.Id), outcome => outcome == RestoreOutcome.Restored).Outcome);

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("get", "archive/servers/database", "--reveal"));
        Assert.Equal("pg-secret", fixture.Cli.Out.Trim(), StringComparer.Ordinal);
    }

    private static Screen<EntriesViewModel> Entries(VaultFixture fixture)
    {
        Assert.Equal(UnlockOutcome.Opened, fixture.Unlock());
        return Screen<EntriesViewModel>.For(fixture, (session, clipboard) => new EntriesViewModel(session, clipboard));
    }

    /// <summary>One of the app's screens, with a clipboard that goes nowhere.</summary>
    /// <remarks>
    /// Copied rather than shared, as the other files in this project copy it: a helper hoisted into
    /// its own type is one more thing to keep in step, and each of these files is meant to be
    /// readable on its own.
    /// </remarks>
    private sealed class Screen<T> : IDisposable
        where T : class, IDisposable
    {
        private readonly ClipboardCountdown _countdown;

        private Screen(ClipboardCountdown countdown, T model)
        {
            _countdown = countdown;
            Model = model;
        }

        internal T Model { get; }

        internal static Screen<T> For(VaultFixture fixture, Func<AppVaultSession, ClipboardCountdown, T> build)
        {
            var countdown = new ClipboardCountdown(NoClipboard.Instance, TimeProvider.System);

            try
            {
                return new Screen<T>(countdown, build(fixture.Session, countdown));
            }
            catch
            {
                countdown.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            Model.Dispose();
            _countdown.Dispose();
        }
    }
}
