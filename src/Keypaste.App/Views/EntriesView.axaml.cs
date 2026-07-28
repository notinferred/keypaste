using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Keypaste.App.Views;

/// <summary>
/// The Entries screen.
/// </summary>
/// <remarks>
/// No code behind the XAML beyond loading it. Everything this screen does — searching, building the
/// group tree, reading and writing the vault — belongs to <see cref="ViewModels.EntriesViewModel"/>,
/// which names no Avalonia type and is therefore assertable with no application and no display.
/// </remarks>
internal sealed partial class EntriesView : UserControl
{
    public EntriesView() => AvaloniaXamlLoader.Load(this);
}
