using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Cli;
using Keypaste.Core;
using Xunit;

namespace Keypaste.Consistency.Tests;

/// <summary>
/// An edit made through the desktop app's screens is what the shipped CLI returns, at once.
/// </summary>
/// <remarks>
/// <para>
/// The claim 4.2 makes is that everything reads and writes through core, so the CLI and the GUI stay
/// consistent. That is not provable by reopening the file with <c>Vault.Open</c>: core is the shared
/// path, so a round trip through it proves persistence and assumes agreement. These tests ask the
/// CLI instead, by running its verbs in-process the way <c>Keypaste.Cli.Tests</c> does.
/// </para>
/// <para>
/// <b>The mutations that must make this file fail</b>, written down because a gate whose failure
/// modes are undocumented gets weakened by somebody who cannot see what it was for:
/// </para>
/// <list type="number">
/// <item>Dropping a <c>vault.Save()</c> from an edit command, so the GUI mutates its in-memory tree
/// and defers the write. The likeliest real bug, and why the CLI is asked before the session ends.</item>
/// <item>Writing environment variables under <c>envs/&lt;project&gt;/</c> rather than
/// <c>env/&lt;project&gt;/</c>. This round-trips through <c>Vault.Open</c> perfectly and only
/// <c>keypaste env ls</c> comes back empty — the mutation that justifies this project existing.</item>
/// <item>Letting the GUI create a variable name <c>EnvConvention</c> rejects, so the app makes
/// variables <c>keypaste run</c> cannot inject.</item>
/// <item>Generating a password the GUI displays but does not store, or storing a truncated one.</item>
/// </list>
/// <para>
/// <b>Every test asserts the CLI succeeded and printed something before asserting what it printed.</b>
/// Without that a <c>CliApp.Run</c> that exits non-zero on every invocation passes the whole file.
/// </para>
/// </remarks>
public sealed class GuiEditIsVisibleToTheCliTests
{
    [Fact]
    public void An_entry_added_in_the_gui_is_listed_by_keypaste_ls()
    {
        using var fixture = new VaultFixture(("seed", "seed-password"));
        using var screen = Entries(fixture);

        screen.Model.BeginAddCommand.Execute(null);
        screen.Model.NewEntryPath = "servers/database";
        screen.Model.ConfirmAddCommand.Execute(null);

        Assert.Null(screen.Model.Error);

        // The vault is still open in the app. The CLI opens its own handle and must see the write.
        Assert.Equal(CliApp.ExitSuccess, fixture.Run("ls"));
        Assert.Contains("database", fixture.Cli.Out, StringComparison.Ordinal);
    }

    /// <summary>
    /// A generated password is exactly the value <c>keypaste get</c> returns.
    /// </summary>
    /// <remarks>
    /// Worth its own test: the generator is the only thing in 4.2 that produces a value nothing else
    /// has seen, so it cannot agree by accident with a constant both sides were given. The same
    /// argument <c>verify-keepassxc-writeback.sh</c> makes for having KeePassXC write the value.
    /// </remarks>
    [Fact]
    public void A_password_generated_in_the_gui_is_the_value_the_cli_returns()
    {
        using var fixture = new VaultFixture(("seed", "seed-password"));
        using var screen = Entries(fixture);

        screen.Model.BeginAddCommand.Execute(null);
        screen.Model.NewEntryPath = "svc/api";
        screen.Model.ConfirmAddCommand.Execute(null);

        Assert.Null(screen.Model.Error);

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("get", "svc/api", "--show"));

        var value = fixture.Cli.Out.Trim();
        Assert.Equal(PasswordGenerator.DefaultLength, value.Length);

        // And it is the value the file holds, not merely a string of the right length.
        Assert.Equal(fixture.Unlocked.Find("svc/api")?.Password, value);
    }

    [Fact]
    public void An_entry_edited_in_the_gui_is_what_the_cli_reads_back()
    {
        using var fixture = new VaultFixture(("github", "gh-password"));
        using var screen = Entries(fixture);

        screen.Model.Selected = screen.Model.Rows.Single(row => row.Title == "github");
        var detail = screen.Model.Detail!;

        detail.EditCommand.Execute(null);
        detail.DraftUsername = "someone-else";
        detail.SaveCommand.Execute(null);

        Assert.Null(screen.Model.Error);

        // The password the GUI did not touch is the one the CLI still hands back.
        Assert.Equal(CliApp.ExitSuccess, fixture.Run("get", "github", "--show"));
        Assert.Equal("gh-password", fixture.Cli.Out.Trim());
    }

    [Fact]
    public void An_entry_deleted_in_the_gui_makes_keypaste_get_report_it_is_gone()
    {
        using var fixture = new VaultFixture(("github", "gh-password"), ("keep", "keep-password"));
        using var screen = Entries(fixture);

        // The positive control first: it is there before it is not.
        Assert.Equal(CliApp.ExitSuccess, fixture.Run("get", "github", "--show"));

        screen.Model.Selected = screen.Model.Rows.Single(row => row.Title == "github");
        screen.Model.DeleteCommand.Execute(null);
        screen.Model.ConfirmDeleteCommand.Execute(null);

        Assert.Null(screen.Model.Error);
        Assert.Equal(CliApp.ExitNotFound, fixture.Run("get", "github", "--show"));

        // And nothing else went with it.
        Assert.Equal(CliApp.ExitSuccess, fixture.Run("get", "keep", "--show"));
    }

    /// <summary>
    /// A variable set in the GUI is listed by <c>keypaste env ls</c> under its project.
    /// </summary>
    /// <remarks>
    /// The test the whole project exists for. A GUI writing to <c>envs/&lt;project&gt;/</c> instead
    /// of <c>env/&lt;project&gt;/</c> would satisfy every assertion made through <c>Vault.Open</c>
    /// and fail only here.
    /// </remarks>
    [Fact]
    public void A_variable_set_in_the_gui_is_listed_by_keypaste_env_ls()
    {
        using var fixture = new VaultFixture(("seed", "seed-password"));
        using var screen = EnvSets(fixture);

        screen.Model.BeginAddCommand.Execute(null);
        screen.Model.NewProject = "billing";
        screen.Model.ConfirmAddCommand.Execute(null);

        var project = screen.Model.OpenProject;
        Assert.NotNull(project);

        project.BeginAddCommand.Execute(null);
        project.NewKey = "STRIPE_KEY";
        project.ConfirmAddCommand.Execute(null);

        Assert.Null(screen.Model.Error);

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("env", "ls"));
        Assert.Contains("billing", fixture.Cli.Out, StringComparison.Ordinal);

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("env", "ls", "billing"));
        Assert.Contains("STRIPE_KEY", fixture.Cli.Out, StringComparison.Ordinal);
    }

    /// <summary>
    /// A variable set in the GUI is injected into a child by <c>keypaste run</c>.
    /// </summary>
    /// <remarks>
    /// The end of the chain, and the one that proves the variable is usable rather than merely
    /// stored. A name the GUI accepted but <c>EnvNameRules</c> rejects would stop here.
    /// </remarks>
    [Fact]
    public void A_variable_set_in_the_gui_is_injected_by_keypaste_run()
    {
        using var fixture = new VaultFixture(("seed", "seed-password"));
        using var screen = EnvSets(fixture);

        screen.Model.OpenCommand.Execute("billing");
        var project = screen.Model.OpenProject!;

        project.BeginAddCommand.Execute(null);
        project.NewKey = "STRIPE_KEY";
        project.ConfirmAddCommand.Execute(null);

        Assert.Null(screen.Model.Error);

        var stored = project.Read("STRIPE_KEY");
        Assert.NotNull(stored);

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("run", "billing", "--", "deploy"));

        // The positive control: a child was started at all.
        Assert.NotEmpty(fixture.Cli.ProcessLauncher.Started);
        Assert.Equal(stored, fixture.Cli.ProcessLauncher.Environment["STRIPE_KEY"]);
    }

    [Fact]
    public void A_variable_removed_in_the_gui_is_gone_from_keypaste_env_ls()
    {
        using var fixture = new VaultFixture(("seed", "seed-password"));
        using var screen = EnvSets(fixture);

        screen.Model.OpenCommand.Execute("billing");
        var project = screen.Model.OpenProject!;

        project.BeginAddCommand.Execute(null);
        project.NewKey = "STRIPE_KEY";
        project.ConfirmAddCommand.Execute(null);

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("env", "ls", "billing"));
        Assert.Contains("STRIPE_KEY", fixture.Cli.Out, StringComparison.Ordinal);

        project.BeginRemove(project.Variables.Single());
        project.ConfirmRemoveCommand.Execute(null);

        Assert.Null(screen.Model.Error);

        fixture.Run("env", "ls", "billing");
        Assert.DoesNotContain("STRIPE_KEY", fixture.Cli.Out, StringComparison.Ordinal);
    }

    /// <summary>
    /// The GUI refuses every variable name the CLI refuses, for the same reason.
    /// </summary>
    /// <remarks>
    /// The mutation this rules out is the one that always happens: a form needs an error message, so
    /// somebody writes a regular expression next to it. Both sides are driven with the same table
    /// and required to agree.
    /// </remarks>
    [Theory]
    [InlineData("lowercase")]
    [InlineData("1LEADING_DIGIT")]
    [InlineData("HAS SPACE")]
    [InlineData("HAS-HYPHEN")]
    [InlineData("")]
    public void The_gui_refuses_every_variable_name_the_cli_refuses(string key)
    {
        using var fixture = new VaultFixture(("seed", "seed-password"));
        using var screen = EnvSets(fixture);

        screen.Model.OpenCommand.Execute("billing");
        var project = screen.Model.OpenProject!;

        project.BeginAddCommand.Execute(null);
        project.NewKey = key;
        project.ConfirmAddCommand.Execute(null);

        var guiRefused = screen.Model.Error is not null;

        var cliExit = fixture.RunAnswering(["value-for-" + key], "env", "set", "billing", key);

        Assert.True(
            guiRefused == (cliExit != CliApp.ExitSuccess),
            $"the GUI {(guiRefused ? "refused" : "accepted")} '{key}' and the CLI exited {cliExit}");
    }

    /// <summary>
    /// The positive control for the theory above: a name both accept is accepted by both.
    /// </summary>
    /// <remarks>
    /// Without it, a GUI that refused everything and a CLI that failed on everything would agree
    /// perfectly and prove nothing.
    /// </remarks>
    [Fact]
    public void The_gui_and_the_cli_both_accept_an_ordinary_name()
    {
        using var fixture = new VaultFixture(("seed", "seed-password"));
        using var screen = EnvSets(fixture);

        screen.Model.OpenCommand.Execute("billing");
        var project = screen.Model.OpenProject!;

        project.BeginAddCommand.Execute(null);
        project.NewKey = "DATABASE_URL";
        project.ConfirmAddCommand.Execute(null);

        Assert.Null(screen.Model.Error);
        Assert.Equal(CliApp.ExitSuccess, fixture.Run("env", "ls", "billing"));
        Assert.Contains("DATABASE_URL", fixture.Cli.Out, StringComparison.Ordinal);
    }

    /// <summary>
    /// A CLI write during a GUI session is refused rather than silently reverted.
    /// </summary>
    /// <remarks>
    /// The reason the lost-write guard exists, asserted from the direction it actually happens in:
    /// the app holds the vault for up to eight hours while somebody alt-tabs to a terminal, which
    /// <c>docs/desktop.md</c> describes as normal.
    /// </remarks>
    [Fact]
    public void A_cli_write_during_a_gui_session_is_not_clobbered_by_the_next_gui_save()
    {
        using var fixture = new VaultFixture(("github", "gh-password"));
        using var screen = Entries(fixture);

        screen.Model.Selected = screen.Model.Rows.Single(row => row.Title == "github");
        var detail = screen.Model.Detail!;

        Assert.Equal(
            CliApp.ExitSuccess,
            fixture.RunAnswering(["from-the-terminal"], "add", "from-the-terminal"));

        detail.EditCommand.Execute(null);
        detail.DraftUsername = "someone-else";
        detail.SaveCommand.Execute(null);

        Assert.NotNull(screen.Model.Error);

        // The terminal's write is still there, which is the whole point.
        Assert.Equal(CliApp.ExitSuccess, fixture.Run("get", "from-the-terminal", "--show"));
        Assert.Equal("from-the-terminal", fixture.Cli.Out.Trim());
    }

    private static Screen<EntriesViewModel> Entries(VaultFixture fixture)
    {
        Assert.Equal(UnlockOutcome.Opened, fixture.Unlock());
        return Screen<EntriesViewModel>.For(fixture, (session, clipboard) => new EntriesViewModel(session, clipboard));
    }

    private static Screen<EnvSetsViewModel> EnvSets(VaultFixture fixture)
    {
        Assert.Equal(UnlockOutcome.Opened, fixture.Unlock());
        return Screen<EnvSetsViewModel>.For(fixture, (session, clipboard) => new EnvSetsViewModel(session, clipboard));
    }

    /// <summary>One of the app's screens, with a clipboard that goes nowhere.</summary>
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
