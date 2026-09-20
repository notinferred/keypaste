using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Cli;
using Keypaste.Core;
using Xunit;

namespace Keypaste.Consistency.Tests;

/// <summary>
/// An entry deleted in the desktop app and restored from its Trash screen is a credential the
/// shipped CLI hands back and injects again.
/// </summary>
/// <remarks>
/// <para>
/// V.3b's claim is not that <c>RestoreRecycled</c> works — <c>VaultRecycleBinTests</c> establishes
/// that — but that a person can delete something by mistake in the app and get it back, and that
/// what they get back is the credential every other keypaste surface then uses. Only a test holding
/// both front ends can ask that: the deletion and the restore are GUI actions, and
/// <c>keypaste get --show</c> and <c>keypaste run</c> are the readers.
/// </para>
/// <para>
/// <b>The mutations that must make this file fail:</b> a restore that never saves; one that
/// re-creates the entry instead of moving the recycled one back, which loses its history and its
/// UUID; a trash view that lists rows the CLI can still see, which would mean the deletion never
/// hid anything; and a restore addressed by title, which would put back whichever of two
/// same-named rows came first.
/// </para>
/// </remarks>
public sealed class RestoredEntryIsVisibleToTheCliTests
{
    private const string _password = "v1-current/9c41ba";

    [Fact]
    public void An_entry_deleted_in_the_gui_is_gone_from_the_cli_and_comes_back_from_the_trash()
    {
        using var fixture = new VaultFixture(("github", _password));
        using var screen = Entries(fixture);

        Delete(screen, "github");

        // The deletion is real from the CLI's side, which is what makes the recovery below worth
        // anything: a trash that listed a live entry would pass every assertion after this one.
        Assert.NotEqual(CliApp.ExitSuccess, fixture.Run("get", "github", "--show"));

        using var trash = Trash(fixture);
        var row = Assert.Single(trash.Model.Rows);
        Assert.Equal("github", row.DisplayTitle, StringComparer.Ordinal);

        trash.Model.Selected = row;
        trash.Model.RestoreCommand.Execute(null);

        Assert.Null(trash.Model.Error);
        Assert.Equal(CliApp.ExitSuccess, fixture.Run("get", "github", "--show"));
        Assert.Equal(_password, fixture.Cli.Out.Trim());
    }

    /// <summary>
    /// The restore moves the recycled entry back rather than making a new one, so its history and
    /// its identity survive the round trip.
    /// </summary>
    /// <remarks>
    /// The UUID is read through <c>Vault.EntryUuid</c> because no CLI verb prints one, with a
    /// second entry to compare against so a regressed constant cannot satisfy it (D-0233).
    /// </remarks>
    [Fact]
    public void A_restored_entry_keeps_its_history_and_its_identity()
    {
        using var fixture = new VaultFixture(("seed", "seed-password"));

        foreach (var value in new[] { "v1-first", "v2-current" })
        {
            Assert.Equal(
                CliApp.ExitSuccess,
                fixture.RunAnswering([value], "env", "set", "billing", "ROTATED"));
        }

        using var screen = Entries(fixture);

        var before = UuidOf(fixture, "ROTATED");
        Assert.Single(HistoryOf(fixture, "ROTATED"));

        Delete(screen, "ROTATED");

        using var trash = Trash(fixture);
        trash.Model.Selected = Assert.Single(trash.Model.Rows);
        trash.Model.RestoreCommand.Execute(null);

        Assert.Equal(before, UuidOf(fixture, "ROTATED"));

        // The anti-vacuity half: a UUID that had regressed to a constant would pass the line above
        // for every entry in every vault.
        Assert.NotEqual(before, UuidOf(fixture, "seed"));

        Assert.Equal(
            "v1-first",
            Assert.Single(HistoryOf(fixture, "ROTATED")).Fields.Password,
            StringComparer.Ordinal);

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("get", "env/billing/ROTATED", "--show"));
        Assert.Equal("v2-current", fixture.Cli.Out.Trim());
    }

    /// <summary>
    /// A variable removed on the Env Sets card and restored from the trash is injected again by
    /// the shipped runner, at the project it came from.
    /// </summary>
    [Fact]
    public void A_variable_restored_from_the_trash_is_injected_by_the_cli_runner()
    {
        using var fixture = new VaultFixture(("seed", "seed-password"));

        Assert.Equal(
            CliApp.ExitSuccess,
            fixture.RunAnswering(["sk-live"], "env", "set", "billing", "STRIPE_KEY"));

        Assert.Equal(UnlockOutcome.Opened, fixture.Unlock());

        using (var env = Env(fixture))
        {
            env.Model.OpenCommand.Execute("billing");

            var project = env.Model.OpenProject!;
            project.Variables.Single(row => row.Key == "STRIPE_KEY").RemoveCommand.Execute(null);
            project.ConfirmRemoveCommand.Execute(null);

            Assert.Contains("trash", env.Model.Notice!, StringComparison.Ordinal);
        }

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("run", "billing", "--", "deploy"));
        Assert.DoesNotContain("STRIPE_KEY", fixture.Cli.ProcessLauncher.Environment.Keys, StringComparer.Ordinal);

        using var trash = Trash(fixture);
        trash.Model.Selected = Assert.Single(trash.Model.Rows);
        trash.Model.RestoreCommand.Execute(null);

        Assert.Null(trash.Model.Error);
        Assert.Equal(CliApp.ExitSuccess, fixture.Run("run", "billing", "--", "deploy"));
        Assert.Equal("sk-live", fixture.Cli.ProcessLauncher.Environment["STRIPE_KEY"], StringComparer.Ordinal);
    }

    /// <summary>
    /// The undo beside the deletion reaches the same recovery, by the identity core handed back
    /// with the deletion rather than by the name on the row.
    /// </summary>
    [Fact]
    public void The_undo_on_the_entry_screen_restores_what_the_cli_then_reads()
    {
        using var fixture = new VaultFixture(("github", _password));
        using var screen = Entries(fixture);

        Delete(screen, "github");
        Assert.True(screen.Model.CanUndoDelete);

        screen.Model.UndoDeleteCommand.Execute(null);

        Assert.Null(screen.Model.Error);
        Assert.Equal(CliApp.ExitSuccess, fixture.Run("get", "github", "--show"));
        Assert.Equal(_password, fixture.Cli.Out.Trim());
    }

    private static void Delete(Screen screen, string title)
    {
        screen.Model.Selected = screen.Model.Rows.Single(row => row.Title == title);
        screen.Model.DeleteCommand.Execute(null);
        screen.Model.ConfirmDeleteCommand.Execute(null);

        Assert.Null(screen.Model.Error);
    }

    /// <summary>One entry's history, read from the file rather than from the open session.</summary>
    private static IReadOnlyList<EntryRevision> HistoryOf(VaultFixture fixture, string title)
    {
        using var vault = Vault.Open(fixture.VaultPath, VaultFixture.Master);
        var found = vault.ReadEntries().Single(entry => entry.Title == title);

        return vault.ReadHistory(EntryName.Of(found))!;
    }

    private static string? UuidOf(VaultFixture fixture, string title)
    {
        using var vault = Vault.Open(fixture.VaultPath, VaultFixture.Master);
        var found = vault.ReadEntries().Single(entry => entry.Title == title);

        return vault.EntryUuid(EntryName.Of(found));
    }

    private static Screen Entries(VaultFixture fixture)
    {
        Assert.Equal(UnlockOutcome.Opened, fixture.Unlock());

        return Screen.For(fixture);
    }

    private static EnvScreen Env(VaultFixture fixture) => EnvScreen.For(fixture);

    private static TrashScreen Trash(VaultFixture fixture) => new(fixture);

    /// <summary>The Entries screen, over a clipboard nothing here reads.</summary>
    private sealed class Screen : IDisposable
    {
        private readonly ClipboardCountdown _countdown;

        private Screen(ClipboardCountdown countdown, EntriesViewModel model)
        {
            _countdown = countdown;
            Model = model;
        }

        internal EntriesViewModel Model { get; }

        internal static Screen For(VaultFixture fixture)
        {
            var countdown = new ClipboardCountdown(new FakeClipboard(), TimeProvider.System);

            try
            {
                return new Screen(countdown, new EntriesViewModel(fixture.Session, countdown));
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

    /// <summary>The Env Sets screen, over a clipboard nothing here reads.</summary>
    private sealed class EnvScreen : IDisposable
    {
        private readonly ClipboardCountdown _countdown;

        private EnvScreen(ClipboardCountdown countdown, EnvSetsViewModel model)
        {
            _countdown = countdown;
            Model = model;
        }

        internal EnvSetsViewModel Model { get; }

        internal static EnvScreen For(VaultFixture fixture)
        {
            var countdown = new ClipboardCountdown(new FakeClipboard(), TimeProvider.System);

            try
            {
                return new EnvScreen(countdown, new EnvSetsViewModel(fixture.Session, countdown));
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

    /// <summary>The Trash screen, which needs no clipboard because it shows no value.</summary>
    private sealed class TrashScreen(VaultFixture fixture) : IDisposable
    {
        internal TrashViewModel Model { get; } = new(fixture.Session);

        public void Dispose() => Model.Dispose();
    }
}
