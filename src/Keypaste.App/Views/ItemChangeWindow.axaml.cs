using Avalonia.Markup.Xaml;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

internal sealed partial class ItemChangeWindow : PromptWindow
{
    public ItemChangeWindow()
    {
        AvaloniaXamlLoader.Load(this);
        FrameCard();
    }

    internal ItemChangeWindow(ItemChangeViewModel request)
        : this() => DataContext = request;
}
