using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Keypaste.App.Views;

/// <summary>Settings › Advanced: a row for each of the activity log, share links, scoped tokens and diagnostics.</summary>
internal sealed partial class AdvancedView : UserControl
{
    public AdvancedView() => AvaloniaXamlLoader.Load(this);
}
