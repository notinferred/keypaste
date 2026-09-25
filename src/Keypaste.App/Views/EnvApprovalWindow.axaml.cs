using Avalonia.Markup.Xaml;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

internal sealed partial class EnvApprovalWindow : PromptWindow
{
    public EnvApprovalWindow() => AvaloniaXamlLoader.Load(this);

    internal EnvApprovalWindow(EnvApprovalViewModel request)
        : this() => DataContext = request;
}
