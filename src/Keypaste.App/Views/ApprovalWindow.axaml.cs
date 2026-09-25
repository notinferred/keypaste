using Avalonia.Markup.Xaml;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

internal sealed partial class ApprovalWindow : PromptWindow
{
    public ApprovalWindow() => AvaloniaXamlLoader.Load(this);

    internal ApprovalWindow(ApprovalViewModel request)
        : this() => DataContext = request;
}
