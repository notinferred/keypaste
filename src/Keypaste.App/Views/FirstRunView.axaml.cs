using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Keypaste.App.Views;

/// <summary>The first run's terms of use and crash-report choice, shown before the welcome.</summary>
internal sealed partial class FirstRunView : UserControl
{
    public FirstRunView()
    {
        AvaloniaXamlLoader.Load(this);

        // Launch, then Enter reads on to the choice, whose two buttons take no default.
        var proceed = this.FindControl<Button>("Continue")!;
        Loaded += (_, _) => Dispatcher.UIThread.Post(() => proceed.Focus(), DispatcherPriority.Background);
    }
}
