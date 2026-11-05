using Avalonia.Markup.Xaml;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

internal sealed partial class RunApprovalWindow : PromptWindow
{
    public RunApprovalWindow()
    {
        AvaloniaXamlLoader.Load(this);
        FrameCard();
    }

    internal RunApprovalWindow(RunApprovalViewModel request)
        : this() => DataContext = request;
}
