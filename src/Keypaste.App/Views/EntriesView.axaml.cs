using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>
/// The Entries screen.
/// </summary>
/// <remarks>
/// Everything this screen does — searching, building the group tree, reading and writing the vault —
/// belongs to <see cref="ViewModels.EntriesViewModel"/>, which names no Avalonia type and is
/// therefore assertable with no application and no display. What is here is layout: the screen's
/// menus, the list's sort and "+" and the pane's ⋯, each of which opens with focus on its first item and closes
/// on a choice, Escape or a press elsewhere; and the keyboard's place when a narrow width swaps the list for the detail.
/// </remarks>
internal sealed partial class EntriesView : UserControl
{
    private const double _dragDistance = 6d;

    /// <summary>Each menu's toggle, by name, and the panel it opens.</summary>
    private static readonly (string Toggle, string Panel)[] _menus = [("EntryMenu", "EntryMenuPanel"), ("AddEntry", "AddMenuPanel"), ("SortMenu", "SortMenuPanel"), ("AddFieldMenu", "AddFieldMenuPanel")];

    private readonly ListDetail _split;
    private readonly ListBox _rows;
    private EntriesViewModel? _entries;
    private EntryDetailViewModel? _detail;
    private (EntryRow Row, PointerPressedEventArgs Press, Point At)? _dragFrom;

    public EntriesView()
    {
        AvaloniaXamlLoader.Load(this);

        _split = this.FindControl<ListDetail>("Split")!;
        _split.PropertyChanged += OnSplitChanged;

        AddHandler(Button.ClickEvent, OnClick, RoutingStrategies.Bubble);
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(ToggleButton.IsCheckedChangedEvent, OnMenuToggled, RoutingStrategies.Bubble);

        _rows = this.FindControl<ListBox>("Rows")!;
        _rows.AddHandler(PointerPressedEvent, OnRowPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        _rows.AddHandler(PointerMovedEvent, OnRowDragged, RoutingStrategies.Tunnel, handledEventsToo: true);
        _rows.AddHandler(PointerReleasedEvent, (_, _) => _dragFrom = null, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>Where the detail takes the list's place, the keyboard goes with it, and back to the chosen row's list when it returns.</summary>
    private void OnSplitChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != ListDetail.ShowsBackProperty)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (TopLevel.GetTopLevel(this) is Window { IsVisible: true })
                {
                    Control? next = _split.ShowsBack ? this.FindControl<Button>("BackToList") : _rows;
                    next?.Focus(NavigationMethod.Tab);
                }
            },
            DispatcherPriority.Loaded);
    }

    /// <summary>A mouse or a pen on a row may become a drag; a finger scrolls.</summary>
    private void OnRowPressed(object? sender, PointerPressedEventArgs e)
    {
        var over = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault()?.DataContext as EntryRow;

        _dragFrom = over is not null && e.Pointer.Type != PointerType.Touch && e.GetCurrentPoint(_rows).Properties.IsLeftButtonPressed && _entries is { HoldsNoForm: true }
            ? (over, e, e.GetPosition(_rows))
            : null;
    }

    private void OnRowDragged(object? sender, PointerEventArgs e)
    {
        if (_dragFrom is not { } from || (e.GetPosition(_rows) - from.At) is var moved && Math.Abs(moved.X) + Math.Abs(moved.Y) < _dragDistance)
        {
            return;
        }

        _dragFrom = null;

        if (_entries is { HoldsNoForm: true } && this.FindAncestorOfType<ShellView>() is { } shell)
        {
            _ = shell.DragItemAsync(from.Row, from.Press);
        }
    }

    /// <summary>Find asks for the search, so it comes into reach first; the shell focuses it after the next layout.</summary>
    internal void MakeSearchReachable() => _entries?.MakeSearchReachable(_split.ShowsBack);

    // The list's header names a chosen group, so a sort by group would only repeat it.
    private void ApplyGroupSort() =>
        _entries?.Table.Show(TableStates.Group, _entries.SelectedGroup is null or { IsEverything: true });

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

        WatchDetail();
        ApplyGroupSort();
    }

    private void WatchDetail()
    {
        if (_detail is not null)
        {
            _detail.FieldRebuilt -= FocusFieldMenu;
        }

        _detail = _entries?.Detail;

        if (_detail is not null)
        {
            _detail.FieldRebuilt += FocusFieldMenu;
        }
    }

    /// <summary>A rebuilt row took the keyboard's place with it, so its new ⋯ takes it, keeping the Item menu's field commands on.</summary>
    private void FocusFieldMenu(string field) =>
        Dispatcher.UIThread.Post(
            () => this.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => button.Name == "FieldMenu" && button.DataContext is EntryFieldRow row && row.Name == field)
                ?.Focus(NavigationMethod.Tab),
            DispatcherPriority.Loaded);

    private void OnEntriesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EntriesViewModel.SelectedGroup))
        {
            ApplyGroupSort();
        }

        if (e.PropertyName == nameof(EntriesViewModel.Detail))
        {
            WatchDetail();
        }

        // A new list of rows clears the ListBox's selection, and its binding re-reads Selected inside that change, where the value is lost and then counts as already published.
        if (e.PropertyName == nameof(EntriesViewModel.Selected) && !ReferenceEquals(_rows.SelectedItem, _entries?.Selected))
        {
            _rows.SelectedItem = _entries?.Selected;
        }
    }

    /// <summary>A choice in a menu closes it, and a field's ⋯ asks for that row's menu as the menu key does.</summary>
    private void OnClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button { Name: "FieldMenu" } fieldMenu)
        {
            fieldMenu.RaiseEvent(new ContextRequestedEventArgs());
            return;
        }

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
        if (e.Key == Key.Escape && CloseGeneratorPanel())
        {
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Escape || OpenMenus().FirstOrDefault() is not ({ } toggle, _))
        {
            return;
        }

        toggle.IsChecked = false;
        toggle.Focus(NavigationMethod.Tab);
        e.Handled = true;
    }

    /// <summary>Escape closes the generator anchored to a password field and gives focus back to the button that opened it.</summary>
    private bool CloseGeneratorPanel()
    {
        string button;

        if (_entries?.NewItem is { HasGeneratorPanel: true } form)
        {
            form.CloseGeneratorCommand.Execute(null);
            button = "NewEntryGenerate";
        }
        else if (_entries?.Detail is { HasGeneratorPanel: true } detail)
        {
            detail.CloseGeneratorCommand.Execute(null);
            button = "ReplacementPasswordGenerate";
        }
        else
        {
            return false;
        }

        this.GetVisualDescendants().OfType<Button>().FirstOrDefault(candidate => candidate.Name == button)?.Focus(NavigationMethod.Tab);
        return true;
    }

    /// <summary>An opened menu takes focus on its first item, visibly so when it was opened from the keyboard.</summary>
    private void OnMenuToggled(object? sender, RoutedEventArgs e)
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
