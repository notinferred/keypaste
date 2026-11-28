using Avalonia.Markup.Xaml;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

internal sealed partial class FrameTrustWindow : PromptWindow
{
    public FrameTrustWindow()
    {
        AvaloniaXamlLoader.Load(this);
        FrameCard();
    }

    internal FrameTrustWindow(FrameTrustViewModel request)
        : this() => DataContext = request;
}
