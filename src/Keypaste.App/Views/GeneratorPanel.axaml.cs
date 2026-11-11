using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>The generator's panel, which takes the keyboard's place on its first control when it opens, and answers Escape and Return before the form around it.</summary>
internal sealed partial class GeneratorPanel : UserControl
{
    public GeneratorPanel()
    {
        AvaloniaXamlLoader.Load(this);
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.KeyModifiers != KeyModifiers.None || DataContext is not GeneratePasswordViewModel panel)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            panel.CancelCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && e.Source is TextBox { Name: "GeneratedFieldName" })
        {
            // Return in the field's name adds the field it names rather than applying the value elsewhere.
            if (panel.AddFieldCommand.CanExecute(null))
            {
                panel.AddFieldCommand.Execute(null);
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Enter && panel.Applies && e.Source is not Button)
        {
            // Return applies, so the form around the panel never confirms with the value still in it.
            if (panel.ApplyCommand.CanExecute(null))
            {
                panel.ApplyCommand.Execute(null);
            }

            e.Handled = true;
        }
    }

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
