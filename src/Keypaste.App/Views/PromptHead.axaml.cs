using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Keypaste.App.Views;

/// <summary>A claim card's header: keypaste's mark and name, then who is asking and whether keypaste verified it.</summary>
internal sealed partial class PromptHead : UserControl
{
    public PromptHead() => AvaloniaXamlLoader.Load(this);
}
