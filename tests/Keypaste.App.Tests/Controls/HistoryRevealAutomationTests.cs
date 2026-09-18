using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
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
/// A superseded password on the entry pane: dots until it is held, and a screen that says
/// nothing different while it does.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not lean on <see cref="RevealedValueTests"/>.</b> That file proves the control, built by
/// hand against a fake row, outside any template. This one proves the <em>screen</em>: the bindings
/// in the comparison template, the labels and buttons around the cell, and the list item the
/// selection came from. Three of 4.9's four masked fields needed their own evidence for the same
/// reason, and a drawn value is the harder case.
/// </para>
/// <para>
/// <b>D-0099 for a value that is deliberately on screen (D-0232).</b> The typed-field differential
/// compares two secrets of equal length, guarded by the mask reaching the automation tree. Neither
/// half transfers: a revision takes no keystrokes, and while it is revealed the cell contributes
/// nothing at all to the tree, so two empty surfaces would compare equal against any
/// implementation. What is compared instead is the whole window at rest against the whole window
/// while held — the surface must not change when the characters appear — with
/// <see cref="RevealedValue.Rendered"/> as the proof that they really did.
/// </para>
/// </remarks>
public sealed class HistoryRevealAutomationTests
{
    private const string _master = "correct horse battery staple";
    private const string _superseded = "SENTINEL-OLD-PASSWORD-4b8e21";
    private const string _current = "SENTINEL-NEW-PASSWORD-1f7c93";

    [Fact]
    public Task The_revision_password_is_dots_until_it_is_held() => HeadlessSession.On(() =>
    {
        using var screen = new HistoryScreen();

        Assert.Equal(new string('•', _superseded.Length), screen.Cell.Rendered);

        screen.Hold();
        Assert.Equal(_superseded, screen.Cell.Rendered);

        screen.Release();
        Assert.Equal(new string('•', _superseded.Length), screen.Cell.Rendered);
        Assert.Empty(screen.History.RevealedWhen);
    });

    /// <summary>
    /// The surface is the same whether or not the characters are on screen.
    /// </summary>
    /// <remarks>
    /// The anti-vacuity half is the load-bearing one here, and it is three claims: the cell really
    /// was showing the password when the second sweep ran, the sweeps found something, and they
    /// found <em>this</em> screen — the entry it is looking at and the time of the revision.
    /// </remarks>
    [Fact]
    public Task Revealing_a_revision_changes_nothing_the_accessibility_bus_can_read() =>
        HeadlessSession.On(() =>
        {
            using var screen = new HistoryScreen();

            var atRest = AutomationSurface.Of(screen.Window);

            screen.Hold();
            var whileHeld = AutomationSurface.Of(screen.Window);

            Assert.Equal(_superseded, screen.Cell.Rendered);
            Assert.NotEmpty(whileHeld);
            Assert.Equal(atRest, whileHeld);

            var texts = whileHeld.Select(entry => entry.Text).ToList();
            Assert.Contains(texts, text => text.Contains("github", StringComparison.Ordinal));
            Assert.Contains(texts, text => text.Contains(screen.Revision.When, StringComparison.Ordinal));
        });

    [Fact]
    public Task Nothing_on_the_screen_exposes_a_revealed_revision() => HeadlessSession.On(() =>
    {
        using var screen = new HistoryScreen();

        screen.Hold();

        Assert.Equal(_superseded, screen.Cell.Rendered);
        AutomationSurface.AssertNothingExposes(screen.Window, _superseded);
        AutomationSurface.AssertNothingExposes(screen.Window, _current);
    });

    /// <summary>
    /// The cell names the revision it belongs to, and never what the revision held.
    /// </summary>
    [Fact]
    public Task The_cell_publishes_a_time_rather_than_a_value() => HeadlessSession.On(() =>
    {
        using var screen = new HistoryScreen();

        screen.Hold();

        Assert.IsType<NoneAutomationPeer>(ControlAutomationPeer.CreatePeerForElement(screen.Cell));

        var name = AutomationProperties.GetName(screen.Cell);
        Assert.Contains(screen.Revision.When, name, StringComparison.Ordinal);
        Assert.DoesNotContain(_superseded, name, StringComparison.Ordinal);
    });

    /// <summary>
    /// Leaving the screen while holding takes the value with it.
    /// </summary>
    /// <remarks>
    /// <c>ShellViewModel</c> replaces its content rather than hiding it, so this is what navigating
    /// away mid-hold does to the pane.
    /// </remarks>
    [Fact]
    public Task Switching_screens_while_held_takes_the_revision_off_the_screen() =>
        HeadlessSession.On(() =>
        {
            using var screen = new HistoryScreen();

            screen.Hold();
            var cell = screen.Cell;
            Assert.True(cell.IsRevealed);

            screen.Window.Content = null;
            Drain();

            Assert.False(cell.IsRevealed);
            Assert.Empty(screen.History.RevealedWhen);
            AutomationSurface.AssertNothingExposes(screen.Window, _superseded);
        });

    /// <summary>
    /// Locking takes it off the screen, and the pane with it.
    /// </summary>
    /// <remarks>
    /// Driven the way the app drives it: the session locks, the screen reloads, and the pane it was
    /// holding is disposed. Nothing here calls <c>EndReveal</c> on the control.
    /// </remarks>
    [Fact]
    public Task Locking_takes_the_revision_off_the_screen() => HeadlessSession.On(() =>
    {
        using var screen = new HistoryScreen();
        var history = screen.History;

        screen.Hold();
        var cell = screen.Cell;
        Assert.True(cell.IsRevealed);

        screen.Session.Lock(VaultLockReason.Manual);
        screen.Model.Reload();
        Drain();

        Assert.Null(screen.Model.Detail);
        Assert.False(cell.IsRevealed);
        Assert.Empty(history.RevealedWhen);
        Assert.Empty(history.Rows);
        AutomationSurface.AssertNothingExposes(screen.Window, _superseded);
    });

    private static void Drain() => Dispatcher.UIThread.RunJobs();

    /// <summary>
    /// The Entries screen, looking at an entry whose password has been replaced once, with its
    /// history open and the one revision selected.
    /// </summary>
    private sealed class HistoryScreen : IDisposable
    {
        private readonly string _directory;

        internal HistoryScreen()
        {
            _directory = Directory.CreateTempSubdirectory("keypaste-history-reveal-").FullName;
            var path = Path.Combine(_directory, "vault.kdbx");

            using (var vault = Vault.Create(path, _master))
            {
                vault.AddEntry(new VaultEntry { Title = "github", Username = "me", Password = _superseded });
                vault.UpdateEntry(new VaultEntry { Title = "github", Username = "me", Password = _current });
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
            History.ToggleCommand.Execute(null);
            Drain();
            History.Selected = History.Rows[0];
            Drain();
        }

        internal AppVaultSession Session { get; }

        internal ClipboardCountdown Countdown { get; }

        internal EntriesViewModel Model { get; }

        internal Window Window { get; }

        internal EntryHistoryViewModel History => Model.Detail!.History;

        internal EntryRevisionRow Revision { get; private set; } = null!;

        /// <summary>The cell the comparison draws the revision's password in.</summary>
        internal RevealedValue Cell =>
            Window.GetVisualDescendants().OfType<RevealedValue>().Single(cell => cell.Name == "RevisionPassword");

        /// <summary>
        /// Holds the cell the comparison built, and remembers which revision it belongs to.
        /// </summary>
        /// <remarks>
        /// The hold is begun on the control rather than by a pointer press on the window: this
        /// session draws no frames, and Avalonia resolves a press to a control through the
        /// renderer, so a press lands on the window and reaches nothing inside it. What that costs
        /// is the gesture, which <see cref="RevealedValueTests"/> covers on the same control; what
        /// it keeps is everything the screen contributes — the cell the template built, the row its
        /// <c>Source</c> binding found, and the mask its <c>MaskedLength</c> binding sized.
        /// </remarks>
        internal void Hold()
        {
            Revision = History.Selected!;
            Cell.BeginReveal();
            Drain();
        }

        internal void Release()
        {
            Cell.EndReveal();
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
