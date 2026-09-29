using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Keypaste.App.Views;

/// <summary>
/// Settings › Advanced's share links: what each link carries, to whom and on what terms, and whether it
/// still opens. Links are made from an item's Share…, in <see cref="ShareDialogView"/>.
/// </summary>
internal sealed partial class SharingView : UserControl
{
    public SharingView() => AvaloniaXamlLoader.Load(this);
}
