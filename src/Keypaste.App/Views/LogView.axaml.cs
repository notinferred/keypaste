using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Keypaste.App.Views;

/// <summary>The Activity screen; everything it reads and does is <see cref="ViewModels.LogViewModel"/>'s.</summary>
internal sealed partial class LogView : UserControl
{
    public LogView() => AvaloniaXamlLoader.Load(this);
}
