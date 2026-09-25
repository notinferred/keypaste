using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Keypaste.App.Views;

internal sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        var strip = this.FindControl<Border>("DragStrip")!;
        var root = this.FindControl<ContentControl>("Root")!;

        // The shell's own titlebar is the drag area while it shows; a strip over it would take its
        // clicks.
        root.PropertyChanged += (_, change) =>
        {
            if (change.Property == ContentControl.ContentProperty)
            {
                strip.IsVisible = change.NewValue is not ShellView;
            }
        };
    }
}
