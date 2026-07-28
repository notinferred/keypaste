using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Keypaste.App.Views;

/// <summary>
/// The activity view.
/// </summary>
/// <remarks>
/// No code behind the XAML beyond loading it. Everything this screen does — reading the log,
/// checking the chain, deciding what to say about a file that is not there — belongs to
/// <see cref="ViewModels.LogViewModel"/>, which is where it can be tested without a display.
/// </remarks>
internal sealed partial class LogView : UserControl
{
    public LogView() => AvaloniaXamlLoader.Load(this);
}
