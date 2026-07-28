using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Keypaste.App.Views;

internal sealed partial class ShellView : UserControl
{
    public ShellView() => AvaloniaXamlLoader.Load(this);
}
