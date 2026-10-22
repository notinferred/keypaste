using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

internal sealed partial class ShellView : UserControl
{
    /// <summary>Room the group menu needs below where it opens, so one opened low in the sidebar stays in the window.</summary>
    private const double _groupMenuHeight = 160d;

    /// <summary>What takes the keyboard after each choice in the group menu: the form it opened.</summary>
    private static readonly Dictionary<string, string> _groupMenuFocus = new(StringComparer.Ordinal)
    {
        ["GroupMenuNewGroup"] = "NewGroupName",
        ["GroupMenuRename"] = "RenameGroupName",
        ["GroupMenuMove"] = "MoveGroupTarget",
        ["GroupMenuDelete"] = "KeepGroup",
    };

    private ShellViewModel? _shell;
    private Control? _focusBeforeImport;

    public ShellView()
    {
        AvaloniaXamlLoader.Load(this);

        var nav = this.FindControl<ListBox>("Nav")!;
        nav.ContainerPrepared += OnSidebarRowPrepared;
        nav.AddHandler(KeyDownEvent, OnSidebarKeyDown, RoutingStrategies.Tunnel);
        nav.AddHandler(PointerReleasedEvent, OnSidebarPointerReleased, RoutingStrategies.Tunnel);

        this.FindControl<Border>("GroupMenuPanel")!.AddHandler(Button.ClickEvent, OnGroupMenuClick, RoutingStrategies.Bubble);
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>A heading names the rows under it and cannot be chosen; a group's row is a denser tree row.</summary>
    private static void OnSidebarRowPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        var heading = e.Container.DataContext is SidebarHeading;
        e.Container.IsEnabled = !heading;
        e.Container.Focusable = !heading;
        e.Container.Classes.Set("heading", heading);
        e.Container.Classes.Set("group", e.Container.DataContext is GroupRow);
    }

    /// <summary>
    /// Right unfolds the chosen row and Left folds it, as in KeePassXC's group tree. Folding replaces the
    /// rows, so the keyboard is handed back to the chosen one, or the next key would go nowhere. The menu
    /// key, or Shift+F10, opens the chosen group's menu.
    /// </summary>
    private void OnSidebarKeyDown(object? sender, KeyEventArgs e)
    {
        if (_shell is null || sender is not ListBox nav)
        {
            return;
        }

        if ((e.Key == Key.Apps && e.KeyModifiers == KeyModifiers.None) || (e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.Shift))
        {
            if (_shell.SelectedSidebarRow is GroupRow chosenGroup && nav.ContainerFromItem(chosenGroup) is Control row)
            {
                OpenGroupMenu(chosenGroup, row.TranslatePoint(new Point(24, row.Bounds.Height), this), NavigationMethod.Tab);
                e.Handled = true;
            }

            return;
        }

        if (e.Key is not (Key.Left or Key.Right) || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        var selected = _shell.SelectedSidebarRow;

        if (selected is NavItem { IsExpandable: true } or GroupRow { HasChildren: true })
        {
            _shell.Fold(selected, open: e.Key == Key.Right);
            e.Handled = true;
            Dispatcher.UIThread.Post(() => FocusChosenRow(nav), DispatcherPriority.Loaded);
        }
    }

    /// <summary>A right-click on a group's row opens its menu where the pointer is.</summary>
    private void OnSidebarPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_shell is null || e.InitialPressMouseButton != MouseButton.Right || e.Source is not Visual source
            || source.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault() is not { DataContext: GroupRow row })
        {
            return;
        }

        OpenGroupMenu(row, e.GetPosition(this), NavigationMethod.Pointer);
        e.Handled = true;
    }

    /// <summary>Opens the group menu at a point of this view, with the keyboard on its first item.</summary>
    private void OpenGroupMenu(GroupRow row, Point? at, NavigationMethod method)
    {
        if (_shell is null)
        {
            return;
        }

        var panel = this.FindControl<Border>("GroupMenuPanel")!;
        var point = at ?? default;
        Canvas.SetLeft(panel, Math.Max(0, point.X));
        Canvas.SetTop(panel, Math.Max(0, Math.Min(point.Y, Bounds.Height - _groupMenuHeight)));

        _shell.OpenGroupMenu(row);

        Dispatcher.UIThread.Post(
            () =>
            {
                if (_shell?.HasGroupMenu == true
                    && panel.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.IsEffectivelyEnabled) is { } first)
                {
                    first.Focus(method);
                }
            },
            DispatcherPriority.Loaded);
    }

    /// <summary>A choice in the group menu hands the keyboard to the form it opened.</summary>
    private void OnGroupMenuClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button { Name: { } name } || !_groupMenuFocus.TryGetValue(name, out var target))
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (this.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.Name == target && control.IsEffectivelyVisible) is { } form)
                {
                    form.Focus(NavigationMethod.Tab);
                }
            },
            DispatcherPriority.Loaded);
    }

    /// <summary>A press anywhere but the open group menu closes it.</summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_shell?.HasGroupMenu != true || e.Source is not Visual source)
        {
            return;
        }

        if (!source.GetSelfAndVisualAncestors().Any(visual => visual is Control { Name: "GroupMenuPanel" }))
        {
            _shell.CloseGroupMenu();
        }
    }

    /// <summary>Escape closes the open group menu and gives the keyboard back to the group's row.</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _shell?.HasGroupMenu != true)
        {
            return;
        }

        _shell.CloseGroupMenu();
        e.Handled = true;
        FocusChosenRow(this.FindControl<ListBox>("Nav")!);
    }

    /// <summary>Gives the keyboard to the sidebar's chosen row.</summary>
    private void FocusChosenRow(ListBox nav)
    {
        if (_shell?.SelectedSidebarRow is { } chosen && nav.ContainerFromItem(chosen) is Control container)
        {
            container.Focus(NavigationMethod.Directional);
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_shell is not null)
        {
            _shell.SearchFocusRequested -= OnSearchFocusRequested;
            _shell.PropertyChanged -= OnShellChanged;
        }

        _shell = DataContext as ShellViewModel;

        if (_shell is not null)
        {
            _shell.SearchFocusRequested += OnSearchFocusRequested;
            _shell.PropertyChanged += OnShellChanged;
        }
    }

    /// <summary>The import dialog hands the keyboard back to what had it, or to Items' "+", when it closes.</summary>
    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ShellViewModel.HasImport) || _shell is null)
        {
            return;
        }

        if (_shell.HasImport)
        {
            // Read now: the dialog moves focus into itself on a later dispatcher pass.
            _focusBeforeImport = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                var back = _focusBeforeImport is { IsEffectivelyVisible: true } before && TopLevel.GetTopLevel(before) is not null
                    ? before
                    : this.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.Name == "AddEntry") ?? this.FindControl<Control>("ShellSearch");
                _focusBeforeImport = null;
                back?.Focus();
            },
            DispatcherPriority.Background);
    }

    private void OnSearchFocusRequested(object? sender, EventArgs e)
    {
        var search = this.FindControl<TextBox>("ShellSearch")!;
        search.Focus();
        search.SelectAll();
    }
}
