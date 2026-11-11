using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Keypaste.App.Views;

/// <summary>The generator's panel, which takes the keyboard's place on its first control when it opens.</summary>
internal sealed partial class GeneratorPanel : UserControl
{
    public GeneratorPanel() => AvaloniaXamlLoader.Load(this);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property.Name == nameof(IsEffectivelyVisible) && change.GetNewValue<bool>())
        {
            Dispatcher.UIThread.Post(
                () => this.GetVisualDescendants().OfType<InputElement>().FirstOrDefault(element => element.Focusable && element.IsEffectivelyEnabled)?.Focus(NavigationMethod.Tab),
                DispatcherPriority.Loaded);
        }
    }
}
