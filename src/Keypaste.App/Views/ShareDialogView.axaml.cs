using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Keypaste.App.Views;

/// <summary>Share…, from an item's ⋯ menu: the form that makes one link, over the screen it was opened from.</summary>
internal sealed partial class ShareDialogView : UserControl
{
    public ShareDialogView() => AvaloniaXamlLoader.Load(this);
}
