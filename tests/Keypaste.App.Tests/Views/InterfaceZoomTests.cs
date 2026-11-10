using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Approval;
using Keypaste.Core.Settings;
using Xunit;

namespace Keypaste.App.Tests.Views;

public sealed class InterfaceZoomTests
{
    private static readonly ScreenArea _laptopAtWindows150 = new(new PixelRect(0, 0, 1366, 720), 1.5);

    private static RunPrompt Run(string reason = "Run the migration.") => new()
    {
        Client = "claude-code",
        Label = "work laptop",
        Reason = reason,
        ReasonWasTruncated = false,
        ReasonWasAltered = false,
        Program = "/usr/local/bin/npm",
        Command = "npm run migrate",
        Directory = "/home/maya/acme/api",
        Project = "acme-api",
        Profile = "dev",
        Variables = [new("DATABASE_URL", "env/acme-api/.env", "DATABASE_URL")],
    };

    private static RunApprovalWindow Open(RunPrompt prompt, ScreenArea? area = null)
    {
        var window = new RunApprovalWindow(new RunApprovalViewModel(prompt));

        if (area is not null)
        {
            window.OpensOn = () => area;
        }

        window.Show();
        WindowInput.Drain();
        DrawnFrame.Capture(window);
        return window;
    }

    private static Rect InWindow(Visual control, TopLevel window) =>
        new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);

    [Fact]
    public Task The_steps_stop_at_each_end_and_a_hand_edited_value_rejoins_them() => HeadlessSession.On(() =>
    {
        Assert.False(InterfaceZoom.CanReduce);
        InterfaceZoom.Reduce();
        Assert.Equal(100, InterfaceZoom.Percent);

        foreach (var expected in new[] { 125, 150, 175, 200 })
        {
            InterfaceZoom.Enlarge();
            Assert.Equal(expected, InterfaceZoom.Percent);
        }

        Assert.False(InterfaceZoom.CanEnlarge);
        InterfaceZoom.Enlarge();
        Assert.Equal(200, InterfaceZoom.Percent);

        InterfaceZoom.Set(137);
        InterfaceZoom.Enlarge();
        Assert.Equal(150, InterfaceZoom.Percent);

        InterfaceZoom.Set(9000);
        Assert.Equal(200, InterfaceZoom.Percent);
        InterfaceZoom.Set(-50);
        Assert.Equal(100, InterfaceZoom.Percent);
    });

    [Theory]
    [InlineData(1, 720, 520)]
    [InlineData(2, 1440, 1040)]
    public void The_minimum_window_scales_on_a_screen_that_holds_it(double zoom, double width, double height)
    {
        var large = new ScreenArea(new PixelRect(0, 0, 2560, 1400), 1);

        Assert.Equal(new Size(width, height), WindowPlacement.Minimum(zoom, large));
    }

    [Fact]
    public void The_minimum_window_never_passes_the_working_area_so_every_zoom_stays_open_on_a_small_screen()
    {
        Assert.Equal(new Size(720, 480), WindowPlacement.Minimum(1, _laptopAtWindows150));

        var atLargest = WindowPlacement.Minimum(2, _laptopAtWindows150);
        Assert.Equal(1366 / 1.5, atLargest.Width, 3);
        Assert.Equal(480, atLargest.Height);
    }

    [Fact]
    public Task Zooming_grows_a_window_it_left_too_small_and_zooming_back_shrinks_nothing() => HeadlessSession.On(() =>
    {
        var window = new MainWindow { Width = 800, Height = 600 };
        window.Show();
        WindowInput.Drain();

        InterfaceZoom.Set(200);
        WindowInput.Drain();

        Assert.Equal(1440, window.MinWidth);
        Assert.Equal(1040, window.MinHeight);
        Assert.Equal(new Size(1440, 1040), window.ClientSize);
        Assert.True(window.Screens!.Primary!.WorkingArea.Contains(new PixelRect(window.Position, new PixelSize(1440, 1040))), $"at {window.Position}");
        Assert.Equal(200, window.Layout(null, false)!.ZoomPercent);

        InterfaceZoom.ActualSize();
        WindowInput.Drain();

        Assert.Equal(720, window.MinWidth);
        Assert.Equal(new Size(1440, 1040), window.ClientSize);
        window.Close();
    });

    [Fact]
    public Task A_restored_window_under_the_zoomed_minimum_comes_back_at_it_and_on_screen() => HeadlessSession.On(() =>
    {
        InterfaceZoom.Set(200);
        var window = new MainWindow();
        var area = window.Screens!.Primary!.WorkingArea;

        window.Restore(new WindowLayout(area.Right - 900, area.Bottom - 600, 800, 500, WindowMode.Normal, null, false, 200));
        window.Show();
        WindowInput.Drain();

        Assert.Equal(new Size(1440, 1040), window.ClientSize);
        Assert.True(area.Contains(new PixelRect(window.Position, new PixelSize(1440, 1040))), $"at {window.Position}");
        window.Close();
    });

    [Theory]
    [InlineData(1500, true)]
    [InlineData(1920, false)]
    public Task Under_zoom_the_breakpoint_reads_the_design_sizes_not_the_window_width(int windowWidth, bool narrow) => HeadlessSession.On(() =>
    {
        InterfaceZoom.Set(200);
        using var shell = new RenderedShell("keypaste-zoom-narrow-");
        shell.Window.Width = windowWidth;
        shell.Window.Height = 1100;
        shell.Frame();

        Assert.Equal(narrow, shell.Shell.IsNarrow);
        Assert.Equal(!narrow, shell.Named<Border>("Sidebar").IsEffectivelyVisible);
    });

    [Fact]
    public Task On_a_small_screen_the_body_keeps_its_minimum_and_scrolls_to_every_control() => HeadlessSession.On(() =>
    {
        InterfaceZoom.Set(200);
        using var shell = new RenderedShell("keypaste-zoom-small-");
        shell.Window.MinWidth = 0;
        shell.Window.MinHeight = 0;
        shell.Window.Width = _laptopAtWindows150.Size.Width;
        shell.Window.Height = _laptopAtWindows150.Size.Height;
        shell.Frame();

        var scroller = shell.Window.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.Equal(new Size(1440, 1040), scroller.Extent);
        Assert.Empty(ReachableControls.Problems(shell.Window));

        var lockButton = shell.Window.GetVisualDescendants().OfType<Button>().First(button => button.Name == "RailLock");
        lockButton.BringIntoView();
        WindowInput.Drain();
        shell.Frame();
        Assert.True(new Rect(shell.Window.ClientSize).Contains(InWindow(lockButton, shell.Window)), $"lock at {InWindow(lockButton, shell.Window)}");
    });

    private static (StackPanel Page, ScrollViewer Scroller, TextBox Field) Tall()
    {
        var field = new TextBox { Width = 200 };
        var page = new StackPanel { Children = { new Border { Height = 1300 }, field, new Border { Height = 800 } } };
        return (page, new ScrollViewer { Content = page }, field);
    }

    [Fact]
    public Task A_focused_field_moves_its_page_as_far_inside_the_frame_as_without_it() => HeadlessSession.On(() =>
    {
        var (_, alone, aloneField) = Tall();
        var plain = new Window { Width = 800, Height = 600, Content = alone };
        plain.Show();
        WindowInput.Drain();
        aloneField.Focus();
        WindowInput.Drain();
        DrawnFrame.Capture(plain);

        var (_, framed, framedField) = Tall();
        var window = new Window { Width = 800, Height = 600, Content = new ZoomFrame(framed) };
        window.Show();
        WindowInput.Drain();
        framedField.Focus();
        WindowInput.Drain();
        DrawnFrame.Capture(window);

        Assert.True(framed.Offset.Y > 0);
        Assert.Equal(alone.Offset, framed.Offset);
        plain.Close();
        window.Close();
    });

    [Fact]
    public Task In_a_frame_that_scrolls_a_field_reached_by_the_keyboard_comes_into_the_window() => HeadlessSession.On(() =>
    {
        InterfaceZoom.Set(200);
        var first = new Button { Content = "first", Margin = new Thickness(4) };
        var last = new Button { Content = "last", Margin = new Thickness(600, 400, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        var body = new StackPanel { Children = { first, last } };
        var window = new Window { Width = 500, Height = 300, Content = new ZoomFrame(body) };
        window.Show();
        WindowInput.Drain();
        DrawnFrame.Capture(window);
        Assert.False(new Rect(window.ClientSize).Contains(InWindow(last, window)));

        first.Focus();
        WindowInput.Drain();
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        WindowInput.Drain();
        DrawnFrame.Capture(window);

        Assert.True(last.IsFocused);
        Assert.True(new Rect(window.ClientSize).Contains(InWindow(last, window)), $"at {InWindow(last, window)}");
        window.Close();
    });

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    [InlineData(200)]
    public Task A_scrolled_page_stays_below_the_back_link_and_never_under_it(int percent) => HeadlessSession.On(() =>
    {
        InterfaceZoom.Set(percent);
        using var shell = new RenderedShell("keypaste-zoom-back-", vault =>
        {
            foreach (var key in Enumerable.Range(0, 30))
            {
                Keypaste.Core.Tests.ProjectVariables.Set(vault, "billing", $"KEY_{key}", "value");
            }
        });
        shell.Window.Width = 720 * percent / 100d;
        shell.Window.Height = 520 * percent / 100d;
        shell.Shell.SelectedSidebarRow = shell.Shell.SidebarRows.OfType<ProjectRow>().Single(row => row.Name == "billing");
        shell.Frame();

        var page = shell.Window.GetVisualDescendants().OfType<ScrollViewer>().First(scroller => scroller.Extent.Height > scroller.Viewport.Height + 24);
        var strip = shell.Named<Border>("BackStrip");
        var back = shell.Named<Button>("Back");
        page.Offset = new Vector(0, 30);
        WindowInput.Drain();
        shell.Frame();

        using var bitmap = shell.Window.CaptureRenderedFrame();
        var (pixels, width, _) = AmberElements.Read(bitmap!);
        var area = InWindow(strip, shell.Window);
        Assert.True(InWindow(page, shell.Window).Top >= area.Bottom - 1, $"the page starts at {InWindow(page, shell.Window)}, under the link at {area}");
        var skip = InWindow(back, shell.Window).Inflate(2);
        var ground = new byte[4];
        Array.Copy(pixels, (((int)area.Y + 1) * width * 4) + ((int)area.Right - 40) * 4, ground, 0, 4);

        for (var y = (int)area.Y + 1; y < (int)area.Bottom - 1; y++)
        {
            for (var x = (int)area.X + 1; x < (int)area.Right - 24; x++)
            {
                if (skip.Contains(new Point(x, y)))
                {
                    continue;
                }

                var at = ((y * width) + x) * 4;
                Assert.True(pixels.AsSpan(at, 4).SequenceEqual(ground), $"the page shows at {x},{y} behind the back link");
            }
        }
    });

    [Theory]
    [InlineData(100)]
    [InlineData(200)]
    public Task A_masked_value_stays_clear_of_the_note_that_wraps_under_it_at_the_narrowest_window(int percent) => HeadlessSession.On(() =>
    {
        InterfaceZoom.Set(percent);
        using var shell = new RenderedShell("keypaste-zoom-note-", vault =>
        {
            Keypaste.Core.Tests.ProjectVariables.Set(vault, "billing", "SHARED", "value");
            Keypaste.Core.Tests.ProjectVariables.Set(vault, "billing", "staging", "SHARED", "value");
        });
        shell.Window.Width = 720 * percent / 100d;
        shell.Window.Height = 520 * percent / 100d;
        shell.Shell.SelectedSidebarRow = shell.Shell.SidebarRows.OfType<Keypaste.App.ViewModels.ProjectRow>().Single(row => row.Name == "billing");
        shell.Frame();

        var notes = shell.Window.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible && block.Text == "same as dev").ToList();
        Assert.NotEmpty(notes);
        Assert.DoesNotContain(ReachableControls.Problems(shell.Window), problem => problem.Contains("overprints", StringComparison.Ordinal));
    });

    [Fact]
    public Task A_click_lands_on_the_control_under_it_at_200_percent() => HeadlessSession.On(() =>
    {
        InterfaceZoom.Set(200);
        var pressed = 0;
        var far = new Button { Content = "far", Width = 60, Height = 30, Margin = new Thickness(400, 300, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        far.Click += (_, _) => pressed++;
        var window = new Window { Width = 1500, Height = 1100, Content = new ZoomFrame(far) };
        window.Show();
        WindowInput.Drain();

        Assert.Equal(new Rect(800, 600, 120, 60), InWindow(far, window));
        WindowInput.Press(window, far);
        WindowInput.Release(window, far);

        Assert.Equal(1, pressed);
        window.Close();
    });

    [Fact]
    public Task A_masked_input_takes_clicks_and_keystrokes_and_stays_at_14px_or_larger_at_200_percent() => HeadlessSession.On(() =>
    {
        InterfaceZoom.Set(200);
        var typed = 0;
        var field = new MaskedInput { Focusable = true, Margin = new Thickness(200), Width = 300, Height = 40, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        field.CharacterTyped += (_, _) => typed++;
        var window = new MainWindow();
        window.FindControl<ContentControl>("Root")!.Content = field;
        window.Show();
        WindowInput.Drain();

        WindowInput.Click(window, field);
        WindowInput.Type(window, "ab");

        Assert.Equal(2, typed);
        Assert.True(field.FontSize * 2 >= 14, $"drawn at {field.FontSize * 2}");
        window.Close();
    });

    [Fact]
    public Task Menus_flyouts_and_tooltips_open_at_the_scale_of_what_opened_them() => HeadlessSession.On(() =>
    {
        static (Size Menu, Size Tip, Rect Anchor) Measure(TopLevel root, Button anchor)
        {
            var menu = new MenuFlyout();
            var item = new MenuItem { Header = "Copy password" };
            menu.Items.Add(item);
            menu.ShowAt(anchor);
            WindowInput.Drain();
            var menuSize = InWindow(item, TopLevel.GetTopLevel(item)!).Size;
            menu.Hide();
            WindowInput.Drain();

            var tip = new ToolTip { Content = "Copy password" };
            ToolTip.SetTip(anchor, tip);
            ToolTip.SetIsOpen(anchor, true);
            WindowInput.Drain();
            var tipSize = InWindow(tip, TopLevel.GetTopLevel(tip)!).Size;
            ToolTip.SetIsOpen(anchor, false);
            WindowInput.Drain();

            return (menuSize, tipSize, InWindow(anchor, root));
        }

        var anchor = new Button { Content = "open", Width = 80, Height = 30, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = new Window { Width = 1500, Height = 1100, Content = new ZoomFrame(anchor) };
        window.Show();
        WindowInput.Drain();
        var actual = Measure(window, anchor);

        InterfaceZoom.Set(200);
        WindowInput.Drain();
        DrawnFrame.Capture(window);
        var zoomed = Measure(window, anchor);

        Assert.Equal(actual.Anchor.Width * 2, zoomed.Anchor.Width, 1d);
        Assert.Equal(actual.Menu.Width * 2, zoomed.Menu.Width, 2d);
        Assert.Equal(actual.Menu.Height * 2, zoomed.Menu.Height, 2d);
        Assert.Equal(actual.Tip.Width * 2, zoomed.Tip.Width, 2d);
        Assert.Equal(actual.Tip.Height * 2, zoomed.Tip.Height, 2d);
        window.Close();
    });

    [Fact]
    public Task A_prompt_scales_as_a_whole_with_its_layout_fixed_and_focus_on_Deny() => HeadlessSession.On(() =>
    {
        var actual = Open(Run());
        var actualSize = actual.ClientSize;
        var deny = InWindow(actual.FindControl<Button>("Deny")!, actual);
        actual.Close();

        InterfaceZoom.Set(125);
        var zoomed = Open(Run());
        var button = zoomed.FindControl<Button>("Deny")!;

        Assert.Equal(actualSize.Width * 1.25, zoomed.ClientSize.Width, 1d);
        Assert.Equal(actualSize.Height * 1.25, zoomed.ClientSize.Height, 1d);
        Assert.Equal(deny.X * 1.25, InWindow(button, zoomed).X, 1d);
        Assert.Equal(deny.Y * 1.25, InWindow(button, zoomed).Y, 1d);
        Assert.True(button.IsFocused);
        Assert.False(button.IsDefault);
        zoomed.Close();
    });

    [Fact]
    public Task A_long_reason_cannot_change_a_zoomed_prompts_size_or_move_its_buttons() => HeadlessSession.On(() =>
    {
        InterfaceZoom.Set(200);
        var brief = Open(Run());
        var size = brief.ClientSize;
        var deny = InWindow(brief.FindControl<Button>("Deny")!, brief);
        brief.Close();

        var longest = Open(Run(new string('x', 4000)));

        Assert.Equal(size, longest.ClientSize);
        Assert.Equal(deny, InWindow(longest.FindControl<Button>("Deny")!, longest));
        longest.Close();
    });

    [Theory]
    [InlineData(1366, 728, 1)]
    [InlineData(1366, 720, 1.5)]
    [InlineData(800, 500, 1)]
    public Task A_prompt_on_a_small_screen_shows_Deny_and_the_reason_whole(int width, int height, double scaling) => HeadlessSession.On(() =>
    {
        InterfaceZoom.Set(200);
        var area = new ScreenArea(new PixelRect(0, 0, width, height), scaling);
        var window = Open(Run(string.Concat(Enumerable.Repeat("Run the migration. ", 100))), area);

        Assert.True(window.ClientSize.Width <= area.Size.Width + 1, $"{window.ClientSize} on {area.Size}");
        Assert.True(window.ClientSize.Height <= area.Size.Height + 1, $"{window.ClientSize} on {area.Size}");

        foreach (var name in new[] { "Deny", "AllowOnce", "AllowOnce", "ReasonText" })
        {
            var control = window.FindControl<Control>(name)!;
            var box = name == "ReasonText" ? control.FindAncestorOfType<Border>()! : control;
            Assert.True(new Rect(window.ClientSize).Contains(InWindow(box, window)), $"{name} at {InWindow(box, window)} in {window.ClientSize}");
        }

        Assert.True(window.FindControl<Button>("Deny")!.IsFocused);
        window.Close();
    });

    [Fact]
    public Task Zooming_while_a_prompt_is_open_moves_nothing_under_the_pointer() => HeadlessSession.On(() =>
    {
        var window = Open(Run());
        var size = window.ClientSize;
        var approve = InWindow(window.FindControl<Button>("AllowOnce")!, window);

        InterfaceZoom.Set(200);
        WindowInput.Drain();
        DrawnFrame.Capture(window);

        Assert.Equal(size, window.ClientSize);
        Assert.Equal(approve, InWindow(window.FindControl<Button>("AllowOnce")!, window));
        window.Close();
    });

    [Fact]
    public void A_prompts_scale_is_the_zoom_where_it_fits_and_the_largest_that_fits_elsewhere()
    {
        var card = new Size(508, 650);

        Assert.Equal(2, PromptFrame.ScaleFor(2, card, new Size(2560, 1400)));
        Assert.Equal(2, PromptFrame.ScaleFor(2, card, Size.Infinity));
        Assert.Equal(480d / 650, PromptFrame.ScaleFor(2, card, _laptopAtWindows150.Size), 6);
    }
}
