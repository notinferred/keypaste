using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Controls;
using Keypaste.App.ViewModels;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Rendering;

/// <summary>
/// Each surface that reveals a secret, read from the frames Skia drew for it (4.6).
/// </summary>
/// <remarks>
/// <para>
/// The reveal is a mouse click on the window that the platform hit-tests to the drawn cell, and every
/// claim that a value is or is not on screen is read from a captured frame by
/// <see cref="DrawnFrame"/>, never from <see cref="RevealedValue.Rendered"/>. A negative is always
/// made in a frame beside the positive it would have had to miss.
/// </para>
/// <para>
/// The automation half is the window differential, made for every surface: outside the toggle, whose name says
/// Hide once the value shows, the window's surface while a value is drawn equals the surface at rest, and neither
/// carries it.
/// </para>
/// </remarks>
public sealed class DrawnRevealTests
{
    // The list and the detail share 1000 wide with too little left for a 32-character value beside its label and Reveal.
    private const double _roomForValues = 1100;

    public static TheoryData<string> Surfaces => ["current", "revision", "env", "field", "plain-field"];

    [Theory]
    [MemberData(nameof(Surfaces))]
    public Task A_click_on_Reveal_draws_the_value_and_a_second_takes_it_off_the_frame(string surface) => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell();
        shell.Window.Width = _roomForValues;
        var (cell, value) = shell.Open(surface);
        var style = TextStyle.Of(cell);
        var dots = new string('•', cell.MaskedLength);
        var toggle = shell.ToggleOf(cell);

        // The env cell sits in a panel with a tooltip, whose 400 ms show timer starts at the hover on the
        // wall clock: where frames drew slowly it opened before the press, the press closed it, and
        // Avalonia's popup layer, which it never removes, was in the revealed surface alone (G.4b). The
        // cell shows no tooltip here, so the surfaces differ only by what the reveal does.
        ToolTip.SetServiceEnabled(cell, false);
        ToolTip.SetServiceEnabled(toggle, false);

        // At rest with focus on the toggle and the pointer already over it, so the only difference the click
        // can make is the click: both change IsPointerOver or focus on every ancestor.
        toggle.Focus();
        shell.Hover(toggle);
        var atRest = shell.Frame();
        var surfaceAtRest = AutomationSurface.OutsideTheToggles(shell.Window);
        Assert.True(atRest.Shows(dots, style, atRest.Of(cell, shell.Window)), "the cell's dots are not drawn at rest");
        AssertNoSecretIn(atRest, style);

        shell.Tap(toggle);
        var shown = shell.Frame();
        var surfaceWhileShown = AutomationSurface.OutsideTheToggles(shell.Window);
        Assert.True(shown.Shows(value, style, shown.Of(cell, shell.Window)), "the revealed value is not drawn in its cell");
        Assert.False(shown.Shows(dots, style, shown.Of(cell, shell.Window)), "the dots are still drawn while the value shows");
        foreach (var other in RenderedShell.Secrets.Where(secret => secret != value))
        {
            Assert.False(shown.Shows(other, style, shown.Everywhere), "a secret nobody revealed is drawn");
        }

        Assert.NotEmpty(surfaceWhileShown);
        Assert.Equal(surfaceAtRest, surfaceWhileShown);
        AssertNoSecretExposed(shell);

        shell.Tap(toggle);
        var hidden = shell.Frame();
        Assert.True(hidden.Shows(dots, style, hidden.Of(cell, shell.Window)), "the dots are not drawn after the second click");
        AssertNoSecretIn(hidden, style);
        AssertNoSecretExposed(shell);
    });

    [Theory]
    [MemberData(nameof(Surfaces))]
    public Task Locking_while_shown_takes_the_value_off_the_next_frame(string surface) => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell();
        shell.Window.Width = _roomForValues;
        var (cell, value) = shell.Open(surface);
        var style = TextStyle.Of(cell);

        shell.Tap(shell.ToggleOf(cell));
        var shown = shell.Frame();
        Assert.True(shown.Shows(value, style, shown.Everywhere), "the value is not drawn before the lock");

        shell.PressLock();

        Assert.True(shell.IsLocked);
        Assert.Null(shell.Session.Unlocked);

        var locked = shell.Frame();
        var label = shell.Named<TextBlock>("PasswordLabel");
        Assert.True(
            locked.Shows(label.Text!, TextStyle.Of(label), locked.Of(label, shell.Window)),
            "the unlock screen is not what the next frame draws");
        AssertNoSecretIn(locked, style);
        AssertNoSecretExposed(shell);
    });

    [Fact]
    public Task Space_on_an_env_values_Reveal_button_draws_it_until_Space_again() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell();
        var (cell, value) = shell.Open("env");
        var style = TextStyle.Of(cell);
        var button = shell.Window.GetVisualDescendants().OfType<RevealButton>().Single(candidate => candidate.DataContext is EnvVariableRow);
        button.Focus();
        RenderedShell.Drain();

        TapSpace(shell);
        var shown = shell.Frame();
        Assert.True(shown.Shows(value, style, shown.Of(cell, shell.Window)), "Space on the Reveal button does not draw the value");
        Assert.True(button.IsFocused, "the toggle lost focus");

        TapSpace(shell);
        var hidden = shell.Frame();
        Assert.True(hidden.Shows(new string('•', cell.MaskedLength), style, hidden.Of(cell, shell.Window)), "the dots are not drawn after the second Space");
        AssertNoSecretIn(hidden, style);
        AssertNoSecretExposed(shell);
    });

    /// <summary>A stray click must not put a secret on the screen: a value is revealed by its button alone.</summary>
    [Theory]
    [MemberData(nameof(Surfaces))]
    public Task A_click_on_a_masked_cell_leaves_it_masked(string surface) => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell();
        shell.Window.Width = _roomForValues;
        var (cell, _) = shell.Open(surface);
        var style = TextStyle.Of(cell);

        var dotsProbe = new string('•', cell.MaskedLength);
        var probe = new System.Text.StringBuilder();
        var started = System.Diagnostics.Stopwatch.StartNew();
        void Note(string stage)
        {
            var f = shell.Frame();
            var region = f.Of(cell, shell.Window);
            var origin = cell.TranslatePoint(default, shell.Window);
            var centre = cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), shell.Window)!.Value;
            var hit = shell.Window.InputHitTest(centre) as Visual;
            var chain = hit is null ? "null" : string.Join(" < ", new[] { hit }.Concat(hit.GetVisualAncestors()).Take(6).Select(v => $"{v.GetType().Name}#{(v as Control)?.Name}[{string.Join(",", (v as StyledElement)?.Classes ?? [])}]"));
            var copy = cell.FindAncestorOfType<PressToCopy>();
            var overlay = Avalonia.Controls.Primitives.OverlayLayer.GetOverlayLayer(cell);
            var overlayKids = overlay is null ? "none" : string.Join(",", overlay.Children.Select(c => $"{c.GetType().Name}@{c.Bounds} vis={c.IsVisible}"));
            var tipsOpen = string.Join(",", shell.Window.GetVisualDescendants().OfType<Control>().Where(ToolTip.GetIsOpen).Select(c => $"{c.GetType().Name}#{c.Name}"));
            probe.AppendLine($"[{stage} t={started.ElapsedMilliseconds}ms] scoreCell={f.Score(dotsProbe, style, region):F3} scoreAll={f.Score(dotsProbe, style, f.Everywhere):F3} region={region} origin={origin} bounds={cell.Bounds} desired={cell.DesiredSize} width={cell.Width} valueWidth={copy?.ValueWidth} copyBounds={copy?.Bounds} copyClasses=[{string.Join(",", copy?.Classes ?? [])}] revealed={cell.IsRevealed} scaling={f.Scaling} focus={shell.Window.FocusManager?.GetFocusedElement()?.GetType().Name}");
            probe.AppendLine($"  hit={chain}");
            probe.AppendLine($"  overlay={overlayKids} tipsOpen=[{tipsOpen}] topLevels={string.Join(",", shell.Window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().Select(p => $"{p.Name}:{p.IsOpen}"))}");
            var shape = f.Shape(region);
            for (var y = 0; y < region.Height; y++)
            {
                probe.Append("  |");
                for (var x = 0; x < region.Width; x++)
                {
                    probe.Append(shape[(y * region.Width) + x] ? '#' : '.');
                }

                probe.AppendLine();
            }
        }

        Note("rest");
        shell.Hover(cell);
        Note("hover");
        shell.Press(cell);
        Note("pressed");
        shell.Release(cell);
        Note("released");

        var frame = shell.Frame();
        Console.WriteLine($"PROBE {surface}\n{probe}");
        Assert.False(cell.IsRevealed);
        Assert.Null(RevealedValue.Shown);
        Assert.True(frame.Shows(new string('•', cell.MaskedLength), style, frame.Of(cell, shell.Window)), $"the cell's dots are not drawn after a click on it\nPROBE {surface}\n{probe}");
        AssertNoSecretIn(frame, style);
    });

    /// <summary>What a click on a cell still does: another environment's cell chooses that environment, and reveals nothing.</summary>
    [Fact]
    public Task A_click_on_another_environments_cell_selects_it_and_reveals_nothing() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell(seed: vault => ProjectVariables.Set(vault, "billing", "staging", "STRIPE_KEY", "SENTINEL-STAGING-VALUE-c41d"));
        var project = shell.OpenProject();
        project.SelectedProfile = "dev";
        RenderedShell.Drain();
        var other = shell.Window.GetVisualDescendants().OfType<Button>()
            .Single(button => button.Classes.Contains("cell") && button.DataContext is EnvProfileCell { Profile: "staging" });

        shell.Tap(other);

        Assert.Equal("staging", project.SelectedProfile);
        Assert.Null(RevealedValue.Shown);
    });

    /// <summary>The keyboard's way to a value on Env: Tab reaches the row's button, the row's actions show for it, Space reveals, and Hide stays in view after focus moves on.</summary>
    [Fact]
    public Task On_Env_Tab_reaches_Reveal_and_its_Hide_stays_in_view_while_the_value_shows() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell();
        var (cell, value) = shell.Open("env");
        var style = TextStyle.Of(cell);
        var toggle = shell.ToggleOf(cell);
        var actions = toggle.FindAncestorOfType<StackPanel>()!;
        Assert.Equal(0, actions.Opacity);

        for (var tab = 0; tab < 200 && !toggle.IsFocused; tab++)
        {
            shell.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            shell.Window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            RenderedShell.Drain();
        }

        Assert.True(toggle.IsFocused, "Tab does not reach the row's Reveal button");
        Assert.Equal(1, actions.Opacity);
        Assert.Equal("Reveal STRIPE_KEY", AutomationProperties.GetName(toggle));

        TapSpace(shell);
        var shown = shell.Frame();
        Assert.True(shown.Shows(value, style, shown.Of(cell, shell.Window)), "Space on the Reveal button does not draw the value");
        Assert.Equal("Hide STRIPE_KEY", AutomationProperties.GetName(toggle));

        shell.Window.GetVisualDescendants().OfType<Button>()
            .First(button => button.IsEffectivelyVisible && button.Focusable && !button.GetVisualAncestors().OfType<Border>().Any(border => border.Classes.Contains("matrix-row")))
            .Focus();
        RenderedShell.Drain();
        Assert.False(toggle.IsFocused);
        Assert.True(cell.IsRevealed);
        Assert.Equal(1, actions.Opacity);

        cell.EndReveal();
        RenderedShell.Drain();
        Assert.Equal(0, actions.Opacity);
    });

    /// <summary>A long value runs on over its neighbours but stops short of its row's actions, so Hide stays in view at the window's narrowest.</summary>
    [Fact]
    public Task A_long_value_on_Env_leaves_Hide_uncovered() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell(seed: vault => ProjectVariables.Set(vault, "billing", "LONG_KEY", new string('x', 400)));
        shell.Window.Width = 720;
        shell.OpenProject();
        RenderedShell.Drain();
        var cell = shell.Window.GetVisualDescendants().OfType<RevealedValue>().Single(value => value.DataContext is EnvVariableRow { Key: "LONG_KEY" });
        var toggle = shell.ToggleOf(cell);

        cell.BeginReveal();
        RenderedShell.Drain();

        Assert.True(cell.IsRevealed);
        var valueEnds = cell.TranslatePoint(new Point(cell.ShownRoom, 0), shell.Window)!.Value.X;
        var hideStarts = toggle.TranslatePoint(default, shell.Window)!.Value.X;
        Assert.True(valueEnds <= hideStarts, $"the value ends at {valueEnds} and Hide starts at {hideStarts}");
        cell.EndReveal();
    });

    [Fact]
    public Task A_click_on_Reveal_draws_the_value_where_the_detail_replaces_the_list() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell();
        shell.Window.Width = 720;
        var (cell, value) = shell.Open("current");
        var style = TextStyle.Of(cell);
        var toggle = shell.ToggleOf(cell);

        Assert.True(shell.Window.GetVisualDescendants().OfType<ListDetail>().Single().ShowsBack, "the detail does not stand in the list's place");
        ToolTip.SetServiceEnabled(toggle, false);

        shell.Tap(toggle);
        var shown = shell.Frame();
        Assert.True(shown.Shows(value, style, shown.Of(cell, shell.Window)), "the revealed value is not drawn in its cell");

        shell.Tap(toggle);
        var hidden = shell.Frame();
        Assert.False(hidden.Shows(value, style, hidden.Everywhere), "the value is still drawn after the second click");
    });

    /// <summary>A value longer than its cell wraps inside the cell, so none of it is cut and the cell keeps its width at rest.</summary>
    [Fact]
    public Task A_long_revealed_value_wraps_within_its_cell() => HeadlessSession.On(() =>
    {
        var longValue = new string('x', 150);
        using var shell = new RenderedShell(seed: vault => vault.AddEntry(new Core.VaultEntry { Title = "long", Username = "me", Password = longValue }));
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        entries.Selected = entries.Rows.Single(row => row.Title == "long");
        RenderedShell.Drain();
        var cell = shell.Named<RevealedValue>("CurrentPassword");
        shell.Frame();
        var atRest = cell.Bounds;

        cell.BeginReveal();
        shell.Frame();

        Assert.Equal(longValue, cell.Rendered);
        Assert.Equal(atRest.Width, cell.Bounds.Width);
        Assert.True(cell.Bounds.Height > atRest.Height, "the value did not wrap");
        cell.EndReveal();
    });

    public static TheoryData<string, string> WaysOut()
    {
        var data = new TheoryData<string, string>();

        foreach (var surface in Surfaces.Select(row => (string)row.Data))
        {
            foreach (var way in new[] { "time", "window focus", "minimize", "another place" })
            {
                data.Add(surface, way);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(WaysOut))]
    public Task Each_way_out_takes_the_value_off_the_frame_and_the_toggle_says_Reveal_again(string surface, string way) => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell();
        shell.Window.Width = _roomForValues;
        var clock = new ManualClock();
        shell.Window.SetValue(RevealedValue.ClockProperty, clock);
        var (cell, value) = shell.Open(surface);
        var style = TextStyle.Of(cell);

        cell.BeginReveal();
        RenderedShell.Drain();
        var shown = shell.Frame();
        Assert.True(shown.Shows(value, style, shown.Everywhere), "the value is not drawn before leaving");

        switch (way)
        {
            case "time":
                clock.Advance(RevealedValue.ShownFor);
                break;
            case "window focus":
                WindowInput.Deactivate(shell.Window);
                break;
            case "minimize":
                shell.Window.WindowState = WindowState.Minimized;
                RenderedShell.Drain();
                shell.Window.WindowState = WindowState.Normal;
                break;
            default:
                shell.Show<SettingsViewModel>(DestinationKind.Settings);
                break;
        }

        RenderedShell.Drain();
        Assert.Null(RevealedValue.Shown);
        AssertNoSecretIn(shell.Frame(), style);
        AssertNoSecretExposed(shell);
        Assert.DoesNotContain(shell.Window.GetVisualDescendants().OfType<RevealButton>(), button => (AutomationProperties.GetName(button) ?? string.Empty).StartsWith("Hide", StringComparison.Ordinal));
    });

    /// <summary>A search that hides the selected item closes the pane, and a group that does not hold it, and neither leaves its value behind.</summary>
    [Theory]
    [InlineData("search")]
    [InlineData("group")]
    [InlineData("another item")]
    public Task Leaving_the_item_takes_its_value_off_the_frame(string how) => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell(seed: vault => vault.AddEntry(new Core.VaultEntry { GroupPath = "work", Title = "gitlab", Username = "me", Password = "other-password-of-gitlab" }));
        shell.Window.Width = _roomForValues;
        var (cell, value) = shell.Open("current");
        var style = TextStyle.Of(cell);
        var entries = Assert.IsType<EntriesViewModel>(shell.Shell.Content);
        cell.BeginReveal();
        RenderedShell.Drain();
        var shown = shell.Frame();
        Assert.True(shown.Shows(value, style, shown.Everywhere), "the value is not drawn before leaving");

        switch (how)
        {
            case "search":
                entries.Search = "no item is called this";
                break;
            case "group":
                entries.SelectedGroup = entries.Groups.Single(group => group.Path == "work");
                break;
            default:
                entries.Selected = entries.Rows.Single(row => row.Title == "gitlab");
                break;
        }

        RenderedShell.Drain();
        Assert.False(cell.IsRevealed);
        Assert.Null(RevealedValue.Shown);
        AssertNoSecretIn(shell.Frame(), style);
    });

    /// <summary>The pane's edit form hides the password row and leaves it attached; cancelling must find dots.</summary>
    [Fact]
    public Task The_edit_form_hides_the_value_and_cancelling_finds_dots() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell();
        var (cell, _) = shell.Open("current");
        var detail = Assert.IsType<EntriesViewModel>(shell.Shell.Content).Detail!;
        cell.BeginReveal();

        detail.EditCommand.Execute(null);
        RenderedShell.Drain();
        shell.Frame();
        Assert.False(cell.IsRevealed);

        detail.CancelCommand.Execute(null);
        RenderedShell.Drain();
        Assert.False(cell.IsRevealed);
    });

    /// <summary>A value saved elsewhere while the pane stays open must not stay drawn as it was.</summary>
    [Fact]
    public Task A_saved_change_is_what_the_shown_cell_draws() => HeadlessSession.On(() =>
    {
        const string next = "SENTINEL-NEXT-CURRENT-b71e";
        using var shell = new RenderedShell();
        var (cell, _) = shell.Open("current");
        cell.BeginReveal();

        shell.Session.Unlocked!.UpdateEntry(new Core.VaultEntry { Title = "github", Username = "me", Password = next });
        Assert.IsType<EntriesViewModel>(shell.Shell.Content).Detail!.Reload();
        RenderedShell.Drain();

        Assert.Equal(next, cell.Rendered);
    });

    /// <summary>Idle lock is the same lock with another reason, and takes the same way off the screen.</summary>
    [Fact]
    public Task An_idle_lock_takes_the_value_off_the_next_frame() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell();
        shell.Window.Width = _roomForValues;
        var (cell, value) = shell.Open("current");
        var style = TextStyle.Of(cell);
        cell.BeginReveal();
        RenderedShell.Drain();
        var shown = shell.Frame();
        Assert.True(shown.Shows(value, style, shown.Everywhere), "the value is not drawn before the lock");

        ((ManualClock)shell.Session.Clock).Advance(shell.Session.IdleTimeout + TimeSpan.FromSeconds(1));
        shell.UntilLocked();

        Assert.False(cell.IsRevealed);
        Assert.Null(RevealedValue.Shown);
        AssertNoSecretIn(shell.Frame(), style);
    });

    private static void TapSpace(RenderedShell shell)
    {
        shell.Window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        shell.Window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        RenderedShell.Drain();
    }

    private static void AssertNoSecretIn(DrawnFrame frame, TextStyle style)
    {
        foreach (var secret in RenderedShell.Secrets)
        {
            Assert.False(frame.Shows(secret, style, frame.Everywhere), $"a {secret.Length}-character secret is drawn");
        }
    }

    private static void AssertNoSecretExposed(RenderedShell shell)
    {
        foreach (var secret in RenderedShell.Secrets)
        {
            AutomationSurface.AssertNothingExposes(shell.Window, secret);
        }
    }
}
