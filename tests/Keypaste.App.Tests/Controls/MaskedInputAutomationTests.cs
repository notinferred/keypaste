using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Xunit;
using Xunit.Sdk;

namespace Keypaste.App.Tests.Controls;

/// <summary>
/// The master-password field, asked what it publishes to the accessibility bus while it holds one.
/// </summary>
/// <remarks>
/// <para>
/// THREATS.md T-22 said in as many words that this test did not exist: <c>MaskedInput</c> was the
/// mitigation and <c>OnCreateAutomationPeer</c> was the only evidence for it. An implementation
/// detail is not evidence — <c>TextBox</c> would have published the password one keystroke at a
/// time through <c>TextBoxAutomationPeer.Value</c>, and the thing that stops it here is a choice
/// somebody could undo in one line.
/// </para>
/// <para>
/// <b><see cref="RevealedValueTests"/> could not stand in for this.</b> It covers a different
/// control, with a different peer type, holding a value handed to it rather than one arriving from
/// the keyboard. It is green today with a `MaskedInput` that published every character.
/// </para>
/// <para>
/// <b>The check is a whole-surface sweep, and it is proved by the tests that expect it to fail.</b>
/// Asserting the peer's type would pass for any peer that has not started leaking yet.
/// <see cref="Attaching_the_password_to_an_automation_property_fails_the_check"/>,
/// <see cref="A_value_exposing_control_in_its_place_fails_the_check"/> and
/// <see cref="A_text_block_drawing_the_password_fails_the_check"/> run the same
/// <c>AssertNothingExposes</c> the positive tests run and require it to raise — so it cannot be
/// weakened without turning them red.
/// </para>
/// </remarks>
public sealed class MaskedInputAutomationTests
{
    internal const string Fixture = "SENTINEL-MASTER-PASSWORD-9c41ba";

    // Same length, no character in common: two passwords whose automation surfaces must be equal.
    internal const string Alpha = "aaaaaaaaaaaaaaaa";
    internal const string Beta = "bbbbbbbbbbbbbbbb";

    // Appears in no label, no class name and no mask this app draws, so a single leaked keystroke
    // has nowhere to hide behind a legitimate string.
    internal const char Rare = 'Ж';

    [Fact]
    public Task The_peer_is_a_plain_control_peer_with_no_value_pattern() => HeadlessSession.On(() =>
    {
        var input = new MaskedInput();

        var peer = ControlAutomationPeer.CreatePeerForElement(input);

        Assert.IsAssignableFrom<ControlAutomationPeer>(peer);
        Assert.Null(peer.GetProvider<IValueProvider>());
    });

    [Fact]
    public Task No_automation_surface_carries_a_typed_password() => HeadlessSession.On(() =>
    {
        using var screen = new UnlockScreen();

        foreach (var c in Fixture)
        {
            screen.Window.KeyTextInput(c.ToString());
        }

        Assert.Equal(Fixture.Length, screen.Model.MaskedLength);
        AutomationSurface.AssertNothingExposes(screen.Password, Fixture);
    });

    /// <summary>
    /// A paste arrives as the whole password in one <c>TextInputEventArgs.Text</c>, which is the
    /// shape SECURITY.md records as the honest limit of this control. It must not become a longer
    /// string on the automation bus either.
    /// </summary>
    [Fact]
    public Task No_automation_surface_carries_a_pasted_password() => HeadlessSession.On(() =>
    {
        using var screen = new UnlockScreen();

        screen.Window.KeyTextInput(Fixture);

        Assert.Equal(Fixture.Length, screen.Model.MaskedLength);
        AutomationSurface.AssertNothingExposes(screen.Password, Fixture);
    });

    /// <summary>
    /// A whole-password search cannot see a per-keystroke leak, which is the one
    /// <c>TextBoxAutomationPeer</c> would actually produce.
    /// </summary>
    [Fact]
    public Task No_automation_surface_carries_a_single_keystroke() => HeadlessSession.On(() =>
    {
        using var screen = new UnlockScreen();

        screen.Window.KeyTextInput(Rare.ToString());

        Assert.Equal(1, screen.Model.MaskedLength);
        AutomationSurface.AssertNothingExposes(screen.Password, Rare.ToString());
    });

    /// <summary>
    /// The strongest one: two passwords of equal length sharing no character produce an identical
    /// automation surface if and only if nothing on it is a function of the characters.
    /// </summary>
    [Fact]
    public Task The_automation_surface_depends_on_the_length_and_not_the_characters() =>
        HeadlessSession.On(() =>
        {
            using var screen = new UnlockScreen();

            AutomationSurface.AssertNamedByPurpose(screen.Password);
            screen.Window.KeyTextInput(Alpha);
            var first = AutomationSurface.Of(screen.Password);
            AutomationSurface.AssertNamedByPurpose(screen.Password);

            screen.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            screen.Window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.Equal(0, screen.Model.MaskedLength);

            screen.Window.KeyTextInput(Beta);
            var second = AutomationSurface.Of(screen.Password);
            AutomationSurface.AssertNamedByPurpose(screen.Password);

            Assert.Equal(first, second);
        });

    /// <summary>
    /// The same differential, on the field a new vault's master password is typed into (4.8).
    /// </summary>
    /// <remarks>
    /// A second and third <see cref="MaskedInput"/> inherit the control and its template, not its
    /// evidence. D-0099 is a claim about every field a master password reaches, and the create form
    /// is where one is chosen for the first time.
    /// </remarks>
    [Fact]
    public Task The_new_password_surface_depends_on_the_length_and_not_the_characters() =>
        HeadlessSession.On(async () =>
        {
            using var screen = new UnlockScreen();
            await screen.BeginCreate();
            screen.NewPassword.Focus();

            AutomationSurface.AssertNamedByPurpose(screen.NewPassword);
            screen.Window.KeyTextInput(Alpha);
            var first = AutomationSurface.Of(screen.NewPassword);
            AutomationSurface.AssertNamedByPurpose(screen.NewPassword);

            screen.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            screen.Window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.Equal(0, screen.Model.NewMaskedLength);

            screen.Window.KeyTextInput(Beta);
            var second = AutomationSurface.Of(screen.NewPassword);
            AutomationSurface.AssertNamedByPurpose(screen.NewPassword);

            Assert.Equal(first, second);
        });

    /// <summary>The same differential again, on the confirmation field (4.8).</summary>
    [Fact]
    public Task The_confirmation_surface_depends_on_the_length_and_not_the_characters() =>
        HeadlessSession.On(async () =>
        {
            using var screen = new UnlockScreen();
            await screen.BeginCreate();
            screen.ConfirmPassword.Focus();

            AutomationSurface.AssertNamedByPurpose(screen.ConfirmPassword);
            screen.Window.KeyTextInput(Alpha);
            var first = AutomationSurface.Of(screen.ConfirmPassword);
            AutomationSurface.AssertNamedByPurpose(screen.ConfirmPassword);

            screen.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            screen.Window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.Equal(0, screen.Model.ConfirmMaskedLength);

            screen.Window.KeyTextInput(Beta);
            var second = AutomationSurface.Of(screen.ConfirmPassword);
            AutomationSurface.AssertNamedByPurpose(screen.ConfirmPassword);

            Assert.Equal(first, second);
        });

    /// <summary>
    /// Nothing anywhere on the create form carries either password.
    /// </summary>
    /// <remarks>
    /// Both fields hold a sentinel at once, so a leak that crossed from one field to the other — a
    /// shared styled property, a peer that reported its sibling — is caught as well as a leak from
    /// either on its own.
    /// </remarks>
    [Fact]
    public Task Nothing_on_the_create_form_exposes_either_password() => HeadlessSession.On(async () =>
    {
        using var screen = new UnlockScreen();
        await screen.BeginCreate();

        screen.NewPassword.Focus();
        screen.Window.KeyTextInput(Fixture);

        screen.ConfirmPassword.Focus();
        screen.Window.KeyTextInput(Rare.ToString() + Fixture);

        AutomationSurface.AssertNothingExposes(screen.Window, Fixture);
        AutomationSurface.AssertNothingExposes(screen.Window, Rare.ToString());
    });

    /// <summary>
    /// The anti-vacuity guard for the two create fields.
    /// </summary>
    /// <remarks>
    /// Their placeholders differ from the unlock field's, so a sweep that silently stopped reaching
    /// them would leave the two differentials above passing on an empty surface.
    /// </remarks>
    [Fact]
    public Task The_mask_reaches_the_automation_tree_for_both_create_fields() =>
        HeadlessSession.On(async () =>
        {
            using var screen = new UnlockScreen();
            await screen.BeginCreate();

            screen.NewPassword.Focus();
            screen.Window.KeyTextInput(Fixture);

            var created = AutomationSurface.Of(screen.NewPassword).Select(entry => entry.Text).ToList();
            Assert.Contains(new string('•', Fixture.Length), created, StringComparer.Ordinal);
            Assert.Contains("New master password", created, StringComparer.Ordinal);

            screen.ConfirmPassword.Focus();
            screen.Window.KeyTextInput(Fixture);

            var confirmed = AutomationSurface.Of(screen.ConfirmPassword).Select(entry => entry.Text).ToList();
            Assert.Contains(new string('•', Fixture.Length), confirmed, StringComparer.Ordinal);
            Assert.Contains("Confirm master password", confirmed, StringComparer.Ordinal);
        });

    /// <summary>
    /// The same differential, on the field a backup's master password is typed into (V.4b).
    /// </summary>
    /// <remarks>
    /// The fourth master-password field, and the one most likely to hold a password somebody no
    /// longer uses anywhere else. It is typed on the locked screen, so D-0099 applies to it exactly
    /// as it does to the first.
    /// </remarks>
    [Fact]
    public Task The_backup_password_surface_depends_on_the_length_and_not_the_characters() =>
        HeadlessSession.On(() =>
        {
            using var screen = new UnlockScreen();
            screen.BeginRestore();
            screen.BackupPassword.Focus();

            AutomationSurface.AssertNamedByPurpose(screen.BackupPassword);
            screen.Window.KeyTextInput(Alpha);
            var first = AutomationSurface.Of(screen.BackupPassword);
            AutomationSurface.AssertNamedByPurpose(screen.BackupPassword);

            screen.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            screen.Window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.Equal(0, screen.Model.Restore!.MaskedLength);

            screen.Window.KeyTextInput(Beta);
            var second = AutomationSurface.Of(screen.BackupPassword);
            AutomationSurface.AssertNamedByPurpose(screen.BackupPassword);

            Assert.Equal(first, second);
        });

    /// <summary>Nothing anywhere on the restore panel carries the backup's password, and the mask does reach it.</summary>
    [Fact]
    public Task Nothing_on_the_restore_panel_exposes_the_backup_password() => HeadlessSession.On(() =>
    {
        using var screen = new UnlockScreen();
        screen.BeginRestore();
        screen.BackupPassword.Focus();

        screen.Window.KeyTextInput(Rare.ToString() + Fixture);

        Assert.Equal(Fixture.Length + 1, screen.Model.Restore!.MaskedLength);
        AutomationSurface.AssertNothingExposes(screen.Window, Fixture);
        AutomationSurface.AssertNothingExposes(screen.Window, Rare.ToString());

        // The anti-vacuity half: its placeholder differs from the other three, so a sweep that
        // stopped reaching this field would leave the absences above passing on nothing.
        var surface = AutomationSurface.Of(screen.BackupPassword).Select(entry => entry.Text).ToList();
        Assert.Contains(new string('•', Fixture.Length + 1), surface, StringComparer.Ordinal);
        Assert.Contains("That backup's master password", surface, StringComparer.Ordinal);
    });

    /// <summary>A master password is typed, never pasted: the restore field ignores the gesture as the unlock field does.</summary>
    [Fact]
    public Task The_backup_password_field_takes_no_paste() => HeadlessSession.On(() =>
    {
        using var screen = new UnlockScreen();
        screen.BeginRestore();

        Assert.False(screen.BackupPassword.AllowsPaste);
        Assert.Null(screen.BackupPassword.Sink);
    });

    /// <summary>
    /// The guard against every absence above being vacuous. If the sweep were reaching nothing,
    /// each of those tests would pass for the worst possible reason.
    /// </summary>
    [Fact]
    public Task The_mask_is_what_reaches_the_automation_tree() => HeadlessSession.On(() =>
    {
        using var screen = new UnlockScreen();

        screen.Window.KeyTextInput(Fixture);

        var surface = AutomationSurface.Of(screen.Password).Select(entry => entry.Text).ToList();

        Assert.Contains(new string('•', Fixture.Length), surface, StringComparer.Ordinal);
        Assert.Contains("Master password", surface, StringComparer.Ordinal);
    });

    /// <summary>
    /// <c>AutomationProperties.Name="{Binding …}"</c> compiles, renders identically and publishes
    /// whatever it is bound to. Nothing on this screen sets one, and this is what keeps that true.
    /// </summary>
    [Fact]
    public Task No_automation_metadata_is_attached_on_the_unlock_screen() => HeadlessSession.On(() =>
    {
        using var screen = new UnlockScreen();

        screen.Window.KeyTextInput(Fixture);

        foreach (var element in screen.Window.GetVisualDescendants().OfType<Control>())
        {
            Assert.Null(AutomationProperties.GetName(element));
            Assert.Null(AutomationProperties.GetHelpText(element));
            Assert.Null(AutomationProperties.GetItemStatus(element));
            Assert.Null(AutomationProperties.GetItemType(element));
            Assert.Null(AutomationProperties.GetAutomationId(element));
        }
    });

    [Fact]
    public Task The_password_is_in_no_styled_property() => HeadlessSession.On(() =>
    {
        using var screen = new UnlockScreen();

        screen.Window.KeyTextInput(Fixture);

        foreach (var property in AvaloniaPropertyRegistry.Instance.GetRegistered(screen.Password))
        {
            Assert.DoesNotContain(
                Fixture,
                screen.Password.GetValue(property)?.ToString() ?? string.Empty,
                StringComparison.Ordinal);
        }

        Assert.Equal(new string('•', Fixture.Length), screen.Password.Display);
    });

    /// <summary>
    /// A leak that moved off the field — into the message line, a tooltip, a future label — is
    /// still a leak, so the sweep is run over the whole screen and not only over the control.
    /// </summary>
    [Fact]
    public Task Nothing_on_the_unlock_screen_exposes_the_password() => HeadlessSession.On(() =>
    {
        using var screen = new UnlockScreen();

        screen.Window.KeyTextInput(Fixture);

        AutomationSurface.AssertNothingExposes(screen.Window, Fixture);
    });

    [Theory]
    [InlineData("Name")]
    [InlineData("HelpText")]
    [InlineData("ItemStatus")]
    [InlineData("ItemType")]
    [InlineData("AutomationId")]
    public Task Attaching_the_password_to_an_automation_property_fails_the_check(string property) =>
        HeadlessSession.On(() =>
        {
            using var screen = new UnlockScreen();

            screen.Window.KeyTextInput(Fixture);
            Attach(screen.Password, property, Fixture);

            var failure = Assert.ThrowsAny<XunitException>(
                () => AutomationSurface.AssertNothingExposes(screen.Password, Fixture));

            // The peer reads the attached property, so the sweep reaches it as GetName before it
            // reaches AutomationProperties.Name. Either is the right answer; naming neither is not.
            Assert.Contains(property, failure.Message, StringComparison.Ordinal);
        });

    /// <summary>
    /// The control this one exists instead of. <c>PasswordChar</c> hides the characters on screen
    /// and changes nothing about what the accessibility bus is told, which is T-22's opening
    /// sentence and is asserted here rather than quoted.
    /// </summary>
    [Fact]
    public Task A_value_exposing_control_in_its_place_fails_the_check() => HeadlessSession.On(() =>
    {
        var box = new TextBox { Text = Fixture, PasswordChar = '•' };

        var value = ControlAutomationPeer.CreatePeerForElement(box).GetProvider<IValueProvider>();
        Assert.Equal(Fixture, value?.Value);

        var failure = Assert.ThrowsAny<XunitException>(() => AutomationSurface.AssertNothingExposes(box, Fixture));

        Assert.Contains("IValueProvider.Value", failure.Message, StringComparison.Ordinal);
    });

    /// <summary>
    /// The half a value pattern does not cover: a control that draws the secret publishes it as
    /// the automation <i>name</i>. T-22's amendment, on the master-password side.
    /// </summary>
    [Fact]
    public Task A_text_block_drawing_the_password_fails_the_check() => HeadlessSession.On(() =>
    {
        var block = new TextBlock { Text = Fixture };

        var failure = Assert.ThrowsAny<XunitException>(() => AutomationSurface.AssertNothingExposes(block, Fixture));

        Assert.Contains("GetName", failure.Message, StringComparison.Ordinal);
    });

    [Fact]
    public Task A_control_that_kept_the_characters_in_a_styled_property_fails_the_check() =>
        HeadlessSession.On(() =>
        {
            var control = new Remembering { Kept = Fixture };

            var failure = Assert.ThrowsAny<XunitException>(
                () => AutomationSurface.AssertNothingExposes(control, Fixture));

            Assert.Contains("styled property", failure.Message, StringComparison.Ordinal);
        });

    private static void Attach(Control control, string property, string value)
    {
        switch (property)
        {
            case "Name": AutomationProperties.SetName(control, value); break;
            case "HelpText": AutomationProperties.SetHelpText(control, value); break;
            case "ItemStatus": AutomationProperties.SetItemStatus(control, value); break;
            case "ItemType": AutomationProperties.SetItemType(control, value); break;
            case "AutomationId": AutomationProperties.SetAutomationId(control, value); break;
            default: throw new ArgumentOutOfRangeException(nameof(property), property, "no such automation property");
        }
    }

    /// <summary>A control that keeps what it was given, which is what this one must never do.</summary>
    private sealed class Remembering : Control
    {
        internal static readonly StyledProperty<string> KeptProperty =
            AvaloniaProperty.Register<Remembering, string>(nameof(Kept), string.Empty);

        internal string Kept
        {
            get => GetValue(KeptProperty);
            set => SetValue(KeptProperty, value);
        }
    }

    /// <summary>The unlock screen, built the way launch builds it, with a vault to unlock.</summary>
    private sealed class UnlockScreen : IDisposable
    {
        private readonly TempVault _fixture = new();
        private readonly AppVaultSession _session;

        internal UnlockScreen()
        {
            _fixture.RememberSelf();
            _session = new AppVaultSession(new ManualClock());
            Picker = new FakeVaultFilePicker();
            Model = new UnlockViewModel(_session, _fixture.Home, Picker, () => { });

            Window = new Window { Content = new UnlockView { DataContext = Model } };
            Window.Show();

            // Focus is posted at Background priority, so let the dispatcher drain before typing.
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        internal Window Window { get; }

        internal UnlockViewModel Model { get; }

        internal FakeVaultFilePicker Picker { get; }

        internal MaskedInput Password => Field("Password");

        /// <summary>The new-vault master password, shown when the create form is open.</summary>
        internal MaskedInput NewPassword => Field("NewPassword");

        /// <summary>Its confirmation.</summary>
        internal MaskedInput ConfirmPassword => Field("ConfirmPassword");

        /// <summary>The password of a backup being restored, shown when the restore panel is open.</summary>
        internal MaskedInput BackupPassword => Field("BackupPassword");

        /// <summary>Saves once so the vault has a backup, then opens the restore panel on it.</summary>
        internal void BeginRestore()
        {
            using (var vault = Keypaste.Core.Vault.Open(_fixture.Path_, TempVault.Password))
            {
                vault.AddEntry(new Keypaste.Core.VaultEntry { Title = "saved-again", Password = "p" });
                vault.Save();
            }

            // The selection was read before the backup existed, so choose the vault again.
            Assert.True(Model.Offer(_fixture.Path_));
            Model.SelectedPath = null;
            Assert.True(Model.Offer(_fixture.Path_));

            Model.StartRestoreCommand.Execute(null);
            Assert.NotNull(Model.Restore);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        /// <summary>Opens the create form, so its two fields can be typed into.</summary>
        internal async Task BeginCreate()
        {
            Picker.NewPath = Path.Combine(_fixture.Home, "created.kdbx");
            await Model.StartCreateAsync();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        // By name, not Single(): there are four of these on the screen since V.4b.
        private MaskedInput Field(string name) =>
            Window.GetVisualDescendants().OfType<MaskedInput>().Single(input => input.Name == name);

        public void Dispose()
        {
            Model.Dispose();
            _session.Dispose();
            _fixture.Dispose();
        }
    }
}
