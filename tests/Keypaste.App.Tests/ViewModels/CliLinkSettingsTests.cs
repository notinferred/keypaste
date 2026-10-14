using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core.Linking;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>Settings › Agents and the CLI: linking the keypaste a terminal runs, and replacing another program only once the person confirms (G.5).</summary>
public sealed class CliLinkSettingsTests
{
    private const string _backup = FakeCliLink.Location + ".keypaste-backup";

    [Fact]
    public void Without_a_link_the_row_is_not_offered()
    {
        using var home = new TempHome();
        using var session = new AppVaultSession(new ManualClock());
        using var screen = new SettingsViewModel(session, home.Path, new DesktopPreferences(home.Path), _ => { });

        Assert.False(screen.CliLinkSupported);
        Assert.False(screen.OffersCliLink);
        Assert.False(screen.OffersCliReplace);
        Assert.False(screen.LinkCliCommand.CanExecute(null));
        Assert.False(screen.BeginReplaceCliCommand.CanExecute(null));
        Assert.Equal(string.Empty, screen.CliLinkText);
    }

    [Fact]
    public async Task Link_links_once_and_then_reads_this_copy()
    {
        var link = FakeCliLink.At(CliLinkState.Absent);
        using var home = new TempHome();
        using var session = new AppVaultSession(new ManualClock());
        using var screen = Screen(home, session, link);

        Assert.True(screen.CliLinkSupported);
        Assert.StartsWith($"Link this app's keypaste at {FakeCliLink.Location} so a terminal can run it.", screen.CliLinkText, StringComparison.Ordinal);
        Assert.True(screen.OffersCliLink);
        Assert.False(screen.OffersCliReplace);

        await screen.LinkCliCommand.ExecuteAsync();

        Assert.Equal(1, link.Links);
        Assert.Equal(0, link.Replaces);
        Assert.Equal($"A terminal runs this app's keypaste from {FakeCliLink.Location}.", screen.CliLinkText);
        Assert.False(screen.OffersCliLink);
        Assert.False(screen.LinkCliCommand.CanExecute(null));
        Assert.Contains(FakeCliLink.Location, screen.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Another_copys_link_is_named_and_Link_points_it_here()
    {
        var link = FakeCliLink.At(CliLinkState.OtherCopy, linkedTo: "/home/person/Applications/keypaste-old.AppImage");
        using var home = new TempHome();
        using var session = new AppVaultSession(new ManualClock());
        using var screen = Screen(home, session, link);

        Assert.Equal($"{FakeCliLink.Location} runs keypaste from /home/person/Applications/keypaste-old.AppImage.", screen.CliLinkText);
        Assert.True(screen.OffersCliLink);
        Assert.False(screen.OffersCliReplace);

        await screen.LinkCliCommand.ExecuteAsync();

        Assert.Equal(1, link.Links);
        Assert.Equal($"A terminal runs this app's keypaste from {FakeCliLink.Location}.", screen.CliLinkText);
    }

    [Fact]
    public void The_PATH_note_shows_only_while_the_links_directory_is_off_PATH()
    {
        using var home = new TempHome();
        using var session = new AppVaultSession(new ManualClock());
        using var onPath = Screen(home, session, FakeCliLink.At(CliLinkState.ThisCopy));
        using var offPath = Screen(home, session, FakeCliLink.At(CliLinkState.ThisCopy, onPath: false));

        Assert.False(onPath.HasCliLinkNote);
        Assert.True(offPath.HasCliLinkNote);
        Assert.StartsWith($"{Path.GetDirectoryName(FakeCliLink.Location)} is not on PATH yet.", offPath.CliLinkNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_keypaste_a_terminal_may_find_first_is_named_and_the_link_is_never_reported_as_what_a_terminal_runs()
    {
        const string MacLink = "/usr/local/bin/keypaste";
        const string Earlier = "/Users/person/.local/bin/keypaste";
        var note = $"A terminal may run {Earlier} instead, if its folder comes before {Path.GetDirectoryName(MacLink)} on PATH. " +
            "Move or delete it so a terminal runs this app's keypaste.";
        var link = FakeCliLink.At(CliLinkState.Absent, location: MacLink, earlierCopy: Earlier);
        using var home = new TempHome();
        using var session = new AppVaultSession(new ManualClock());
        using var screen = Screen(home, session, link);

        Assert.StartsWith($"Link this app's keypaste at {MacLink} so a terminal can run it.", screen.CliLinkText, StringComparison.Ordinal);
        Assert.Equal(note, screen.CliLinkNote);
        Assert.True(screen.OffersCliLink);

        await screen.LinkCliCommand.ExecuteAsync();

        Assert.Equal(1, link.Links);
        Assert.Equal($"{MacLink} runs this app's keypaste.", screen.CliLinkText);
        Assert.Equal(note, screen.CliLinkNote);
        Assert.True(screen.HasCliLinkNote);
        Assert.False(screen.OffersCliLink);
    }

    [Fact]
    public void A_foreign_file_is_named_and_offered_Replace_never_Link()
    {
        using var home = new TempHome();
        using var session = new AppVaultSession(new ManualClock());
        using var file = Screen(home, session, FakeCliLink.At(CliLinkState.Foreign));
        using var symlink = Screen(home, session, FakeCliLink.At(CliLinkState.Foreign, linkedTo: "/opt/other/bin/keypaste"));

        Assert.Equal($"{FakeCliLink.Location} is another program, not this app's keypaste.", file.CliLinkText);
        Assert.Equal($"{FakeCliLink.Location} is another program, not this app's keypaste. It links to /opt/other/bin/keypaste.", symlink.CliLinkText);

        foreach (var screen in new[] { file, symlink })
        {
            Assert.True(screen.OffersCliReplace);
            Assert.True(screen.BeginReplaceCliCommand.CanExecute(null));
            Assert.False(screen.OffersCliLink);
            Assert.False(screen.LinkCliCommand.CanExecute(null));
            Assert.False(screen.HasCliLinkNote);
        }
    }

    [Fact]
    public void Replace_only_asks_and_Keep_it_closes_the_question_changing_nothing()
    {
        var link = FakeCliLink.At(CliLinkState.Foreign);
        using var home = new TempHome();
        using var session = new AppVaultSession(new ManualClock());
        using var screen = Screen(home, session, link);

        screen.BeginReplaceCliCommand.Execute(null);

        Assert.True(screen.IsConfirmingCliReplace);
        Assert.StartsWith(
            $"Replace {FakeCliLink.Location} with this app's keypaste? The file there now is kept as {_backup}.",
            screen.CliReplacePrompt,
            StringComparison.Ordinal);
        Assert.False(screen.BeginReplaceCliCommand.CanExecute(null));
        Assert.Equal(0, link.Replaces);
        Assert.Equal(0, link.Links);

        screen.CancelReplaceCliCommand.Execute(null);

        Assert.False(screen.IsConfirmingCliReplace);
        Assert.False(screen.ConfirmReplaceCliCommand.CanExecute(null));
        Assert.True(screen.OffersCliReplace);
        Assert.Equal(0, link.Replaces);
        Assert.Equal(0, link.Links);
        Assert.Equal(string.Empty, screen.Message);
    }

    [Fact]
    public async Task A_confirmed_Replace_replaces_once_and_names_where_the_old_file_is()
    {
        var link = FakeCliLink.At(CliLinkState.Foreign);
        using var home = new TempHome();
        using var session = new AppVaultSession(new ManualClock());
        using var screen = Screen(home, session, link);

        Assert.False(screen.ConfirmReplaceCliCommand.CanExecute(null));
        screen.BeginReplaceCliCommand.Execute(null);
        await screen.ConfirmReplaceCliCommand.ExecuteAsync();

        Assert.Equal(1, link.Replaces);
        Assert.Equal(0, link.Links);
        Assert.False(screen.IsConfirmingCliReplace);
        Assert.Equal($"{FakeCliLink.Location} now runs this app's keypaste; the old file is {_backup}.", screen.Message);
        Assert.Equal($"A terminal runs this app's keypaste from {FakeCliLink.Location}.", screen.CliLinkText);
        Assert.False(screen.OffersCliReplace);
    }

    [Fact]
    public void A_taken_backup_offers_no_Replace_and_says_why()
    {
        using var home = new TempHome();
        using var session = new AppVaultSession(new ManualClock());
        using var screen = Screen(home, session, FakeCliLink.At(CliLinkState.Foreign, backupExists: true));

        Assert.False(screen.OffersCliReplace);
        Assert.False(screen.BeginReplaceCliCommand.CanExecute(null));
        Assert.False(screen.OffersCliLink);
        Assert.Equal(
            $"{_backup} already exists, so keypaste replaces nothing. Move or delete it to replace {FakeCliLink.Location}.",
            screen.CliLinkNote);
    }

    [Theory]
    [InlineData(CliLinkState.Absent)]
    [InlineData(CliLinkState.Foreign)]
    public void A_translocated_copy_offers_neither_and_says_why(CliLinkState state)
    {
        using var home = new TempHome();
        using var session = new AppVaultSession(new ManualClock());
        using var screen = Screen(home, session, FakeCliLink.At(state, translocated: true));

        Assert.False(screen.OffersCliLink);
        Assert.False(screen.OffersCliReplace);
        Assert.False(screen.LinkCliCommand.CanExecute(null));
        Assert.False(screen.BeginReplaceCliCommand.CanExecute(null));
        Assert.StartsWith("macOS is running this copy from a temporary location.", screen.CliLinkNote, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CliLinkOutcome.Cancelled, null, null, "Nothing was linked.")]
    [InlineData(CliLinkOutcome.Refused, CliLinkRefusal.NotKeypaste, null, FakeCliLink.Location + " changed before keypaste linked it, so it was left as it is.")]
    [InlineData(CliLinkOutcome.Refused, CliLinkRefusal.BackupExists, null, _backup + " already exists, so keypaste replaces nothing. Move or delete it to replace " + FakeCliLink.Location + ".")]
    [InlineData(CliLinkOutcome.Failed, null, "No space left on device", "keypaste could not link " + FakeCliLink.Location + ": No space left on device")]
    public async Task A_link_that_did_not_happen_says_so_and_leaves_the_row(CliLinkOutcome outcome, CliLinkRefusal? refusal, string? reason, string message)
    {
        var link = FakeCliLink.At(CliLinkState.Absent, answer: new CliLinkResult(outcome, refusal, reason));
        using var home = new TempHome();
        using var session = new AppVaultSession(new ManualClock());
        using var screen = Screen(home, session, link);

        await screen.LinkCliCommand.ExecuteAsync();

        Assert.Equal(1, link.Links);
        Assert.Equal(message, screen.Message);
        Assert.StartsWith("Link this app's keypaste at", screen.CliLinkText, StringComparison.Ordinal);
        Assert.True(screen.OffersCliLink);
        Assert.True(screen.LinkCliCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(CliLinkOutcome.Cancelled, null, "Nothing was linked.")]
    [InlineData(CliLinkOutcome.Refused, CliLinkRefusal.BackupExists, _backup + " already exists, so keypaste replaces nothing. Move or delete it to replace " + FakeCliLink.Location + ".")]
    public async Task A_replacement_that_did_not_happen_says_so_and_closes_the_question(CliLinkOutcome outcome, CliLinkRefusal? refusal, string message)
    {
        var link = FakeCliLink.At(CliLinkState.Foreign, answer: new CliLinkResult(outcome, refusal));
        using var home = new TempHome();
        using var session = new AppVaultSession(new ManualClock());
        using var screen = Screen(home, session, link);

        screen.BeginReplaceCliCommand.Execute(null);
        await screen.ConfirmReplaceCliCommand.ExecuteAsync();

        Assert.Equal(1, link.Replaces);
        Assert.Equal(message, screen.Message);
        Assert.False(screen.IsConfirmingCliReplace);
        Assert.True(screen.OffersCliReplace);
    }

    [Fact]
    public async Task A_link_under_way_cannot_be_started_again()
    {
        using var gate = new SemaphoreSlim(0);
        var link = FakeCliLink.At(CliLinkState.Absent, gate: gate);
        using var home = new TempHome();
        using var session = new AppVaultSession(new ManualClock());
        using var screen = Screen(home, session, link);

        var first = screen.LinkCliCommand.ExecuteAsync();

        Assert.True(screen.IsCliLinking);
        Assert.False(screen.LinkCliCommand.CanExecute(null));
        await screen.LinkCliCommand.ExecuteAsync();

        gate.Release();
        await first;

        Assert.Equal(1, link.Links);
        Assert.False(screen.IsCliLinking);
    }

    [Fact]
    public async Task A_replacement_under_way_cannot_be_started_again_or_dismissed()
    {
        using var gate = new SemaphoreSlim(0);
        var link = FakeCliLink.At(CliLinkState.Foreign, gate: gate);
        using var home = new TempHome();
        using var session = new AppVaultSession(new ManualClock());
        using var screen = Screen(home, session, link);

        screen.BeginReplaceCliCommand.Execute(null);
        var first = screen.ConfirmReplaceCliCommand.ExecuteAsync();

        Assert.True(screen.IsCliLinking);
        Assert.False(screen.ConfirmReplaceCliCommand.CanExecute(null));
        Assert.False(screen.CancelReplaceCliCommand.CanExecute(null));
        await screen.ConfirmReplaceCliCommand.ExecuteAsync();
        screen.CancelReplaceCliCommand.Execute(null);
        Assert.True(screen.IsConfirmingCliReplace);

        gate.Release();
        await first;

        Assert.Equal(1, link.Replaces);
        Assert.Equal(0, link.Links);
        Assert.False(screen.IsConfirmingCliReplace);
    }

    private static SettingsViewModel Screen(TempHome home, AppVaultSession session, FakeCliLink link) =>
        new(session, home.Path, new DesktopPreferences(home.Path), _ => { }) { CliLink = link };
}
