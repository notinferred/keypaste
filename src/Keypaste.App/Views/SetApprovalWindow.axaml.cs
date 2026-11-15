using Avalonia.Markup.Xaml;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

internal sealed partial class SetApprovalWindow : PromptWindow
{
    public SetApprovalWindow()
    {
        AvaloniaXamlLoader.Load(this);
        FrameCard();
    }

    internal SetApprovalWindow(SetApprovalViewModel request)
        : this() => DataContext = request;
}
