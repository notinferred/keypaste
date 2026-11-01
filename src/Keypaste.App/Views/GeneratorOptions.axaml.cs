using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Keypaste.App.Views;

/// <summary>A generator's choices, the same in every form that generates a value and in Generate password.</summary>
internal sealed partial class GeneratorOptions : UserControl
{
    public GeneratorOptions() => AvaloniaXamlLoader.Load(this);
}
