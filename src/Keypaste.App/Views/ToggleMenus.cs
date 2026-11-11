using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.App.Controls;

namespace Keypaste.App.Views;

/// <summary>
/// A view's menus drawn as a toggle and a <c>Border.menu</c> panel: each opens with focus on its first item and closes on a choice,
/// Escape, which gives the keyboard back to its toggle, or a press anywhere but the menu or its toggle.
/// </summary>
internal sealed class ToggleMenus
{
    private readonly Control _view;
    private readonly (string Toggle, string Panel)[] _menus;

    private ToggleMenus(Control view, (string Toggle, string Panel)[] menus)
    {
        _view = view;
        _menus = menus;
        view.AddHandler(Button.ClickEvent, OnClick, RoutingStrategies.Bubble);
        view.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        view.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        view.AddHandler(ToggleButton.IsCheckedChangedEvent, OnToggled, RoutingStrategies.Bubble);
    }

    /// <param name="view">The view that holds the menus.</param>
    /// <param name="menus">Each menu's toggle and panel, by name.</param>
    internal static void Attach(Control view, params (string Toggle, string Panel)[] menus) => _ = new ToggleMenus(view, menus);

    private void OnClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button { Classes: var classes } && classes.Contains("menu-item"))
        {
            foreach (var (toggle, _) in Open())
            {
                toggle.IsChecked = false;
            }
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Visual source)
        {
            return;
        }

        foreach (var (toggle, panel) in Open())
        {
            var inside = source.GetSelfAndVisualAncestors().Any(visual =>
                ReferenceEquals(visual, toggle) || visual is Control control && control.Name == panel);

            if (!inside)
            {
                toggle.IsChecked = false;
            }
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || Open().FirstOrDefault() is not ({ } toggle, _))
        {
            return;
        }

        toggle.IsChecked = false;
        toggle.Focus(NavigationMethod.Tab);
        e.Handled = true;
    }

    private void OnToggled(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not ToggleButton { IsChecked: true } toggle
            || _menus.FirstOrDefault(menu => menu.Toggle == toggle.Name).Panel is not { } name)
        {
            return;
        }

        RevealedValue.HideShown();

        var method = toggle.Classes.Contains(":focus-visible") ? NavigationMethod.Tab : NavigationMethod.Pointer;

        Dispatcher.UIThread.Post(
            () =>
            {
                if (toggle.IsChecked == true
                    && _view.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.Name == name) is { } panel
                    && panel.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.IsEffectivelyEnabled) is { } first)
                {
                    first.Focus(method);
                }
            },
            DispatcherPriority.Loaded);
    }

    private IEnumerable<(ToggleButton Toggle, string Panel)> Open()
    {
        var toggles = _view.GetVisualDescendants().OfType<ToggleButton>().ToList();

        foreach (var (name, panel) in _menus)
        {
            if (toggles.FirstOrDefault(toggle => toggle.Name == name) is { IsChecked: true } toggle)
            {
                yield return (toggle, panel);
            }
        }
    }
}
