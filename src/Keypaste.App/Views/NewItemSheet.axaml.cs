using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Keypaste.App.Views;

/// <summary>New item's sheet over the window: its grid takes the keyboard on its first tile.</summary>
internal sealed partial class NewItemSheet : UserControl
{
    public NewItemSheet()
    {
        AvaloniaXamlLoader.Load(this);
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
}
