using System.ComponentModel;
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
    private ShellViewModel? _shell;
    private Control? _focusBeforeImport;

    public ShellView()
    {
        AvaloniaXamlLoader.Load(this);

        var nav = this.FindControl<ListBox>("Nav")!;
        nav.ContainerPrepared += OnSidebarRowPrepared;
        nav.AddHandler(KeyDownEvent, OnSidebarKeyDown, RoutingStrategies.Tunnel);
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
    /// rows, so the keyboard is handed back to the chosen one, or the next key would go nowhere.
    /// </summary>
    private void OnSidebarKeyDown(object? sender, KeyEventArgs e)
    {
        if (_shell is null || sender is not ListBox nav || e.Key is not (Key.Left or Key.Right) || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        var row = _shell.SelectedSidebarRow;

        if (row is NavItem { IsExpandable: true } or GroupRow { HasChildren: true })
        {
            _shell.Fold(row, open: e.Key == Key.Right);
            e.Handled = true;
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (_shell?.SelectedSidebarRow is { } chosen && nav.ContainerFromItem(chosen) is Control container)
                    {
                        container.Focus(NavigationMethod.Directional);
                    }
                },
                DispatcherPriority.Loaded);
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
