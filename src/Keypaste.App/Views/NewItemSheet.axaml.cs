using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Keypaste.App.Views;

/// <summary>New item's sheet over the window: its grid takes the keyboard on its first tile, and Add field…'s menu opens beside its button.</summary>
internal sealed partial class NewItemSheet : UserControl
{
    private const double _menuGap = 4;

    public NewItemSheet()
    {
        AvaloniaXamlLoader.Load(this);
        ToggleMenus.Attach(this, ("AddFieldMenu", "AddFieldMenuPanel"));

        this.FindControl<ToggleButton>("AddFieldMenu")!.IsCheckedChanged += (sender, _) =>
        {
            if (sender is ToggleButton { IsChecked: true } toggle)
            {
                toggle.BringIntoView();
                Dispatcher.UIThread.Post(PlaceFieldMenu, DispatcherPriority.Loaded);
            }
        };
        this.FindControl<Border>("TemplateSheet")!.PropertyChanged += (sender, change) =>
        {
            if (change.Property == IsVisibleProperty && change.NewValue is true)
            {
                Dispatcher.UIThread.Post(FocusFirstTile, DispatcherPriority.Loaded);
            }
        };

        // A title the template preset is taken whole when the keyboard reaches it, so typing replaces it.
        this.FindControl<TextBox>("NewItemTitle")!.GotFocus += (sender, e) =>
        {
            if (e.NavigationMethod != NavigationMethod.Pointer && sender is TextBox title)
            {
                title.SelectAll();
            }
        };
    }

    private void FocusFirstTile()
    {
        if (this.FindControl<ItemsControl>("TemplateTiles")?.GetVisualDescendants().OfType<Button>().FirstOrDefault(tile => tile.IsEffectivelyVisible) is { } first)
        {
            first.Focus(NavigationMethod.Tab);
        }
    }

    // Under the button where the form has room below it, otherwise above it, and never past the sheet's edges.
    private void PlaceFieldMenu()
    {
        var toggle = this.FindControl<ToggleButton>("AddFieldMenu")!;
        var layer = this.FindControl<Canvas>("FieldMenuLayer")!;
        var menu = this.FindControl<Border>("AddFieldMenuPanel")!;

        if (toggle.IsChecked != true || toggle.TranslatePoint(default, layer) is not { } at)
        {
            return;
        }

        menu.Measure(Size.Infinity);
        var height = menu.DesiredSize.Height;
        var below = at.Y + toggle.Bounds.Height + _menuGap;
        var top = below + height <= layer.Bounds.Height ? below : at.Y - _menuGap - height;
        Canvas.SetLeft(menu, at.X);
        Canvas.SetTop(menu, Math.Clamp(top, 0, Math.Max(0, layer.Bounds.Height - height)));
    }
}
