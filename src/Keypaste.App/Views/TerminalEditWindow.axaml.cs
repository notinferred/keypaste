using Avalonia.Markup.Xaml;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

internal sealed partial class TerminalEditWindow : PromptWindow
{
    public TerminalEditWindow() => AvaloniaXamlLoader.Load(this);

    internal TerminalEditWindow(TerminalEditViewModel request)
        : this() => DataContext = request;
}
