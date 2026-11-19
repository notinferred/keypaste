using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Keypaste.App.Views;

internal sealed partial class LockedVaultView : UserControl
{
    public LockedVaultView() => AvaloniaXamlLoader.Load(this);
}
