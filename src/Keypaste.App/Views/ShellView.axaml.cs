using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

internal sealed partial class ShellView : UserControl
{
    private static void OnToastPointerEntered(object? sender, PointerEventArgs e) => ((sender as Control)?.DataContext as Toast)?.Hold();

    private static void OnToastPointerExited(object? sender, PointerEventArgs e) => ((sender as Control)?.DataContext as Toast)?.Release();

    private ShellViewModel? _shell;
    private Control? _focusBeforeImport;
    private Control? _focusBeforeDiscardPrompt;
    private Control? _focusBeforeHandedPrompt;

    // Kept past the moment a control leaves the screen and takes the focus with it.
    private Visual? _lastFocused;
    private TopLevel? _root;

    public ShellView()
    {
        AvaloniaXamlLoader.Load(this);
        Cue = this.FindControl<DropCue>(nameof(Cue))!;

        var nav = this.FindControl<ListBox>("Nav")!;
        nav.ContainerPrepared += OnSidebarRowPrepared;
        nav.AddHandler(KeyDownEvent, OnSidebarKeyDown, RoutingStrategies.Tunnel);
        this.FindControl<ListBox>("RailNav")!.AddHandler(TappedEvent, OnRailPlaceTapped);
        this.FindControl<Button>("SidebarAdd")!.Click += OnSidebarAdd;

        AddHandler(GotFocusEvent, (_, e) => _lastFocused = e.Source as Visual);
        AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Bubble);
        AddHandler(PointerReleasedEvent, OnControlClick, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnShiftF10, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnShiftF10, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnPressOutsideSidebar, RoutingStrategies.Tunnel, handledEventsToo: true);

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    // On the window rather than here, ahead of the handlers the dialogs under the question put there, so Escape answers it first.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _root = TopLevel.GetTopLevel(this);
        _root?.AddHandler(KeyDownEvent, OnDiscardPromptEscape, RoutingStrategies.Tunnel);
        InterfaceZoom.Changed += PlaceSidebar;
        PlaceSidebar();
        Commands?.SidebarChanged();
        NameTheWindow();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        if (_shell is not null)
        {
            _shell.IsNarrow = e.NewSize.Width < WindowChrome.NarrowBelow;
            _shell.WindowWidth = e.NewSize.Width;
        }
    }

    // Held so the title can go back once this has left the window, when the window can no longer be asked for.
    private Window? _named;

    private void NameTheWindow()
    {
        _named = TopLevel.GetTopLevel(this) as Window;

        if (_named is { } window)
        {
            window.Title = _shell?.WindowTitle ?? ShellViewModel.BareWindowTitle;
        }
    }

    /// <summary>The titlebar's first column follows the sidebar under it; a sidebar over the content spans it and leaves the columns as the rail does, and with neither the foot moves up to the titlebar.</summary>
    private void PlaceSidebar()
    {
        var beside = _shell is { ShowsSidebar: true, SidebarOverlays: false };
        var title = this.FindControl<Grid>("TitleColumns")!;
        this.FindControl<Border>("TitleBar")!.Classes.Set("beside-sidebar", beside);
        this.FindControl<Border>("TitleOverSidebar")!.IsVisible = beside;
        var foot = this.FindControl<Border>("VaultFoot")!;
        var sidebarSlot = this.FindControl<Decorator>("SidebarFootSlot")!;
        var titleSlot = this.FindControl<Decorator>("TitleFootSlot")!;
        var noRoomBelow = _shell is { ShowsSidebar: false, ShowsRail: false };
        foot.Classes.Set("in-titlebar", noRoomBelow);

        if (noRoomBelow && titleSlot.Child != foot)
        {
            sidebarSlot.Child = null;
            titleSlot.Child = foot;
        }
        else if (!noRoomBelow && sidebarSlot.Child != foot)
        {
            titleSlot.Child = null;
            sidebarSlot.Child = foot;
        }

        title.Margin = WindowChrome.CaptionInset(InterfaceZoom.Factor);
        title.ColumnDefinitions[0].Width = new GridLength(beside ? WindowChrome.SidebarWidth : WindowChrome.LeadingInset(InterfaceZoom.Factor));
        var collapsed = _shell is { ShowsRail: true } or { SidebarOverlays: true } ? WindowChrome.RailWidth : 0;
        this.FindControl<Grid>("BodyColumns")!.ColumnDefinitions[0].Width = new GridLength(beside ? WindowChrome.SidebarWidth : collapsed);
        Grid.SetColumnSpan(this.FindControl<Border>("Sidebar")!, _shell is { SidebarOverlays: true } ? 2 : 1);
    }

    /// <summary>A sidebar opened over the content takes the keyboard. What goes with the keyboard on it hands it on rather than to nothing, unless a press has already moved it.</summary>
    private void OnSidebarShownChanged() =>
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_shell is { SidebarOverlays: true })
                {
                    FocusChosenRow();
                }
                else if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Control { IsEffectivelyVisible: true })
                {
                    HandOnTheKeyboard();
                }
            },
            DispatcherPriority.Loaded);

    private void HandOnTheKeyboard()
    {
        // What holds the keyboard still shows, so nothing was lost; handing on again would focus it twice.
        if (_lastFocused is Control { IsFocused: true, IsEffectivelyVisible: true })
        {
            return;
        }

        var showSidebar = this.FindControl<Button>("ShowSidebar")!;

        // At 840 and wider the button's hand-off stays as Q.18 left it.
        var fromButton = _lastFocused == showSidebar && _shell is { IsNarrow: true };

        if (!fromButton && _lastFocused?.GetSelfAndVisualAncestors().Any(visual => visual == this.FindControl<Border>("Sidebar") || visual == this.FindControl<Border>("Rail")) != true)
        {
            return;
        }

        if (_shell is { ShowsRail: true } shell)
        {
            if (fromButton && shell.SelectedRailPlace is { } place)
            {
                GiveFocus(this.FindControl<ListBox>("RailNav")!.ContainerFromItem(place), NavigationMethod.Directional);
            }
            else
            {
                GiveFocus(this.FindControl<Button>("RailGroups"), NavigationMethod.Tab);
            }
        }
        else if (_shell is { ShowsSidebar: true })
        {
            FocusChosenRow();
        }
        else
        {
            GiveFocus(showSidebar, NavigationMethod.Tab);
        }
    }

    // Focusing a control that already holds focus adds its focus adorner a second time and throws.
    private static void GiveFocus(Control? control, NavigationMethod method)
    {
        if (control is { IsFocused: false })
        {
            control.Focus(method);
        }
    }

    /// <summary>The list does not hear a press on its selected row, but the rail's Items still shows every item from a group or a project, as the sidebar's row does.</summary>
    private void OnRailPlaceTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual source && _shell is { } shell
            && source.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault()?.DataContext is NavItem { IsExpandable: true } items
            && Equals(items, shell.SelectedRailPlace))
        {
            shell.SelectedSidebarRow = items;
        }
    }

    /// <summary>A press outside a sidebar lying over the content closes it, and still does what it presses.</summary>
    private void OnPressOutsideSidebar(object? sender, PointerPressedEventArgs e)
    {
        if (_shell is { SidebarOverlays: true } shell && e.Source is Visual source
            && !source.GetSelfAndVisualAncestors().Contains(this.FindControl<Border>("Sidebar")))
        {
            shell.CloseSidebarOverlay();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        InterfaceZoom.Changed -= PlaceSidebar;
        _root?.RemoveHandler(KeyDownEvent, OnDiscardPromptEscape);
        _root = null;

        if (_named is { } window)
        {
            window.Title = ShellViewModel.BareWindowTitle;
            _named = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>The commands a row's context menu offers; the running app's, or a test's.</summary>
    internal AppCommands? Commands { get; init; }

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
        if (_shell is null || sender is not ListBox nav)
        {
            return;
        }

        if (e.Key is not (Key.Left or Key.Right) || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        var selected = _shell.SelectedSidebarRow;

        if (selected is NavItem { IsExpandable: true } or GroupRow { HasChildren: true })
        {
            _shell.Fold(selected, open: e.Key == (FlowDirection == FlowDirection.RightToLeft ? Key.Left : Key.Right));
            e.Handled = true;
            Dispatcher.UIThread.Post(FocusChosenRow, DispatcherPriority.Loaded);
        }
    }

    /// <summary>The sidebar's add menu, at its button.</summary>
    private void OnSidebarAdd(object? sender, RoutedEventArgs e)
    {
        if (Commands is null || sender is not Button button)
        {
            return;
        }

        var menu = AppMenu.Context(Commands.SidebarAdd);
        FlyoutBase.SetAttachedFlyout(button, menu);
        menu.ShowAt(button);
    }

    /// <summary>
    /// A row's context menu, from a right-click, the menu key or Shift+F10, which Avalonia raises as one request,
    /// and from a Control-click on macOS: the row's commands from the menus, at the pointer or at the row.
    /// </summary>
    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (Commands is null || e.Source is not Visual source
            || source.GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault(control => AppCommands.HasContextMenu(control.DataContext)) is not { } anchor)
        {
            return;
        }

        var row = anchor.DataContext!;

        if (Commands.ContextMenu(row) is not { } entries)
        {
            return;
        }

        e.Handled = true;

        // A menu lies over the pane, and a value shown beneath it would stay out of the person's reach to hide.
        RevealedValue.HideShown();

        // Choosing a sidebar row can rebuild the sidebar, taking the row's container with it.
        if (TopLevel.GetTopLevel(anchor) is null)
        {
            anchor = this.FindControl<ListBox>("Nav")!.ContainerFromItem(row) ?? (Control)this;
        }

        var menu = AppMenu.Context(entries);

        // Attached, not set as the row's ContextFlyout, which would open itself at the next request.
        FlyoutBase.SetAttachedFlyout(anchor, menu);
        menu.ShowAt(anchor, showAtPointer: e.TryGetPosition(anchor, out _));
    }

    /// <summary>macOS opens a context menu on a Control-click as on a right-click.</summary>
    private void OnControlClick(object? sender, PointerReleasedEventArgs e)
    {
        if (Commands is { Mac: true } && e.InitialPressMouseButton == MouseButton.Left && e.KeyModifiers == KeyModifiers.Control
            && e.Source is Interactive source)
        {
            source.RaiseEvent(new ContextRequestedEventArgs(e));
            e.Handled = true;
        }
    }

    /// <summary>Shift+F10 asks for the focused row's context menu as the menu key does, which not every platform's Avalonia does.</summary>
    private void OnShiftF10(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F10 || e.KeyModifiers != KeyModifiers.Shift)
        {
            return;
        }

        // The key's release is taken too, so a platform that does raise the request raises it once.
        if (e.RoutedEvent == KeyDownEvent && e.Source is Interactive source)
        {
            source.RaiseEvent(new ContextRequestedEventArgs());
        }

        e.Handled = true;
    }

    /// <summary>Gives the keyboard to the sidebar's chosen row, which is in the footer's list on Trash and Settings.</summary>
    private void FocusChosenRow()
    {
        var chosen = _shell?.SelectedFooter is { } footer
            ? this.FindControl<ListBox>("FooterNav")!.ContainerFromItem(footer)
            : _shell?.SelectedSidebarRow is { } row ? this.FindControl<ListBox>("Nav")!.ContainerFromItem(row) : null;
        GiveFocus(chosen, NavigationMethod.Directional);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_shell is not null)
        {
            _shell.SearchFocusRequested -= OnSearchFocusRequested;
            _shell.PropertyChanged -= OnShellChanged;
            _shell.VaultMoved -= RevealedValue.HideShown;
        }

        _shell = DataContext as ShellViewModel;

        if (_shell is not null)
        {
            _shell.VaultMoved += RevealedValue.HideShown;
            _shell.SearchFocusRequested += OnSearchFocusRequested;
            _shell.PropertyChanged += OnShellChanged;
            _shell.IsNarrow = Bounds.Width > 0 && Bounds.Width < WindowChrome.NarrowBelow;
            _shell.WindowWidth = Bounds.Width;
        }

        PlaceSidebar();
    }

    /// <summary>The import dialog hands the keyboard back to what had it, or to Items' "+", when it closes.</summary>
    private void OnDiscardPromptEscape(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _shell is { HasDiscardPrompt: true } shell)
        {
            shell.KeepDraftCommand.Press();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _shell is { HasHandedPrompt: true } asked)
        {
            asked.KeepVaultCommand.Press();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _shell is { SidebarOverlays: true } overlaid)
        {
            overlaid.CloseSidebarOverlay();
            e.Handled = true;
        }
    }

    private void OnDiscardPromptChanged(bool asking)
    {
        if (asking)
        {
            _focusBeforeDiscardPrompt = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (asking)
                {
                    this.FindControl<Button>("KeepDraft")?.Focus();
                }
                else
                {
                    // The lists keep the row they wrote, and a binding does not republish a value its source never took.
                    this.FindControl<ListBox>("Nav")!.SelectedItem = _shell?.SelectedSidebarRow;
                    this.FindControl<ListBox>("FooterNav")!.SelectedItem = _shell?.SelectedFooter;
                    this.FindControl<ListBox>("RailNav")!.SelectedItem = _shell?.SelectedRailPlace;

                    if (_focusBeforeDiscardPrompt is { IsEffectivelyVisible: true } before)
                    {
                        before.Focus();
                    }
                }
            },
            DispatcherPriority.Loaded);
    }

    private void OnHandedPromptChanged(bool asking)
    {
        if (asking)
        {
            _focusBeforeHandedPrompt = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
            Dispatcher.UIThread.Post(() => this.FindControl<Button>("KeepVault")?.Focus(), DispatcherPriority.Loaded);
        }
        else if (_focusBeforeHandedPrompt is { IsEffectivelyVisible: true } before)
        {
            // At once rather than posted, so the discard question Lock and open can lead to returns focus here too.
            before.Focus();
        }
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.WindowTitle))
        {
            NameTheWindow();
        }

        if (e.PropertyName is nameof(ShellViewModel.ShowsSidebar) or nameof(ShellViewModel.SidebarOverlays) or nameof(ShellViewModel.ShowsRail))
        {
            PlaceSidebar();
            Commands?.SidebarChanged();
            OnSidebarShownChanged();
        }

        if (e.PropertyName == nameof(ShellViewModel.HasDiscardPrompt) && _shell is not null)
        {
            OnDiscardPromptChanged(_shell.HasDiscardPrompt);
        }

        if (e.PropertyName == nameof(ShellViewModel.HasHandedPrompt) && _shell is not null)
        {
            OnHandedPromptChanged(_shell.HasHandedPrompt);
        }

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
                    : this.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.Name is "AddEntry" or "Nav" or "RailGroups" or "ShowSidebar" && control.IsEffectivelyVisible);
                _focusBeforeImport = null;
                back?.Focus();
            },
            DispatcherPriority.Loaded);
    }

    /// <summary>Find from another place has only just asked for Items, so the field is there once it has been drawn.</summary>
    private void OnSearchFocusRequested(object? sender, EventArgs e)
    {
        this.GetVisualDescendants().OfType<EntriesView>().FirstOrDefault()?.MakeSearchReachable();

        Dispatcher.UIThread.Post(
            () =>
            {
                if (this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(box => box.Name == "ShellSearch" && box.IsEffectivelyVisible) is { } search)
                {
                    search.Focus();
                    search.SelectAll();
                }
            },
            DispatcherPriority.Loaded);
    }
}
