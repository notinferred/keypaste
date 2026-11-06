using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>Share…, from an item's ⋯ menu: the form that makes one link, over the screen it was opened from.</summary>
internal sealed partial class ShareDialogView : UserControl
{
    public ShareDialogView() => AvaloniaXamlLoader.Load(this);

    // The keyboard goes into the dialog as it opens, so that Return and Escape reach it.
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is SharingViewModel)
        {
            Dispatcher.UIThread.Post(() => this.FindControl<TextBox>("Recipient")?.Focus(), DispatcherPriority.Loaded);
        }
    }
}
