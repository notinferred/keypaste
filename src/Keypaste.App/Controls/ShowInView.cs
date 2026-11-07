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

    /// <summary>Scrolls a page's message into view when it appears, leaving the keyboard where it is.</summary>
    public static readonly AttachedProperty<bool> ScrollsProperty =
        AvaloniaProperty.RegisterAttached<ShowInView, Control, bool>("Scrolls");

    /// <summary>A form has just appeared, for a menu whose closing takes the keyboard back.</summary>
    internal static event Action<Control>? Shown;

    static ShowInView()
    {
        OnShowProperty.Changed.AddClassHandler<Control>(OnShowChanged);
        ScrollsProperty.Changed.AddClassHandler<Control>(OnScrollsChanged);
    }

    public static bool GetOnShow(Control control) => control.GetValue(OnShowProperty);

    public static void SetOnShow(Control control, bool value) => control.SetValue(OnShowProperty, value);

    public static bool GetScrolls(Control control) => control.GetValue(ScrollsProperty);

    public static void SetScrolls(Control control, bool value) => control.SetValue(ScrollsProperty, value);

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

    private static void OnScrollsChanged(Control control, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.NewValue is true)
        {
            control.PropertyChanged += ScrollWhenShown;
        }
        else
        {
            control.PropertyChanged -= ScrollWhenShown;
        }
    }

    private static void ScrollWhenShown(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == Visual.IsVisibleProperty && change.NewValue is true && sender is Control control)
        {
            Dispatcher.UIThread.Post(() => BringInWithWhatItFollows(control), DispatcherPriority.Loaded);
        }
    }

    // A problem line sits right after the field it concerns, and reading it with that field off screen says nothing.
    private static void BringInWithWhatItFollows(Control control)
    {
        if (control.GetVisualParent() is not Control parent)
        {
            return;
        }

        var siblings = parent.GetVisualChildren().OfType<Control>().Where(sibling => sibling.IsEffectivelyVisible).ToList();
        var before = siblings.IndexOf(control) - 1;
        parent.BringIntoView(before < 0 ? control.Bounds : control.Bounds.Union(siblings[before].Bounds));
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
        Shown?.Invoke(control);
        TakeTheKeyboard(control);
    }

    /// <summary>Scrolls <paramref name="control"/> into view and gives the keyboard to its first field.</summary>
    internal static void TakeTheKeyboard(Control control)
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
                    ?? shown.FirstOrDefault(element => element is ComboBox)
                    ?? confirm
                    ?? buttons.FirstOrDefault(button => button.Classes.Contains("ghost")))
                    ?.Focus();
            },
            DispatcherPriority.Loaded);
    }
}
