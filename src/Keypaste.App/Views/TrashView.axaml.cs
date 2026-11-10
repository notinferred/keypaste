using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.App.Controls;

namespace Keypaste.App.Views;

/// <summary>
/// The Trash screen.
/// </summary>
/// <remarks>
/// There is no secret control here: a trash row holds a name, where it came from and when it went, so nothing on
/// this screen needs masking or a hold. What is here is the sort menu's behaviour, which opens with focus on its
/// first item and closes on a choice, Escape or a press elsewhere.
/// </remarks>
internal sealed partial class TrashView : UserControl
{
    public TrashView()
    {
        AvaloniaXamlLoader.Load(this);

        AddHandler(Button.ClickEvent, OnClick, RoutingStrategies.Bubble);
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(ToggleButton.IsCheckedChangedEvent, OnMenuToggled, RoutingStrategies.Bubble);

        this.FindControl<ListDetail>("Split")!.PropertyChanged += (_, e) =>
        {
            if (e.Property == ListDetail.ShowsBackProperty)
            {
                var split = this.FindControl<ListDetail>("Split")!;
                Dispatcher.UIThread.Post(
                    () =>
                    {
                        if (TopLevel.GetTopLevel(this) is Window { IsVisible: true } window && KeyboardIsHere(window))
                        {
                            Control? next = split.ShowsBack ? this.FindControl<Button>("BackToList") : this.FindControl<ListBox>("TrashRows");
                            next?.Focus(NavigationMethod.Tab);
                        }
                    },
                    DispatcherPriority.Loaded);
            }
        };
    }

    private ToggleButton? OpenSortMenu => this.FindControl<ToggleButton>("SortMenu") is { IsChecked: true } toggle ? toggle : null;

    private bool KeyboardIsHere(Window window) => window.FocusManager?.GetFocusedElement() is not Visual focused || this.IsVisualAncestorOf(focused);

    private void OnClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button { Classes: var classes } && classes.Contains("menu-item") && OpenSortMenu is { } toggle)
        {
            toggle.IsChecked = false;
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Visual source || OpenSortMenu is not { } toggle)
        {
            return;
        }

        var inside = source.GetSelfAndVisualAncestors().Any(visual =>
            ReferenceEquals(visual, toggle) || visual is Control { Name: "SortMenuPanel" });

        if (!inside)
        {
            toggle.IsChecked = false;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || OpenSortMenu is not { } toggle)
        {
            return;
        }

        toggle.IsChecked = false;
        toggle.Focus(NavigationMethod.Tab);
        e.Handled = true;
    }

    private void OnMenuToggled(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not ToggleButton { Name: "SortMenu", IsChecked: true } toggle)
        {
            return;
        }

        var method = toggle.Classes.Contains(":focus-visible") ? NavigationMethod.Tab : NavigationMethod.Pointer;

        Dispatcher.UIThread.Post(
            () =>
            {
                if (toggle.IsChecked == true
                    && this.FindControl<Border>("SortMenuPanel") is { } panel
                    && panel.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.IsEffectivelyEnabled) is { } first)
                {
                    first.Focus(method);
                }
            },
            DispatcherPriority.Loaded);
    }
}
