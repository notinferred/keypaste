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

/// <summary>
/// The Entries screen.
/// </summary>
/// <remarks>
/// Everything this screen does — searching, building the group tree, reading and writing the vault —
/// belongs to <see cref="ViewModels.EntriesViewModel"/>, which names no Avalonia type and is
/// therefore assertable with no application and no display. What is here is layout: the screen's two
/// menus, the list's "+" and the pane's ⋯, each of which opens with focus on its first item and closes
/// on a choice, Escape or a press elsewhere; and the list's row, which folds away while the item takes
/// the whole view and comes back at the height it was dragged to.
/// </remarks>
internal sealed partial class EntriesView : UserControl
{
    /// <summary>The list's least height beside the preview (D-0344).</summary>
    internal const double ListMinHeight = 100d;

    /// <summary>Each menu's toggle, by name, and the panel it opens.</summary>
    private static readonly (string Toggle, string Panel)[] _menus = [("EntryMenu", "EntryMenuPanel"), ("AddEntry", "AddMenuPanel")];

    private readonly RowDefinition _listRow;
    private GridLength _listHeight;
    private EntriesViewModel? _entries;

    public EntriesView()
    {
        AvaloniaXamlLoader.Load(this);

        _listRow = this.FindControl<Grid>("Split")!.RowDefinitions[0];
        _listHeight = _listRow.Height;

        AddHandler(Button.ClickEvent, OnClick, RoutingStrategies.Bubble);
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(ToggleButton.IsCheckedChangedEvent, OnMenuToggled, RoutingStrategies.Bubble);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_entries is not null)
        {
            _entries.PropertyChanged -= OnEntriesChanged;
        }

        _entries = DataContext as EntriesViewModel;

        if (_entries is not null)
        {
            _entries.PropertyChanged += OnEntriesChanged;
        }

        PlaceList();
    }

    private void OnEntriesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EntriesViewModel.ShowsList))
        {
            PlaceList();
        }
    }

    /// <summary>A star row keeps its share of the height with nothing in it, so the list's row goes to zero while the item takes the whole view.</summary>
    private void PlaceList()
    {
        if (_entries is null || _entries.ShowsList)
        {
            _listRow.MinHeight = ListMinHeight;
            _listRow.Height = _listHeight;
            return;
        }

        if (_listRow.Height.Value > 0)
        {
            _listHeight = _listRow.Height;
        }

        _listRow.MinHeight = 0;
        _listRow.Height = new GridLength(0);
    }

    /// <summary>A choice in a menu closes it.</summary>
    private void OnClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button { Classes: var classes } && classes.Contains("menu-item"))
        {
            foreach (var (toggle, _) in OpenMenus())
            {
                toggle.IsChecked = false;
            }
        }
    }

    /// <summary>A press anywhere but a menu or its button closes it.</summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Visual source)
        {
            return;
        }

        foreach (var (toggle, panel) in OpenMenus())
        {
            var inside = source.GetSelfAndVisualAncestors().Any(visual =>
                ReferenceEquals(visual, toggle) || visual is Control control && control.Name == panel);

            if (!inside)
            {
                toggle.IsChecked = false;
            }
        }
    }

    /// <summary>Escape closes an open menu and gives focus back to its button.</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || OpenMenus().FirstOrDefault() is not ({ } toggle, _))
        {
            return;
        }

        toggle.IsChecked = false;
        toggle.Focus(NavigationMethod.Tab);
        e.Handled = true;
    }

    /// <summary>An opened menu takes focus on its first item, visibly so when it was opened from the keyboard.</summary>
    private void OnMenuToggled(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not ToggleButton { IsChecked: true } toggle
            || _menus.FirstOrDefault(menu => menu.Toggle == toggle.Name).Panel is not { } name)
        {
            return;
        }

        var method = toggle.Classes.Contains(":focus-visible") ? NavigationMethod.Tab : NavigationMethod.Pointer;

        Dispatcher.UIThread.Post(
            () =>
            {
                if (toggle.IsChecked == true
                    && this.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.Name == name) is { } panel
                    && panel.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.IsEffectivelyEnabled) is { } first)
                {
                    first.Focus(method);
                }
            },
            DispatcherPriority.Loaded);
    }

    /// <summary>Each menu that is open, with the name of its panel.</summary>
    private IEnumerable<(ToggleButton Toggle, string Panel)> OpenMenus()
    {
        var toggles = this.GetVisualDescendants().OfType<ToggleButton>().ToList();

        foreach (var (name, panel) in _menus)
        {
            if (toggles.FirstOrDefault(toggle => toggle.Name == name) is { IsChecked: true } toggle)
            {
                yield return (toggle, panel);
            }
        }
    }
}
