using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Cli;
using Keypaste.Core;
using Xunit;

namespace Keypaste.Consistency.Tests;

/// <summary>
/// A secret entered in the desktop app is the one the shipped CLI hands back, character for
/// character, and replacing one keeps what it replaced.
/// </summary>
/// <remarks>
/// <para>
/// 4.9's claim is not that a value reached the file — <c>Vault.Open</c> would show that — but that
/// it reached it <b>unchanged</b>, and that is only provable against something that did not put it
/// there. The CLI is asked, the way <see cref="GuiEditIsVisibleToTheCliTests"/> asks it.
/// </para>
/// <para>
/// <b>The mutations that must make this file fail:</b> a field that stores the mask instead of the
/// characters; a buffer read after it was cleared, giving an empty password with no error; a
/// replacement written with <c>AddEntry</c> or a delete-and-re-add, which loses the old value
/// instead of keeping it in history (D-0014); and a paste that strips what it cannot accept rather
/// than refusing it, which stores a secret that differs from the one on the clipboard.
/// </para>
/// </remarks>
public sealed class ExistingSecretIsVisibleToTheCliTests
{
    // Every character class a provider actually issues, so a field that quietly dropped one is not
    // hidden by a fixture made of letters.
    private const string _typed = "s3cr3t/from+the=provider_9c41ba";
    private const string _pasted = "sk_live_51Hx8zQ2eZvKYlo2C/aBc+dEf=";

    [Fact]
    public void A_password_typed_in_the_gui_is_the_value_the_cli_returns()
    {
        using var fixture = new VaultFixture(("seed", "seed-password"));
        using var screen = Entries(fixture);

        screen.Model.BeginAddCommand.Execute(null);
        screen.Model.NewEntryPath = "svc/api";
        screen.Model.GeneratePassword = false;
        Enter(screen.Model.NewPassword, _typed);
        screen.Model.ConfirmAddCommand.Execute(null);

        Assert.Null(screen.Model.Error);

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("get", "svc/api", "--show"));
        Assert.Equal(_typed, fixture.Cli.Out.Trim());
    }

    [Fact]
    public async Task A_password_pasted_in_the_gui_is_the_value_the_cli_returns()
    {
        using var fixture = new VaultFixture(("seed", "seed-password"));
        using var screen = Entries(fixture);

        screen.Clipboard.Plant(_pasted);

        screen.Model.BeginAddCommand.Execute(null);
        screen.Model.NewEntryPath = "svc/api";
        screen.Model.GeneratePassword = false;
        await screen.Model.NewPassword.Paste();
        screen.Model.ConfirmAddCommand.Execute(null);

        Assert.Null(screen.Model.Error);

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("get", "svc/api", "--show"));
        Assert.Equal(_pasted, fixture.Cli.Out.Trim());
    }

    /// <summary>
    /// A password replaced in the GUI is what the CLI reads back, and the old one is still in the
    /// entry's KeePass history rather than gone (D-0014).
    /// </summary>
    [Fact]
    public void A_replaced_password_is_the_value_the_cli_returns_and_costs_one_history_item()
    {
        using var fixture = new VaultFixture(("github", "gh-password"));
        using var screen = Entries(fixture);

        Assert.Equal(0, HistoryOf(fixture, string.Empty, "github"));

        screen.Model.Selected = screen.Model.Rows.Single(row => row.Title == "github");
        var detail = screen.Model.Detail!;

        detail.EditCommand.Execute(null);
        Enter(detail.NewPassword, _typed);
        detail.SaveCommand.Execute(null);

        Assert.Null(screen.Model.Error);

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("get", "github", "--show"));
        Assert.Equal(_typed, fixture.Cli.Out.Trim());

        Assert.Equal(1, HistoryOf(fixture, string.Empty, "github"));
    }

    /// <summary>
    /// The same edit that replaces the password also changes a field beside it, and the two cost
    /// one history item between them rather than one each.
    /// </summary>
    [Fact]
    public void A_replacement_alongside_a_field_edit_costs_one_history_item()
    {
        using var fixture = new VaultFixture(("github", "gh-password"));
        using var screen = Entries(fixture);

        screen.Model.Selected = screen.Model.Rows.Single(row => row.Title == "github");
        var detail = screen.Model.Detail!;

        detail.EditCommand.Execute(null);
        detail.DraftUsername = "someone-else";
        Enter(detail.NewPassword, _typed);
        detail.SaveCommand.Execute(null);

        Assert.Null(screen.Model.Error);
        Assert.Equal(1, HistoryOf(fixture, string.Empty, "github"));
    }

    [Fact]
    public void A_variable_value_typed_in_the_gui_is_injected_by_keypaste_run()
    {
        using var fixture = new VaultFixture(("seed", "seed-password"));
        using var screen = EnvSets(fixture);

        screen.Model.OpenCommand.Execute("billing");
        var project = screen.Model.OpenProject!;

        project.BeginAddCommand.Execute(null);
        project.NewKey = "STRIPE_KEY";
        project.GenerateValue = false;
        Enter(project.NewValue, _typed);
        project.ConfirmAddCommand.Execute(null);

        Assert.Null(screen.Model.Error);

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("run", "billing", "--", "deploy"));

        // The positive control: a child was started at all.
        Assert.NotEmpty(fixture.Cli.ProcessLauncher.Started);
        Assert.Equal(_typed, fixture.Cli.ProcessLauncher.Environment["STRIPE_KEY"]);
    }

    [Fact]
    public async Task A_variable_value_pasted_in_the_gui_is_injected_by_keypaste_run()
    {
        using var fixture = new VaultFixture(("seed", "seed-password"));
        using var screen = EnvSets(fixture);

        screen.Clipboard.Plant(_pasted);

        screen.Model.OpenCommand.Execute("billing");
        var project = screen.Model.OpenProject!;

        project.BeginAddCommand.Execute(null);
        project.NewKey = "STRIPE_KEY";
        project.GenerateValue = false;
        await project.NewValue.Paste();
        project.ConfirmAddCommand.Execute(null);

        Assert.Null(screen.Model.Error);

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("run", "billing", "--", "deploy"));
        Assert.Equal(_pasted, fixture.Cli.ProcessLauncher.Environment["STRIPE_KEY"]);
    }

    [Fact]
    public void A_replaced_variable_value_reaches_the_cli_and_costs_one_history_item()
    {
        using var fixture = new VaultFixture(("seed", "seed-password"));

        // Written from the terminal before the app opens the file: the value being replaced came
        // from somewhere else, and the app's session would otherwise not know it was there.
        Assert.Equal(CliApp.ExitSuccess, fixture.RunAnswering(["the-old-value"], "env", "set", "billing", "STRIPE_KEY"));

        using var screen = EnvSets(fixture);

        screen.Model.OpenCommand.Execute("billing");
        var project = screen.Model.OpenProject!;

        Assert.Equal(0, HistoryOf(fixture, "env/billing", "STRIPE_KEY"));

        project.BeginReplace(project.Variables.Single(row => row.Key == "STRIPE_KEY"));
        Enter(project.ReplacementValue, _typed);
        project.ConfirmReplaceCommand.Execute(null);

        Assert.Null(screen.Model.Error);

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("env", "ls", "billing"));
        Assert.Contains("STRIPE_KEY", fixture.Cli.Out, StringComparison.Ordinal);

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("run", "billing", "--", "deploy"));
        Assert.Equal(_typed, fixture.Cli.ProcessLauncher.Environment["STRIPE_KEY"]);

        Assert.Equal(1, HistoryOf(fixture, "env/billing", "STRIPE_KEY"));
    }

    private static void Enter(SecretField field, string value)
    {
        foreach (var c in value)
        {
            field.Type(c);
        }
    }

    /// <summary>History items on one entry, read from the file rather than from the open session.</summary>
    private static int HistoryOf(VaultFixture fixture, string groupPath, string title)
    {
        using var vault = Vault.Open(fixture.VaultPath, VaultFixture.Master);
        return vault.ReadHistory(new EntryName(groupPath, title))?.Count ?? -1;
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

    /// <summary>One of the app's screens, over a clipboard a test can put text on.</summary>
    private sealed class Screen<T> : IDisposable
        where T : class, IDisposable
    {
        private readonly ClipboardCountdown _countdown;

        private Screen(FakeClipboard clipboard, ClipboardCountdown countdown, T model)
        {
            Clipboard = clipboard;
            _countdown = countdown;
            Model = model;
        }

        internal T Model { get; }

        internal FakeClipboard Clipboard { get; }

        internal static Screen<T> For(VaultFixture fixture, Func<AppVaultSession, ClipboardCountdown, T> build)
        {
            var clipboard = new FakeClipboard();
            var countdown = new ClipboardCountdown(clipboard, TimeProvider.System);

            try
            {
                return new Screen<T>(clipboard, countdown, build(fixture.Session, countdown));
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
