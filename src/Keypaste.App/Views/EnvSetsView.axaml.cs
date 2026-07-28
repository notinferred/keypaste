using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Keypaste.App.Views;

/// <summary>
/// The Env Sets screen.
/// </summary>
/// <remarks>
/// No code behind the XAML beyond loading it. The reveal gesture lives in
/// <see cref="Controls.RevealedValue"/>, which handles its own press and release so the value is on
/// screen for exactly as long as a finger is down — and the "one at a time" rule lives in
/// <see cref="ViewModels.EnvProjectViewModel"/>, where it is assertable without a display.
/// </remarks>
internal sealed partial class EnvSetsView : UserControl
{
    public EnvSetsView() => AvaloniaXamlLoader.Load(this);
}
