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
        }
        else
        {
            control.PropertyChanged -= OnPropertyChanged;
        }
    }

    private static void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property != Visual.IsVisibleProperty || change.NewValue is not true || sender is not Control control)
        {
            return;
        }

        // After layout, so the form has a size to scroll to and its fields are effectively visible.
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!control.IsEffectivelyVisible)
                {
                    return;
                }

                control.BringIntoView();
                control.GetVisualDescendants()
                    .OfType<InputElement>()
                    .FirstOrDefault(field => field is TextBox or MaskedInput && field.IsEffectivelyVisible && field.IsEffectivelyEnabled)
                    ?.Focus();
            },
            DispatcherPriority.Loaded);
    }
}
