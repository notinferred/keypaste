using Avalonia.Markup.Xaml;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

internal sealed partial class FillWindow : PromptWindow
{
    public FillWindow()
    {
        AvaloniaXamlLoader.Load(this);
        FrameCard();
    }

    internal FillWindow(FillViewModel request)
        : this() => DataContext = request;
}
