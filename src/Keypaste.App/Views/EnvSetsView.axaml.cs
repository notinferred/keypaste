using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Keypaste.App.Views;

/// <summary>
/// The Env Sets screen.
/// </summary>
/// <remarks>
/// No code behind the XAML beyond loading it. The reveal lives in
/// <see cref="Controls.RevealedValue"/>, which hides itself, and the "one at a
/// time" rule also in <see cref="ViewModels.EnvProjectViewModel"/>, where it is assertable without a display.
/// </remarks>
internal sealed partial class EnvSetsView : UserControl
{
    public EnvSetsView() => AvaloniaXamlLoader.Load(this);
}
