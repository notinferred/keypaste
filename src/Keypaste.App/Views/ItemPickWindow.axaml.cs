using Avalonia.Markup.Xaml;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

internal sealed partial class ItemPickWindow : PromptWindow
{
    public ItemPickWindow()
    {
        AvaloniaXamlLoader.Load(this);
        FrameCard();
    }

    internal ItemPickWindow(ItemPickViewModel request)
        : this() => DataContext = request;
}
