using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Keypaste.App.Views;

/// <summary>The one first-launch screen, in front of the unlock screen until the terms of use are agreed to; Continue holds the keyboard, so Return agrees.</summary>
internal sealed partial class FirstLaunchView : UserControl
{
    public FirstLaunchView()
    {
        AvaloniaXamlLoader.Load(this);
        var proceed = this.FindControl<Button>("Continue")!;
        Loaded += (_, _) => Dispatcher.UIThread.Post(() => proceed.Focus(), DispatcherPriority.Background);
    }
}
