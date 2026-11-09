using Avalonia.Markup.Xaml;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

internal sealed partial class SaveValueWindow : PromptWindow
{
    public SaveValueWindow()
    {
        AvaloniaXamlLoader.Load(this);
        FrameCard();
    }

    internal SaveValueWindow(SaveValueViewModel request)
        : this() => DataContext = request;
}
