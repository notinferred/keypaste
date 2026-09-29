using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>The Agents screen; everything it reads and does is <see cref="AgentActivityViewModel"/>'s.</summary>
/// <remarks>
/// What is here is each client card's ⋯ menu, which opens with focus on its first choice and closes on
/// a choice, Escape or a press elsewhere, as the Items screen's menus do.
/// </remarks>
internal sealed partial class AgentActivityView : UserControl
{
    private const string _menuToggle = "ClientMenu";
    private const string _menuPanel = "ClientMenuPanel";

    public AgentActivityView()
    {
        AvaloniaXamlLoader.Load(this);

        AddHandler(Button.ClickEvent, OnClick, RoutingStrategies.Bubble);
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(ToggleButton.IsCheckedChangedEvent, OnMenuToggled, RoutingStrategies.Bubble);
    }

    /// <summary>A choice in a menu closes it.</summary>
    private void OnClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button { Classes: var classes } && classes.Contains("menu-item"))
        {
            foreach (var toggle in OpenMenus())
            {
                toggle.IsChecked = false;
            }
        }
    }

    /// <summary>A press anywhere but an open menu or its button closes it.</summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Visual source)
        {
            return;
        }

        foreach (var toggle in OpenMenus())
        {
            var inside = source.GetSelfAndVisualAncestors().Any(visual =>
                ReferenceEquals(visual, toggle) || IsPanelOf(visual, toggle));

            if (!inside)
            {
                toggle.IsChecked = false;
            }
        }
    }

    /// <summary>Escape closes an open menu and gives focus back to its button.</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || OpenMenus().FirstOrDefault() is not { } toggle)
        {
            return;
        }

        toggle.IsChecked = false;
        toggle.Focus(NavigationMethod.Tab);
        e.Handled = true;
    }

    /// <summary>An opened menu takes focus on its first choice, visibly so when it was opened from the keyboard.</summary>
    private void OnMenuToggled(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not ToggleButton { IsChecked: true, Name: _menuToggle } toggle)
        {
            return;
        }

        foreach (var other in OpenMenus().Where(other => !ReferenceEquals(other, toggle)))
        {
            other.IsChecked = false;
        }

        var method = toggle.Classes.Contains(":focus-visible") ? NavigationMethod.Tab : NavigationMethod.Pointer;

        Dispatcher.UIThread.Post(
            () =>
            {
                if (toggle.IsChecked == true
                    && this.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => IsPanelOf(control, toggle)) is { } panel
                    && panel.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.IsEffectivelyEnabled) is { } first)
                {
                    first.Focus(method);
                }
            },
            DispatcherPriority.Loaded);
    }

    /// <summary>Whether a visual is the menu a toggle opens: the panel of the same card.</summary>
    private static bool IsPanelOf(Visual visual, ToggleButton toggle) =>
        visual is Control { Name: _menuPanel } panel && ReferenceEquals(panel.DataContext, toggle.DataContext);

    private List<ToggleButton> OpenMenus() =>
        this.GetVisualDescendants().OfType<ToggleButton>().Where(toggle => toggle is { Name: _menuToggle, IsChecked: true }).ToList();
}
