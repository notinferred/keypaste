using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Keypaste.App.Views;

/// <summary>Settings › Advanced's diagnostics: the vault, keypaste's home and the version in use.</summary>
internal sealed partial class DiagnosticsView : UserControl
{
    public DiagnosticsView() => AvaloniaXamlLoader.Load(this);
}
