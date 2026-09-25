using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace Keypaste.App.Views;

/// <summary>
/// The Entries screen.
/// </summary>
/// <remarks>
/// Everything this screen does — searching, building the group tree, reading and writing the vault —
/// belongs to <see cref="ViewModels.EntriesViewModel"/>, which names no Avalonia type and is
/// therefore assertable with no application and no display. The one thing here is closing the pane's
/// ⋯ menu, which is a matter of where the pointer went rather than of anything in the vault.
/// </remarks>
internal sealed partial class EntriesView : UserControl
{
    /// <summary>How wide the screen must be for the group tree to start open: room for it, the list and a pane that shows a long password whole.</summary>
    internal const double TreeOpensAt = 960d;

    private readonly ToggleButton _groups;
    private bool _placingTree;
    private bool _treeChosen;

    public EntriesView()
    {
        AvaloniaXamlLoader.Load(this);

        _groups = this.FindControl<ToggleButton>("GroupsToggle")!;
        _groups.IsCheckedChanged += (_, _) => _treeChosen |= !_placingTree;

        AddHandler(Button.ClickEvent, OnClick, RoutingStrategies.Bubble);
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>The tree starts open on a wide screen and folded on a narrow one, until somebody chooses.</summary>
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        if (_treeChosen)
        {
            return;
        }

        _placingTree = true;
        _groups.IsChecked = e.NewSize.Width >= TreeOpensAt;
        _placingTree = false;
    }

    /// <summary>A choice in the menu closes it.</summary>
    private void OnClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button { Classes: var classes } && classes.Contains("menu-item"))
        {
            CloseMenu();
        }
    }

    /// <summary>A press anywhere but the menu or its button closes it.</summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Menu() is not { IsChecked: true } toggle || e.Source is not Visual source)
        {
            return;
        }

        var inside = source.GetSelfAndVisualAncestors().Any(visual =>
            ReferenceEquals(visual, toggle) || visual is Control { Name: "EntryMenuPanel" });

        if (!inside)
        {
            toggle.IsChecked = false;
        }
    }

    private void CloseMenu()
    {
        if (Menu() is { } toggle)
        {
            toggle.IsChecked = false;
        }
    }

    private ToggleButton? Menu() =>
        this.GetVisualDescendants().OfType<ToggleButton>().FirstOrDefault(toggle => toggle.Name == "EntryMenu");
}
