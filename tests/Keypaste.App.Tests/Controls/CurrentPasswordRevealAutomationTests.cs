using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.App.Clipboard;
using Keypaste.App.Controls;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Controls;

/// <summary>
/// The selected entry's current password: dots until its cell is revealed, and a screen that says
/// nothing different but its toggle while it is (V.10).
/// </summary>
/// <remarks>
/// The click is hit-tested through the window onto the toggle the template built, which keeps every
/// binding the pane gives it. The frame it draws is <c>DrawnRevealTests</c>.
/// </remarks>
public sealed class CurrentPasswordRevealAutomationTests
{
    private const string _master = "correct horse battery staple";
    private const string _current = "SENTINEL-CURRENT-PASSWORD-5e1d7a";
    private const string _superseded = "SENTINEL-OLD-PASSWORD-4b8e21";
    private const string _neighbour = "SENTINEL-NEIGHBOUR-PASSWORD-80c2f4";

    [Fact]
    public Task A_click_on_Reveal_shows_the_password_and_a_second_hides_it() => HeadlessSession.On(() =>
    {
        using var screen = new EntryScreen();
        var dots = new string('•', _current.Length);

        Assert.Equal(dots, screen.Cell.Rendered);

        screen.Click();
        Assert.Equal(_current, screen.Cell.Rendered);

        screen.Click();
        Assert.Equal(dots, screen.Cell.Rendered);
    });

    [Fact]
    public Task A_click_on_the_cell_leaves_it_masked() => HeadlessSession.On(() =>
    {
        using var screen = new EntryScreen();

        WindowInput.Tap(screen.Window, screen.Cell);

        Assert.False(screen.Cell.IsRevealed);
        Assert.Equal(new string('•', _current.Length), screen.Cell.Rendered);
    });

    /// <summary>
    /// The pane reuses its template for the next entry, so the cell outlives the selection and has
    /// to drop what it was drawing.
    /// </summary>
    [Fact]
    public Task Selecting_another_entry_hides_it() => HeadlessSession.On(() =>
    {
        using var screen = new EntryScreen();

        screen.Click();
        Assert.Equal(_current, screen.Cell.Rendered);

        screen.Model.Selected = screen.Model.Rows.Single(row => row.Title == "gitlab");
        Drain();

        Assert.Equal(new string('•', _neighbour.Length), screen.Cell.Rendered);
        AutomationSurface.AssertNothingExposes(screen.Window, _current);
    });

    [Fact]
    public Task Switching_screens_while_shown_takes_the_password_off_the_screen() => HeadlessSession.On(() =>
    {
        using var screen = new EntryScreen();

        screen.Click();
        var cell = screen.Cell;
        Assert.True(cell.IsRevealed);

        screen.Window.Content = null;
        Drain();

        Assert.False(cell.IsRevealed);
    });

    /// <summary>Driven the way the app locks: the session locks and the screen reloads.</summary>
    [Fact]
    public Task Locking_takes_the_password_off_the_screen() => HeadlessSession.On(() =>
    {
        using var screen = new EntryScreen();

        screen.Click();
        var cell = screen.Cell;
        Assert.True(cell.IsRevealed);

        screen.Session.Lock(VaultLockReason.Manual);
        screen.Model.Reload();
        Drain();

        Assert.Null(screen.Model.Detail);
        Assert.False(cell.IsRevealed);
        AutomationSurface.AssertNothingExposes(screen.Window, _current);
    });

    /// <summary>
    /// A click on a locked vault draws nothing.
    /// </summary>
    [Fact]
    public Task A_click_after_the_vault_locked_draws_only_dots() => HeadlessSession.On(() =>
    {
        using var screen = new EntryScreen();

        screen.Session.Lock(VaultLockReason.Manual);
        screen.Click();

        Assert.False(screen.Cell.IsRevealed);
        Assert.Equal(new string('•', _current.Length), screen.Cell.Rendered);
    });

    /// <summary>
    /// Outside its toggle, the whole window's surface is the same while the characters are drawn, and the
    /// characters really were drawn when the second sweep ran.
    /// </summary>
    [Fact]
    public Task Revealing_changes_nothing_the_accessibility_bus_can_read() => HeadlessSession.On(() =>
    {
        using var screen = new EntryScreen();

        // At rest with focus on the toggle and the pointer over it, so the only difference the click can make is the click.
        ToolTip.SetServiceEnabled(screen.Toggle, false);
        screen.Toggle.Focus();
        WindowInput.Hover(screen.Window, screen.Toggle);
        var atRest = AutomationSurface.OutsideTheToggles(screen.Window);

        screen.Click();
        var whileShown = AutomationSurface.OutsideTheToggles(screen.Window);

        Assert.Equal(_current, screen.Cell.Rendered);
        Assert.NotEmpty(whileShown);
        Assert.Equal(atRest, whileShown);
        Assert.Contains(whileShown, entry => entry.Text.Contains("github", StringComparison.Ordinal));

        AutomationSurface.AssertNothingExposes(screen.Window, _current);
        AutomationSurface.AssertNothingExposes(screen.Window, _superseded);
    });

    [Fact]
    public Task The_cell_is_named_by_what_it_is_and_contributes_no_peer() => HeadlessSession.On(() =>
    {
        using var screen = new EntryScreen();

        screen.Click();

        Assert.IsType<NoneAutomationPeer>(ControlAutomationPeer.CreatePeerForElement(screen.Cell));
        Assert.Equal("The password", AutomationProperties.GetName(screen.Cell));
    });

    private static void Drain() => Dispatcher.UIThread.RunJobs();

    /// <summary>
    /// The Entries screen with "github" selected: a password replaced once, so it has a history,
    /// beside a neighbour whose password is a different length.
    /// </summary>
    private sealed class EntryScreen : IDisposable
    {
        private readonly string _directory;

        internal EntryScreen()
        {
            _directory = Directory.CreateTempSubdirectory("keypaste-current-reveal-").FullName;
            var path = Path.Combine(_directory, "vault.kdbx");

            using (var vault = Vault.Create(path, _master))
            {
                vault.AddEntry(new VaultEntry { Title = "github", Username = "me", Password = _superseded });
                vault.UpdateEntry(new VaultEntry { Title = "github", Username = "me", Password = _current });
                vault.AddEntry(new VaultEntry { Title = "gitlab", Username = "me", Password = _neighbour });
                vault.Save();
            }

            Session = new AppVaultSession(new ManualClock(AppClock.Start));

            using (var master = TempVault.Secret(_master))
            {
                Assert.Equal(UnlockOutcome.Opened, Session.TryUnlock(path, master.Value));
            }

            Countdown = new ClipboardCountdown(new FakeClipboard(), new ManualClock(AppClock.Start));
            Model = new EntriesViewModel(Session, Countdown);

            Window = new Window { Content = new EntriesView { DataContext = Model } };
            Window.Show();
            Drain();

            Model.Selected = Model.Rows.Single(row => row.Title == "github");
            Drain();
        }

        internal AppVaultSession Session { get; }

        internal ClipboardCountdown Countdown { get; }

        internal EntriesViewModel Model { get; }

        internal Window Window { get; }

        internal RevealedValue Cell =>
            Window.GetVisualDescendants().OfType<RevealedValue>().Single(cell => cell.Name == "CurrentPassword");

        internal RevealButton Toggle =>
            Window.GetVisualDescendants().OfType<RevealButton>().Single(button => ReferenceEquals(button.Target, Cell));

        internal void Click() => WindowInput.Tap(Window, Toggle);

        public void Dispose()
        {
            Model.Dispose();
            Countdown.Dispose();
            Session.Dispose();
            Window.Close();

            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
