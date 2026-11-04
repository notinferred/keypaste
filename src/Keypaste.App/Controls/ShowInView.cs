using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Keypaste.App.Controls;

/// <summary>
/// Scrolls a form into view when it appears, and puts the caret in its first field, so a form opened
/// from a row or button far down a long page is where the click was answered.
/// </summary>
internal sealed class ShowInView : AvaloniaObject
{
    public static readonly AttachedProperty<bool> OnShowProperty =
        AvaloniaProperty.RegisterAttached<ShowInView, Control, bool>("OnShow");

    static ShowInView() => OnShowProperty.Changed.AddClassHandler<Control>(OnShowChanged);

    public static bool GetOnShow(Control control) => control.GetValue(OnShowProperty);

    public static void SetOnShow(Control control, bool value) => control.SetValue(OnShowProperty, value);

    private ShowInView()
    {
    }

    private static void OnShowChanged(Control control, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.NewValue is true)
        {
            control.PropertyChanged += OnPropertyChanged;
            control.AttachedToVisualTree += OnAttached;
        }
        else
        {
            control.PropertyChanged -= OnPropertyChanged;
            control.AttachedToVisualTree -= OnAttached;
        }
    }

    private static void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == Visual.IsVisibleProperty && change.NewValue is true && sender is Control control)
        {
            Reveal(control);
        }
    }

    // A form in a row that a list rebuilt is shown from the start, with no change of visibility to hear.
    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control { IsVisible: true } control)
        {
            Reveal(control);
        }
    }

    private static void Reveal(Control control)
    {
        // After layout, so the form has a size to scroll to and its fields are effectively visible.
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!control.IsEffectivelyVisible)
                {
                    return;
                }

                control.BringIntoView();
                var shown = control.GetVisualDescendants()
                    .OfType<InputElement>()
                    .Where(element => element.IsEffectivelyVisible && element.IsEffectivelyEnabled)
                    .ToList();

                var buttons = shown.Where(element => element is Button).ToList();
                var confirm = buttons.Any(button => button.Classes.Contains("danger"))
                    ? null
                    : buttons.FirstOrDefault(button => !button.Classes.Contains("ghost"));

                // A form with no field takes the keyboard on its confirm, unless that is a danger one, when Return reaches only its quiet button.
                (shown.FirstOrDefault(element => element is TextBox or MaskedInput)
                    ?? confirm
                    ?? buttons.FirstOrDefault(button => button.Classes.Contains("ghost")))
                    ?.Focus();
            },
            DispatcherPriority.Loaded);
    }
}
