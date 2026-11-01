using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace Keypaste.App.Views;

internal sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

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
