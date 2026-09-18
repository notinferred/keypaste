using Avalonia.Controls;
using Avalonia.Headless;
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
/// The four fields 4.9 adds, asked the same questions the master-password fields are asked, and
/// asked what the paste gesture actually does.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not lean on <see cref="MaskedInputAutomationTests"/>.</b> These are the same control, and
/// that is exactly why they need their own evidence: a field inherits the class and its template,
/// not the surface it ends up with. Three of these four live inside <c>DataTemplate</c>s, beside
/// labels, notes and buttons that the unlock screen does not have, and the sweep runs over the
/// whole screen.
/// </para>
/// <para>
/// <b>The gesture is driven through the window rather than through
/// <see cref="ISecretSink.Paste"/>.</b> Calling the view model directly would prove nothing about
/// the binding, the modifier or <see cref="MaskedInput.AllowsPaste"/> — and it is those three that
/// decide whether the unlock field can be pasted into, which SECURITY.md makes a claim about.
/// </para>
/// </remarks>
public sealed class SecretFieldAutomationTests
{
    private const string _master = "correct horse battery staple";

    // Same length, no character in common: two secrets whose automation surfaces must be equal.
    private const string _alpha = "aaaaaaaaaaaaaaaa";
    private const string _beta = "bbbbbbbbbbbbbbbb";

    private const string _fixture = "SENTINEL-ENTRY-SECRET-9c41ba";

    [Fact]
    public Task The_new_entry_password_surface_depends_on_the_length_and_not_the_characters() =>
        HeadlessSession.On(() =>
        {
            using var screen = new EntriesScreen();
            screen.BeginAdd();

            Assert.Equal(_alpha.Length, Differential(screen.Window, screen.NewEntryPassword).Length);
        });

    [Fact]
    public Task The_replacement_password_surface_depends_on_the_length_and_not_the_characters() =>
        HeadlessSession.On(() =>
        {
            using var screen = new EntriesScreen();
            screen.BeginEdit();

            Assert.Equal(_alpha.Length, Differential(screen.Window, screen.ReplacementPassword).Length);
        });

    [Fact]
    public Task The_new_variable_value_surface_depends_on_the_length_and_not_the_characters() =>
        HeadlessSession.On(() =>
        {
            using var screen = new EnvScreen();
            screen.BeginAdd();

            Assert.Equal(_alpha.Length, Differential(screen.Window, screen.NewEnvValue).Length);
        });

    [Fact]
    public Task The_replacement_variable_value_surface_depends_on_the_length_and_not_the_characters() =>
        HeadlessSession.On(() =>
        {
            using var screen = new EnvScreen();
            screen.BeginReplace();

            Assert.Equal(_alpha.Length, Differential(screen.Window, screen.ReplacementEnvValue).Length);
        });

    /// <summary>
    /// The anti-vacuity guard for the four differentials: the mask reaches the tree, so each of
    /// them is comparing something rather than two empty lists.
    /// </summary>
    [Fact]
    public Task The_mask_reaches_the_automation_tree_for_every_new_field() => HeadlessSession.On(() =>
    {
        using var entries = new EntriesScreen();
        entries.BeginAdd();
        Type(entries.Window, entries.NewEntryPassword, _fixture);
        AssertShowsTheMask(entries.NewEntryPassword);

        entries.BeginEdit();
        Type(entries.Window, entries.ReplacementPassword, _fixture);
        AssertShowsTheMask(entries.ReplacementPassword);

        using var env = new EnvScreen();
        env.BeginAdd();
        Type(env.Window, env.NewEnvValue, _fixture);
        AssertShowsTheMask(env.NewEnvValue);

        env.BeginReplace();
        Type(env.Window, env.ReplacementEnvValue, _fixture);
        AssertShowsTheMask(env.ReplacementEnvValue);
    });

    /// <summary>
    /// Nothing anywhere on either screen carries what was typed, not only the field itself. A
    /// note, a label or a button that grew a binding to the value would be caught here.
    /// </summary>
    [Fact]
    public Task Nothing_on_either_screen_exposes_what_was_typed() => HeadlessSession.On(() =>
    {
        using var entries = new EntriesScreen();
        entries.BeginAdd();
        Type(entries.Window, entries.NewEntryPassword, _fixture);
        AutomationSurface.AssertNothingExposes(entries.Window, _fixture);

        entries.BeginEdit();
        Type(entries.Window, entries.ReplacementPassword, _fixture);
        AutomationSurface.AssertNothingExposes(entries.Window, _fixture);

        using var env = new EnvScreen();
        env.BeginAdd();
        Type(env.Window, env.NewEnvValue, _fixture);
        AutomationSurface.AssertNothingExposes(env.Window, _fixture);

        env.BeginReplace();
        Type(env.Window, env.ReplacementEnvValue, _fixture);
        AutomationSurface.AssertNothingExposes(env.Window, _fixture);
    });

    /// <summary>
    /// The gesture itself: the platform's paste keystroke fills the buffer behind the field, and
    /// nothing of what arrived is on the screen afterwards.
    /// </summary>
    [Fact]
    public Task The_paste_gesture_fills_the_field_it_was_pressed_in() => HeadlessSession.On(async () =>
    {
        using var screen = new EntriesScreen();
        screen.Clipboard.Plant(_fixture);
        screen.BeginAdd();

        await Paste(screen.Window, screen.NewEntryPassword);

        Assert.Equal(1, screen.Clipboard.ReadCount);
        Assert.Equal(_fixture.Length, screen.Model.NewPassword.MaskedLength);
        AutomationSurface.AssertNothingExposes(screen.Window, _fixture);
    });

    [Fact]
    public Task The_paste_gesture_fills_the_replacement_value_field() => HeadlessSession.On(async () =>
    {
        using var screen = new EnvScreen();
        screen.Clipboard.Plant(_fixture);
        screen.BeginReplace();

        await Paste(screen.Window, screen.ReplacementEnvValue);

        Assert.Equal(1, screen.Clipboard.ReadCount);
        Assert.Equal(_fixture.Length, screen.Project.ReplacementValue.MaskedLength);
    });

    /// <summary>
    /// The scope of the whole feature, asserted rather than described: the master-password field
    /// has no sink to paste into and does not answer the gesture.
    /// </summary>
    /// <remarks>
    /// The field is typed into afterwards and the length required to rise. Without that, a field
    /// that had stopped receiving keyboard input at all would pass this test, and the claim would
    /// be about a dead control rather than a refused gesture.
    /// </remarks>
    [Fact]
    public Task The_unlock_field_ignores_the_paste_gesture() => HeadlessSession.On(async () =>
    {
        using var screen = new UnlockScreen();

        screen.Password.Focus();
        screen.Window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Control);
        screen.Window.KeyReleaseQwerty(PhysicalKey.V, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        await screen.Password.PasteCompleted;

        Assert.False(screen.Password.AllowsPaste);
        Assert.Null(screen.Password.Sink);
        Assert.Equal(0, screen.Model.MaskedLength);

        screen.Window.KeyTextInput(_alpha);
        Assert.Equal(_alpha.Length, screen.Model.MaskedLength);
    });

    /// <summary>
    /// The differential itself: one secret, cleared, then another, with the two surfaces required
    /// to match.
    /// </summary>
    /// <returns>What was typed the second time, so a caller can assert the field was not empty.</returns>
    private static string Differential(Window window, MaskedInput field)
    {
        Type(window, field, _alpha);
        var first = AutomationSurface.Of(field);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.Equal(0, field.MaskedLength);

        window.KeyTextInput(_beta);
        var second = AutomationSurface.Of(field);

        Assert.Equal(first, second);
        return _beta;
    }

    private static void AssertShowsTheMask(MaskedInput field)
    {
        var surface = AutomationSurface.Of(field).Select(entry => entry.Text).ToList();

        Assert.Contains(new string('•', _fixture.Length), surface, StringComparer.Ordinal);
    }

    private static void Type(Window window, MaskedInput field, string text)
    {
        field.Focus();
        window.KeyTextInput(text);
    }

    private static async Task Paste(Window window, MaskedInput field)
    {
        field.Focus();
        window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.V, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        await field.PasteCompleted;
    }

    /// <summary>A vault with an entry and a project, and a session holding it open.</summary>
    private abstract class Screen : IDisposable
    {
        private readonly string _directory;

        protected Screen()
        {
            _directory = Directory.CreateTempSubdirectory("keypaste-secret-field-").FullName;
            var path = Path.Combine(_directory, "vault.kdbx");

            using (var vault = Vault.Create(path, _master))
            {
                vault.AddEntry(new VaultEntry { Title = "github", Username = "me", Password = "gh" });
                vault.AddEntry(new VaultEntry { Title = "STRIPE_KEY", Password = "sk", GroupPath = "env/billing" });
                vault.Save();
            }

            Session = new AppVaultSession(new ManualClock());

            using (var master = TempVault.Secret(_master))
            {
                Assert.Equal(UnlockOutcome.Opened, Session.TryUnlock(path, master.Value));
            }

            Clipboard = new FakeClipboard();
            Countdown = new ClipboardCountdown(Clipboard, new ManualClock());
        }

        internal AppVaultSession Session { get; }

        internal ClipboardCountdown Countdown { get; }

        internal FakeClipboard Clipboard { get; }

        internal Window Window { get; private set; } = null!;

        public virtual void Dispose()
        {
            Countdown.Dispose();
            Session.Dispose();

            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        /// <summary>Shows a view over this session's vault and lets the dispatcher settle.</summary>
        protected void Show(Control content)
        {
            Window = new Window { Content = content };
            Window.Show();
            Drain();
        }

        /// <summary>
        /// Runs the posted work a template materialization leaves behind, so a field that has just
        /// become visible is in the tree by the time it is looked for.
        /// </summary>
        protected static void Drain() => Dispatcher.UIThread.RunJobs();

        protected MaskedInput Field(string name) =>
            Window.GetVisualDescendants().OfType<MaskedInput>().Single(input => input.Name == name);
    }

    private sealed class EntriesScreen : Screen
    {
        internal EntriesScreen()
        {
            Model = new EntriesViewModel(Session, Countdown);
            Show(new EntriesView { DataContext = Model });
        }

        internal EntriesViewModel Model { get; }

        internal MaskedInput NewEntryPassword => Field("NewEntryPassword");

        internal MaskedInput ReplacementPassword => Field("ReplacementPassword");

        /// <summary>Opens the add form with generation off, which is what shows the field.</summary>
        internal void BeginAdd()
        {
            Model.BeginAddCommand.Execute(null);
            Model.GeneratePassword = false;
            Drain();
        }

        /// <summary>Selects an entry and opens its edit form.</summary>
        internal void BeginEdit()
        {
            Model.Selected = Model.Rows.Single(row => row.Title == "github");
            Drain();
            Model.Detail!.EditCommand.Execute(null);
            Drain();
        }

        public override void Dispose()
        {
            Model.Dispose();
            base.Dispose();
        }
    }

    private sealed class EnvScreen : Screen
    {
        internal EnvScreen()
        {
            Model = new EnvSetsViewModel(Session, Countdown);
            Show(new EnvSetsView { DataContext = Model });
            Model.OpenCommand.Execute("billing");
            Drain();
        }

        internal EnvSetsViewModel Model { get; }

        internal EnvProjectViewModel Project => Model.OpenProject!;

        internal MaskedInput NewEnvValue => Field("NewEnvValue");

        internal MaskedInput ReplacementEnvValue => Field("ReplacementEnvValue");

        internal void BeginAdd()
        {
            Project.BeginAddCommand.Execute(null);
            Project.GenerateValue = false;
            Drain();
        }

        internal void BeginReplace()
        {
            Project.BeginReplace(Project.Variables.Single(row => row.Key == "STRIPE_KEY"));
            Drain();
        }

        public override void Dispose()
        {
            Model.Dispose();
            base.Dispose();
        }
    }

    /// <summary>
    /// The unlock screen, with a vault remembered so its password field is live.
    /// </summary>
    /// <remarks>
    /// Built on a locked session rather than on <see cref="Screen"/>: the field only accepts a
    /// keystroke when there is a vault to unlock, and a screen whose session was already open would
    /// make the refusal below true for the wrong reason.
    /// </remarks>
    private sealed class UnlockScreen : IDisposable
    {
        private readonly TempVault _fixture = new();
        private readonly AppVaultSession _session;

        internal UnlockScreen()
        {
            _fixture.RememberSelf();
            _session = new AppVaultSession(new ManualClock());
            Model = new UnlockViewModel(_session, _fixture.Home, new FakeVaultFilePicker(), () => { });

            Window = new Window { Content = new UnlockView { DataContext = Model } };
            Window.Show();

            // Focus is posted at Background priority, so let the dispatcher drain before typing.
            Dispatcher.UIThread.RunJobs();
        }

        internal Window Window { get; }

        internal UnlockViewModel Model { get; }

        internal MaskedInput Password =>
            Window.GetVisualDescendants().OfType<MaskedInput>().Single(input => input.Name == "Password");

        public void Dispose()
        {
            Model.Dispose();
            _session.Dispose();
            _fixture.Dispose();
        }
    }
}
