using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.Core.Settings;

namespace Keypaste.App.Views;

internal sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        var body = (Control)Content!;
        Content = null;
        Content = new ZoomFrame(body);

        var strip = this.FindControl<Border>("DragStrip")!;
        var root = this.FindControl<ContentControl>("Root")!;

        // The shell's own titlebar is the drag area while it shows; a strip over it would take its
        // clicks.
        root.PropertyChanged += (_, change) =>
        {
            if (change.Property == ContentControl.ContentProperty)
            {
                strip.IsVisible = change.NewValue is not ShellView;
            }
        };

        AddHandler(GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);

        ApplyZoom();
        InterfaceZoom.Changed += ApplyZoom;
        Closed += (_, _) => InterfaceZoom.Changed -= ApplyZoom;

        Opened += (_, _) =>
        {
            ApplyZoom();
            NoteBounds();
        };

        // Posted, because a platform can report the new size of a maximize before its new state.
        PositionChanged += (_, _) => Dispatcher.UIThread.Post(
            () =>
            {
                HoldMinimum();
                NoteBounds();
            },
            DispatcherPriority.Background);
        Resized += (_, _) => Dispatcher.UIThread.Post(NoteBounds, DispatcherPriority.Background);
    }

    /// <summary>Opens the window as it was left, on the screen it was on or brought back onto one, before it shows.</summary>
    /// <param name="saved">The layout <c>app.toml</c> keeps.</param>
    internal void Restore(WindowLayout saved)
    {
        ArgumentNullException.ThrowIfNull(saved);

        if (Screens is not { ScreenCount: > 0 } screens)
        {
            return;
        }

        // The main screen first, where a window on no screen comes back to.
        var areas = screens.All
            .OrderByDescending(screen => screen.IsPrimary)
            .Select(ScreenArea.Of)
            .ToList();
        var placed = WindowPlacement.Fit(saved, new Size(MinWidth, MinHeight), areas);

        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = placed.Position;
        Width = placed.Size.Width;
        Height = placed.Size.Height;
        _normal = (placed.Position, placed.Size);
        WindowState = saved.Mode switch
        {
            WindowMode.Maximized => WindowState.Maximized,
            WindowMode.FullScreen => WindowState.FullScreen,
            _ => WindowState.Normal,
        };
    }

    /// <summary>How the window stands, at its own bounds under any maximize, or null before it has ever had bounds.</summary>
    /// <param name="sidebarHidden">Whether the sidebar is hidden, which the shell holds.</param>
    /// <param name="tableSorts">Each table's order, which the shell holds.</param>
    /// <param name="tableWidths">Each table's widths, which the shell holds.</param>
    internal WindowLayout? Layout(bool sidebarHidden, string? tableSorts = null, string? tableWidths = null)
    {
        NoteBounds();

        return _normal is not var (position, size)
            ? null
            : new WindowLayout(position.X, position.Y, (int)Math.Round(size.Width), (int)Math.Round(size.Height), _mode, sidebarHidden, InterfaceZoom.Percent, tableSorts, tableWidths);
    }

    private ScreenArea? ScreenInUse() =>
        Screens is { ScreenCount: > 0 } screens && (screens.ScreenFromWindow(this) ?? screens.Primary) is { } screen ? ScreenArea.Of(screen) : null;

    private Size HoldMinimum()
    {
        var minimum = WindowPlacement.Minimum(InterfaceZoom.Factor, ScreenInUse());
        MinWidth = minimum.Width;
        MinHeight = minimum.Height;
        return minimum;
    }

    /// <summary>Takes the zoom: the titlebar and the minimum scale with it, and a window it left too small grows, staying on its screen.</summary>
    private void ApplyZoom()
    {
        ExtendClientAreaTitleBarHeightHint = WindowChrome.TitleBarHeight * InterfaceZoom.Factor;
        var minimum = HoldMinimum();

        if (WindowState != WindowState.Normal || (Width >= minimum.Width && Height >= minimum.Height))
        {
            return;
        }

        if (IsVisible && ScreenInUse() is { } screen)
        {
            var now = new WindowLayout(Position.X, Position.Y, (int)Math.Ceiling(Width), (int)Math.Ceiling(Height), WindowMode.Normal, false);
            Position = WindowPlacement.Fit(now, minimum, [screen]).Position;
        }

        Width = Math.Max(Width, minimum.Width);
        Height = Math.Max(Height, minimum.Height);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // A minimized window is still one that fills its screen, or not, when it comes back.
        if (change.Property == WindowStateProperty)
        {
            _mode = WindowState switch
            {
                WindowState.Maximized => WindowMode.Maximized,
                WindowState.FullScreen => WindowMode.FullScreen,
                WindowState.Normal => WindowMode.Normal,
                _ => _mode,
            };
        }
    }

    // Only a shown window at its own size has bounds worth keeping: Windows moves a minimized one to -32000.
    private void NoteBounds()
    {
        if (IsVisible && WindowState == WindowState.Normal)
        {
            _normal = (Position, ClientSize);
        }
    }

    /// <summary>
    /// What the Edit menu acts on: the control that had focus before the menu button or one of its items took it.
    /// </summary>
    internal IInputElement? EditTarget =>
        _beforeMenu is Visual visual && GetTopLevel(visual) == this ? _beforeMenu : FocusManager?.GetFocusedElement();

    /// <summary>Shows the menu button, which only Windows and Linux use, with the menus it opens.</summary>
    internal void UseMenuButton(MenuFlyout menus)
    {
        ArgumentNullException.ThrowIfNull(menus);

        var button = this.FindControl<Button>("MenuButton")!;
        button.Flyout = menus;
        button.IsVisible = true;
    }

    private IInputElement? _beforeMenu;
    private (PixelPoint Position, Size Size)? _normal;
    private WindowMode _mode;

    private void OnGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.Source is Visual visual && !IsMenu(visual))
        {
            _beforeMenu = visual as IInputElement;
        }
    }

    private bool IsMenu(Visual visual) =>
        visual == this.FindControl<Button>("MenuButton") || visual.FindAncestorOfType<MenuItem>(includeSelf: true) is not null;
}
