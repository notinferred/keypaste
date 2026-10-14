using Avalonia.Markup.Xaml;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

internal sealed partial class AccessChangeWindow : PromptWindow
{
    public AccessChangeWindow() => AvaloniaXamlLoader.Load(this);

    internal AccessChangeWindow(AccessChangeViewModel request)
        : this() => DataContext = request;
}
