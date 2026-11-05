using Avalonia.Markup.Xaml;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

internal sealed partial class PairingWindow : PromptWindow
{
    public PairingWindow()
    {
        AvaloniaXamlLoader.Load(this);
        FrameCard();
    }

    internal PairingWindow(PairingViewModel request)
        : this() => DataContext = request;
}
