using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Cli;
using Keypaste.Core;
using Xunit;

namespace Keypaste.Consistency.Tests;

/// <summary>
/// A vault organized through the desktop app is the vault the shipped CLI then addresses.
/// </summary>
/// <remarks>
/// <para>
/// V-V.5b says in as many words that "a view model asserting over a fixture list does not establish
/// the move that produced it", and reopening with <c>Vault.Open</c> would not close that either:
/// core is the shared path, so a round trip through it proves persistence and assumes agreement.
/// These tests ask the CLI, which is the consumer whose answer changes when a group is renamed.
/// </para>
/// <para>
/// <b>The mutations that must make this file fail:</b> a rename or a move that writes a copy rather
/// than moving the entry, so its history is left behind; an organize that defers its
/// <c>vault.Save()</c>, so the CLI's own handle sees the vault as it was; and a project rename that
/// leaves <c>keypaste run</c> resolving the old name, which would mean the group path and the env
/// convention had come apart.
/// </para>
/// </remarks>
public sealed class GuiOrganizeIsVisibleToTheCliTests
{
    /// <summary>
    /// V-V.5b's own sequence, in its own order.
    /// </summary>
    /// <remarks>
    /// Rename the group holding a project, move an entry into it, reopen, and read the moved entry
    /// at its new path with its history intact — then ask <c>keypaste run</c> whether it agrees
    /// about the project's name. The history assertion goes through <c>Vault.Open</c> because no
    /// CLI verb reads a revision; everything else is the CLI's answer.
    /// </remarks>
    [Fact]
    public void A_project_renamed_and_an_entry_moved_into_it_are_what_keypaste_run_injects()
    {
        using var fixture = new VaultFixture(("seed", "seed-password"), ("DEPLOY_TOKEN", "first"));

        // A project with a variable, written by the shipped CLI so the fixture is not the app's own
        // idea of where a variable goes.
        Assert.Equal(
            CliApp.ExitSuccess,
            fixture.RunAnswering(["sk-live-7"], "env", "set", "billing", "STRIPE_KEY"));

        using (var screen = Entries(fixture))
        {
            // A revision on the entry that is about to be moved, so "its history intact" is a claim
            // about something the entry actually has. Fixture setup, not the behaviour under test.
            fixture.Unlocked.UpdateEntry(new VaultEntry { Title = "DEPLOY_TOKEN", Password = "second" });
            fixture.Unlocked.Save();
            screen.Model.Reload();

            var entries = screen.Model;

            // 1. Rename the group holding the env project.
            entries.SelectedGroup = entries.Groups.Single(node => node.Path == "env/billing");
            entries.BeginRenameGroupCommand.Execute(null);
            entries.DraftGroupName = "invoicing";
            entries.ConfirmRenameGroupCommand.Execute(null);

            Assert.Null(entries.Error);

            // 2. Move an entry into the renamed group, through the one confirm that does both.
            // The sidebar followed the rename, so step back to everything to reach the entry.
            entries.SelectedGroup = entries.Groups.Single(node => node.IsEverything);
            entries.Selected = entries.Rows.Single(row => row.Title == "DEPLOY_TOKEN");
            entries.OrganizeCommand.Execute(null);
            entries.MoveTarget = entries.MoveTargets.Single(node => node.Path == "env/invoicing");
            entries.ConfirmOrganizeCommand.Execute(null);

            Assert.Null(entries.Error);
        }

        // 3. Reopen and read the moved entry at its new path, with its history.
        using (var reopened = Vault.Open(fixture.VaultPath, VaultFixture.Master))
        {
            var moved = new EntryName("env/invoicing", "DEPLOY_TOKEN");

            Assert.Null(reopened.Find(new EntryName(string.Empty, "DEPLOY_TOKEN")));
            Assert.Equal("second", reopened.Find(moved)!.Password, StringComparer.Ordinal);
            Assert.Equal(
                new[] { "first" },
                reopened.ReadHistory(moved)!.Select(revision => revision.Fields.Password));
        }

        // 4. The CLI resolves the project by its new name, with both variables in the child.
        Assert.Equal(CliApp.ExitSuccess, fixture.Run("run", "invoicing", "--", "deploy"));

        // The positive control: a child was started at all.
        Assert.NotEmpty(fixture.Cli.ProcessLauncher.Started);
        Assert.Equal("sk-live-7", fixture.Cli.ProcessLauncher.Environment["STRIPE_KEY"]);
        Assert.Equal("second", fixture.Cli.ProcessLauncher.Environment["DEPLOY_TOKEN"]);

        // And not by its old one. A rename that left both names working would mean the group path
        // and the env convention had come apart.
        Assert.NotEqual(CliApp.ExitSuccess, fixture.Run("run", "billing", "--", "deploy"));
    }

    /// <summary>
    /// The reader half: an entry the app moved is where <c>keypaste get</c> looks for it.
    /// </summary>
    [Fact]
    public void An_entry_moved_in_the_gui_is_read_by_keypaste_get_at_its_new_path()
    {
        using var fixture = new VaultFixture(("seed", "seed-password"), ("servers/database", "pg-secret"));

        using (var screen = Entries(fixture))
        {
            var entries = screen.Model;

            entries.BeginCreateGroupCommand.Execute(null);
            entries.DraftGroupName = "archive";
            entries.ConfirmCreateGroupCommand.Execute(null);
            Assert.Null(entries.Error);

            entries.Selected = entries.Rows.Single(row => row.Title == "database");
            entries.OrganizeCommand.Execute(null);
            entries.DraftTitle = "database-old";
            entries.MoveTarget = entries.MoveTargets.Single(node => node.Path == "archive");
            entries.ConfirmOrganizeCommand.Execute(null);

            Assert.Null(entries.Error);
        }

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("get", "archive/database-old", "--show"));
        Assert.Equal("pg-secret", fixture.Cli.Out.Trim(), StringComparer.Ordinal);

        Assert.NotEqual(CliApp.ExitSuccess, fixture.Run("get", "servers/database", "--show"));
    }

    /// <summary>
    /// A group the app created is one the CLI lists, empty though it is.
    /// </summary>
    /// <remarks>
    /// An empty group appears in no entry listing, so this is the one organize act whose result
    /// <c>keypaste ls</c> has to be asked about separately.
    /// </remarks>
    [Fact]
    public void A_group_created_in_the_gui_is_listed_by_keypaste_ls()
    {
        using var fixture = new VaultFixture(("seed", "seed-password"));

        using (var screen = Entries(fixture))
        {
            var entries = screen.Model;

            entries.BeginCreateGroupCommand.Execute(null);
            entries.DraftGroupName = "archive";
            entries.ConfirmCreateGroupCommand.Execute(null);

            Assert.Null(entries.Error);
        }

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("ls"));
        Assert.Contains("archive", fixture.Cli.Out, StringComparison.Ordinal);
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
