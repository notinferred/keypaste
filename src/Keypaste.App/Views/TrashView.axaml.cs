using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Keypaste.App.Views;

/// <summary>
/// The Trash screen.
/// </summary>
/// <remarks>
/// No code behind the XAML beyond loading it. There is no secret control here: a trash row holds a
/// name, where it came from and when it went, so nothing on this screen needs masking or a hold.
/// </remarks>
internal sealed partial class TrashView : UserControl
{
    public TrashView() => AvaloniaXamlLoader.Load(this);
}
