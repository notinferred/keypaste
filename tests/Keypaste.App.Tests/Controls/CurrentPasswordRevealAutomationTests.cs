using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.App.Clipboard;
using Keypaste.App.Controls;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.Controls;

/// <summary>
/// The selected entry's current password: dots until its cell is held, and a screen that says
/// nothing different while it is (V.10, D-0300).
/// </summary>
/// <remarks>
/// The gesture is delivered as pointer events raised on the cell the template built, which keeps
/// the control's own press, release and capture handling and every binding the pane gives it. The
/// press the platform hit-tests through the window, and the frame it draws, are
/// <c>DrawnRevealTests</c>.
/// </remarks>
public sealed class CurrentPasswordRevealAutomationTests
{
    private const string _master = "correct horse battery staple";
    private const string _current = "SENTINEL-CURRENT-PASSWORD-5e1d7a";
    private const string _superseded = "SENTINEL-OLD-PASSWORD-4b8e21";
    private const string _neighbour = "SENTINEL-NEIGHBOUR-PASSWORD-80c2f4";

    [Fact]
    public Task Holding_the_drawn_cell_shows_the_password_and_releasing_hides_it() => HeadlessSession.On(() =>
    {
        using var screen = new EntryScreen();
        var dots = new string('•', _current.Length);

        Assert.Equal(dots, screen.Cell.Rendered);

        screen.Press();
        Assert.Equal(_current, screen.Cell.Rendered);

        screen.Release();
        Assert.Equal(dots, screen.Cell.Rendered);
    });

    [Fact]
    public Task Losing_the_pointer_ends_the_hold() => HeadlessSession.On(() =>
    {
        using var screen = new EntryScreen();

        screen.Press();
        Assert.Equal(_current, screen.Cell.Rendered);

        screen.Pointer.Capture(null);
        Drain();

        Assert.False(screen.Cell.IsRevealed);
        Assert.Equal(new string('•', _current.Length), screen.Cell.Rendered);
    });

    /// <summary>
    /// The pane reuses its template for the next entry, so the cell outlives the selection and has
    /// to drop what it was drawing.
    /// </summary>
    [Fact]
    public Task Selecting_another_entry_ends_the_hold() => HeadlessSession.On(() =>
    {
        using var screen = new EntryScreen();

        screen.Press();
        Assert.Equal(_current, screen.Cell.Rendered);

        screen.Model.Selected = screen.Model.Rows.Single(row => row.Title == "gitlab");
        Drain();

        Assert.Equal(new string('•', _neighbour.Length), screen.Cell.Rendered);
        AutomationSurface.AssertNothingExposes(screen.Window, _current);
    });

    [Fact]
    public Task Switching_screens_while_held_takes_the_password_off_the_screen() => HeadlessSession.On(() =>
    {
        using var screen = new EntryScreen();

        screen.Press();
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

        screen.Press();
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
    /// A press on a locked vault draws nothing, and its release still ends cleanly.
    /// </summary>
    [Fact]
    public Task A_hold_after_the_vault_locked_draws_only_dots() => HeadlessSession.On(() =>
    {
        using var screen = new EntryScreen();

        screen.Session.Lock(VaultLockReason.Manual);
        screen.Press();

        Assert.False(screen.Cell.IsRevealed);
        Assert.Equal(new string('•', _current.Length), screen.Cell.Rendered);

        screen.Release();
    });

    /// <summary>
    /// The whole window's surface is the same while the characters are drawn (D-0232), and the
    /// characters really were drawn when the second sweep ran.
    /// </summary>
    [Fact]
    public Task Revealing_changes_nothing_the_accessibility_bus_can_read() => HeadlessSession.On(() =>
    {
        using var screen = new EntryScreen();

        var atRest = AutomationSurface.Of(screen.Window);

        screen.Press();
        var whileHeld = AutomationSurface.Of(screen.Window);

        Assert.Equal(_current, screen.Cell.Rendered);
        Assert.NotEmpty(whileHeld);
        Assert.Equal(atRest, whileHeld);
        Assert.Contains(whileHeld, entry => entry.Text.Contains("github", StringComparison.Ordinal));

        AutomationSurface.AssertNothingExposes(screen.Window, _current);
        AutomationSurface.AssertNothingExposes(screen.Window, _superseded);
    });

    [Fact]
    public Task The_cell_is_named_by_what_it_does_and_contributes_no_peer() => HeadlessSession.On(() =>
    {
        using var screen = new EntryScreen();

        screen.Press();

        Assert.IsType<NoneAutomationPeer>(ControlAutomationPeer.CreatePeerForElement(screen.Cell));
        Assert.Equal("Hold to reveal the password", AutomationProperties.GetName(screen.Cell));
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

            Session = new AppVaultSession(new ManualClock());

            using (var master = TempVault.Secret(_master))
            {
                Assert.Equal(UnlockOutcome.Opened, Session.TryUnlock(path, master.Value));
            }

            Countdown = new ClipboardCountdown(new FakeClipboard(), new ManualClock());
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

        internal Pointer Pointer { get; } = new(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);

        internal RevealedValue Cell =>
            Window.GetVisualDescendants().OfType<RevealedValue>().Single(cell => cell.Name == "CurrentPassword");

        internal void Press()
        {
            Cell.RaiseEvent(new PointerPressedEventArgs(
                Cell, Pointer, Window, default(Point), 0, PointerPointProperties.None, KeyModifiers.None));
            Drain();
        }

        internal void Release()
        {
            Cell.RaiseEvent(new PointerReleasedEventArgs(
                Cell, Pointer, Window, default(Point), 0, PointerPointProperties.None, KeyModifiers.None, MouseButton.Left));
            Drain();
        }

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
