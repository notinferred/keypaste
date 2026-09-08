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

        Assert.IsType<ControlAutomationPeer>(peer);
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
        AssertNothingExposes(screen.Password, Fixture);
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
        AssertNothingExposes(screen.Password, Fixture);
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
        AssertNothingExposes(screen.Password, Rare.ToString());
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

            screen.Window.KeyTextInput(Alpha);
            var first = Surface(screen.Password);

            screen.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            screen.Window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.Equal(0, screen.Model.MaskedLength);

            screen.Window.KeyTextInput(Beta);
            var second = Surface(screen.Password);

            Assert.Equal(first, second);
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

        var surface = Surface(screen.Password).Select(entry => entry.Text).ToList();

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

        AssertNothingExposes(screen.Window, Fixture);
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
                () => AssertNothingExposes(screen.Password, Fixture));

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

        var failure = Assert.ThrowsAny<XunitException>(() => AssertNothingExposes(box, Fixture));

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

        var failure = Assert.ThrowsAny<XunitException>(() => AssertNothingExposes(block, Fixture));

        Assert.Contains("GetName", failure.Message, StringComparison.Ordinal);
    });

    [Fact]
    public Task A_control_that_kept_the_characters_in_a_styled_property_fails_the_check() =>
        HeadlessSession.On(() =>
        {
            var control = new Remembering { Kept = Fixture };

            var failure = Assert.ThrowsAny<XunitException>(
                () => AssertNothingExposes(control, Fixture));

            Assert.Contains("styled property", failure.Message, StringComparison.Ordinal);
        });

    private static void AssertNothingExposes(Control control, string secret)
    {
        foreach (var (source, text) in Surface(control))
        {
            if (text.Contains(secret, StringComparison.Ordinal))
            {
                Assert.Fail($"{source} exposes the fixture password");
            }
        }
    }

    /// <summary>
    /// Everything a process on the accessibility bus, or a reader of this control's own state, can
    /// get back — peer by peer down the tree, then every attached automation property and every
    /// registered styled property on every element in it.
    /// </summary>
    private static List<(string Source, string Text)> Surface(Control control)
    {
        var found = new List<(string Source, string Text)>();

        Walk(ControlAutomationPeer.CreatePeerForElement(control), found);

        foreach (var element in Descendants(control))
        {
            var what = element.GetType().Name;

            Add(found, $"AutomationProperties.Name on {what}", AutomationProperties.GetName(element));
            Add(found, $"AutomationProperties.HelpText on {what}", AutomationProperties.GetHelpText(element));
            Add(found, $"AutomationProperties.ItemStatus on {what}", AutomationProperties.GetItemStatus(element));
            Add(found, $"AutomationProperties.ItemType on {what}", AutomationProperties.GetItemType(element));
            Add(found, $"AutomationProperties.AutomationId on {what}", AutomationProperties.GetAutomationId(element));
            Add(found, $"AutomationProperties.AcceleratorKey on {what}", AutomationProperties.GetAcceleratorKey(element));
            Add(found, $"AutomationProperties.AccessKey on {what}", AutomationProperties.GetAccessKey(element));

            foreach (var property in AvaloniaPropertyRegistry.Instance.GetRegistered(element))
            {
                Add(found, $"the {property.Name} styled property on {what}", element.GetValue(property)?.ToString());
            }
        }

        return found;
    }

    private static IEnumerable<Control> Descendants(Control control) =>
        new[] { control }.Concat(control.GetVisualDescendants().OfType<Control>());

    private static void Walk(AutomationPeer peer, List<(string Source, string Text)> found)
    {
        var what = peer.GetType().Name;

        Add(found, $"{what}.GetName", peer.GetName());
        Add(found, $"{what}.GetHelpText", peer.GetHelpText());
        Add(found, $"{what}.GetItemStatus", peer.GetItemStatus());
        Add(found, $"{what}.GetItemType", peer.GetItemType());
        Add(found, $"{what}.GetAutomationId", peer.GetAutomationId());
        Add(found, $"{what}.GetClassName", peer.GetClassName());
        Add(found, $"{what}.GetLocalizedControlType", peer.GetLocalizedControlType());
        Add(found, $"{what}.GetAcceleratorKey", peer.GetAcceleratorKey());
        Add(found, $"{what}.GetAccessKey", peer.GetAccessKey());
        Add(found, "IValueProvider.Value", peer.GetProvider<IValueProvider>()?.Value);

        foreach (var child in peer.GetChildren())
        {
            Walk(child, found);
        }
    }

    private static void Add(List<(string Source, string Text)> found, string source, string? text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            found.Add((source, text));
        }
    }

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
            Model = new UnlockViewModel(_session, _fixture.Home, () => { });

            Window = new Window { Content = new UnlockView { DataContext = Model } };
            Window.Show();

            // Focus is posted at Background priority, so let the dispatcher drain before typing.
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        internal Window Window { get; }

        internal UnlockViewModel Model { get; }

        internal MaskedInput Password => Window.GetVisualDescendants().OfType<MaskedInput>().Single();

        public void Dispose()
        {
            Model.Dispose();
            _session.Dispose();
            _fixture.Dispose();
        }
    }
}
