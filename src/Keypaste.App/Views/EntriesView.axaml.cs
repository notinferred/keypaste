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
/// therefore assertable with no application and no display. What is here is layout: the screen's two
/// menus, the list's "+" and the pane's ⋯, each of which opens with focus on its first item and closes
/// on a choice, Escape or a press elsewhere; and the list's row, which folds away while the item takes
/// the whole view and comes back at the height it was dragged to.
/// </remarks>
internal sealed partial class EntriesView : UserControl
{
    /// <summary>The list's least height beside the preview.</summary>
    internal const double ListMinHeight = 100d;

    private const int _shareOfWhole = 1000;
    private const double _dragDistance = 6d;

    /// <summary>Each menu's toggle, by name, and the panel it opens.</summary>
    private static readonly (string Toggle, string Panel)[] _menus = [("EntryMenu", "EntryMenuPanel"), ("AddEntry", "AddMenuPanel")];

    private readonly RowDefinition _listRow;
    private readonly RowDefinition _previewRow;
    private readonly TextBox _search;
    private readonly ListBox _rows;
    private GridLength _listHeight;
    private EntriesViewModel? _entries;
    private EntryDetailViewModel? _detail;
    private ShellViewModel? _shell;
    private (EntryRow Row, PointerPressedEventArgs Press, Point At)? _dragFrom;

    public EntriesView()
    {
        AvaloniaXamlLoader.Load(this);

        var split = this.FindControl<Grid>("Split")!;
        _listRow = split.RowDefinitions[0];
        _previewRow = split.RowDefinitions[2];
        _listHeight = _listRow.Height;
        _listRow.PropertyChanged += OnRowChanged;
        _previewRow.PropertyChanged += OnRowChanged;

        AddHandler(Button.ClickEvent, OnClick, RoutingStrategies.Bubble);
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(ToggleButton.IsCheckedChangedEvent, OnMenuToggled, RoutingStrategies.Bubble);

        _rows = this.FindControl<ListBox>("Rows")!;
        _rows.AddHandler(PointerPressedEvent, OnRowPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        _rows.AddHandler(PointerMovedEvent, OnRowDragged, RoutingStrategies.Tunnel, handledEventsToo: true);
        _rows.AddHandler(PointerReleasedEvent, (_, _) => _dragFrom = null, RoutingStrategies.Tunnel, handledEventsToo: true);
        _search = this.FindControl<TextBox>("ShellSearch")!;
        _search.GotFocus += (_, _) => TellEntriesAboutSearchFocus();
        _search.LostFocus += (_, _) => TellEntriesAboutSearchFocus();
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

    private void TellEntriesAboutSearchFocus()
    {
        if (_entries is not null)
        {
            _entries.SearchHasFocus = _search.IsFocused;
        }
    }

    // The shell holds the split and the width, because this view is built again at every return to Items.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (this.FindAncestorOfType<ShellView>()?.DataContext is not ShellViewModel shell)
        {
            return;
        }

        ShowContactColumns(!shell.IsNarrow);

        // Before the rows are watched, so setting them one at a time cannot read as a move of the divider.
        if (shell.ListShare is { } share)
        {
            _listHeight = new GridLength(share, GridUnitType.Star);
            _previewRow.Height = new GridLength(_shareOfWhole - share, GridUnitType.Star);
            PlaceList();
        }

        _shell = shell;
        _shell.PropertyChanged += OnShellChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_shell is not null)
        {
            _shell.PropertyChanged -= OnShellChanged;
            _shell = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnShellChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.IsNarrow) && _shell is not null)
        {
            ShowContactColumns(!_shell.IsNarrow);
        }
    }

    // The list's header names a chosen group, so its column would only repeat it.
    private void ShowGroupColumn()
    {
        var shows = _entries?.SelectedGroup is null or { IsEverything: true };
        EntryColumns.SetShowsGroup(this, shows);
        _entries?.Table.Show(TableStates.Group, shows);
    }

    private void ShowContactColumns(bool shows)
    {
        EntryColumns.SetShowsContact(this, shows);
        _entries?.Table.Show(TableStates.Username, shows);
        _entries?.Table.Show(TableStates.Url, shows);
    }

    /// <summary>The divider, dragged or moved from the keyboard, gives each row a star length; the shell keeps their proportion.</summary>
    private void OnRowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == RowDefinition.HeightProperty && _shell is not null && _listRow.Height.IsStar && _previewRow.Height.IsStar)
        {
            var whole = _listRow.Height.Value + _previewRow.Height.Value;
            _shell.ListShare = Math.Clamp((int)Math.Round(_shareOfWhole * _listRow.Height.Value / whole), 1, _shareOfWhole - 1);
        }
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

        WatchDetail();
        TellEntriesAboutSearchFocus();
        PlaceList();
        ShowGroupColumn();

        if (_shell is not null)
        {
            ShowContactColumns(!_shell.IsNarrow);
        }
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
            ShowGroupColumn();
        }

        if (e.PropertyName == nameof(EntriesViewModel.Detail))
        {
            WatchDetail();
        }

        if (e.PropertyName == nameof(EntriesViewModel.ShowsList))
        {
            PlaceList();
        }

        // A new list of rows clears the ListBox's selection, and its binding re-reads Selected inside that change, where the value is lost and then counts as already published.
        if (e.PropertyName == nameof(EntriesViewModel.Selected) && !ReferenceEquals(_rows.SelectedItem, _entries?.Selected))
        {
            _rows.SelectedItem = _entries?.Selected;
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
