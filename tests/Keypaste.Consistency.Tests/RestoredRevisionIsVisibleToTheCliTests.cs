using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Cli;
using Xunit;

namespace Keypaste.Consistency.Tests;

/// <summary>
/// A revision restored in the desktop app is the value the shipped CLI hands back, and the entry it
/// was restored on is still the same entry.
/// </summary>
/// <remarks>
/// <para>
/// V.2b's claim is not that <c>RestoreRevision</c> works — <c>VaultHistoryTests</c> establishes
/// that — but that the button on the pane reaches it, saves, and leaves a vault the shipped reader
/// agrees about. Only a test holding both front ends can ask that: the restore is a GUI action and
/// <c>keypaste get --show</c> is the reader.
/// </para>
/// <para>
/// <b>The mutations that must make this file fail:</b> a restore that never saves, which every
/// in-memory assertion elsewhere would still pass, and one that restores a different index than the
/// row it was pressed for. The displaced value kept in history (D-0014) and the entry's UUID are
/// asserted on the reopened file by <c>EntryHistoryTests</c> and <c>VaultHistoryTests</c>.
/// </para>
/// </remarks>
public sealed class RestoredRevisionIsVisibleToTheCliTests
{
    private const string _oldest = "v0-the-original/9c41ba";
    private const string _middle = "v1-then+this=one";
    private const string _current = "v2_and_now_this";

    [Fact]
    public void A_revision_restored_in_the_gui_is_the_value_the_cli_returns()
    {
        using var fixture = new VaultFixture(("github", _oldest));
        using var screen = Entries(fixture);

        var detail = Select(screen, "github");
        Replace(detail, _middle);
        Replace(detail, _current);

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("get", "github", "--show"));
        Assert.Equal(_current, fixture.Cli.Out.Trim());

        Restore(detail, oldest: true);

        Assert.Null(screen.Model.Error);
        Assert.Equal(CliApp.ExitSuccess, fixture.Run("get", "github", "--show"));
        Assert.Equal(_oldest, fixture.Cli.Out.Trim());

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("ls"));
        Assert.Single(
            fixture.Cli.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            line => line.Contains("github", StringComparison.Ordinal));
    }

    /// <summary>
    /// Revisions the shipped CLI wrote are the ones the app restores, and the CLI injects the result.
    /// </summary>
    /// <remarks>
    /// The one test here whose earlier values were not produced by the code under test: four
    /// <c>env set</c> calls write them, the first creating the project's home entry and each later one
    /// a revision of it. A restore that only understood its own writes would pass everything above
    /// and fail here.
    /// </remarks>
    [Fact]
    public void Revisions_the_cli_wrote_are_the_ones_the_gui_restores()
    {
        using var fixture = new VaultFixture(("seed", "seed-password"));

        foreach (var value in new[] { "v1-first", "v2-second", "v3-third", "v4-current" })
        {
            Assert.Equal(
                CliApp.ExitSuccess,
                fixture.RunAnswering([value], "env", "set", "billing", "ROTATED"));
        }

        using var screen = Entries(fixture);
        var detail = Select(screen, ".env");

        Assert.Equal(3, detail.History.Rows.Count);

        Restore(detail, oldest: true);

        Assert.Null(screen.Model.Error);
        Assert.Equal(CliApp.ExitSuccess, fixture.Run("get", "env/billing/.env", "--field", "ROTATED", "--show"));
        Assert.Equal("v1-first", fixture.Cli.Out.Trim());

        Assert.Equal(CliApp.ExitSuccess, fixture.Run("run", "billing", "--", "deploy"));
        Assert.NotEmpty(fixture.Cli.ProcessLauncher.Started);
        Assert.Equal("v1-first", fixture.Cli.ProcessLauncher.Environment["ROTATED"]);
    }

    private static EntryDetailViewModel Select(Screen screen, string title)
    {
        screen.Model.Selected = screen.Model.Rows.Single(row => row.Title == title);

        var detail = screen.Model.Detail!;
        detail.History.ToggleCommand.Execute(null);

        return detail;
    }

    private static void Replace(EntryDetailViewModel detail, string password)
    {
        detail.EditCommand.Execute(null);

        foreach (var c in password)
        {
            detail.NewPassword.Type(c);
        }

        detail.SaveCommand.Execute(null);
    }

    private static void Restore(EntryDetailViewModel detail, bool oldest)
    {
        var history = detail.History;
        history.Selected = oldest ? history.Rows[^1] : history.Rows[0];
        history.RestoreCommand.Execute(null);
    }

    private static Screen Entries(VaultFixture fixture)
    {
        Assert.Equal(UnlockOutcome.Opened, fixture.Unlock());

        return Screen.For(fixture);
    }

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
}
