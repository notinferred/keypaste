using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Keypaste.App.Views;

/// <summary>Settings › Advanced's scoped tokens: the vault's tokens, minting one and revoking one.</summary>
internal sealed partial class ScopedTokensView : UserControl
{
    public ScopedTokensView() => AvaloniaXamlLoader.Load(this);
}
