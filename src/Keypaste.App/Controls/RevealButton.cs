using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Keypaste.App.Controls;

/// <summary>
/// The Reveal button beside a masked value: holding it shows the value in <see cref="Target"/>, and
/// letting go hides it again.
/// </summary>
/// <remarks>
/// A hold rather than a toggle, for the rule the cell itself follows (D-0300): the value is on screen
/// only while somebody is deliberately holding it. The button asks the cell to reveal and never sees
/// the value, and its label does not change while held, so the accessibility surface stays as it was
/// (D-0232). Space and Enter hold it from the keyboard.
/// </remarks>
internal sealed class RevealButton : Button
{
    /// <summary>The cell whose value a hold shows.</summary>
    internal static readonly StyledProperty<RevealedValue?> TargetProperty =
        AvaloniaProperty.Register<RevealButton, RevealedValue?>(nameof(Target));

    internal RevealedValue? Target
    {
        get => GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(Button);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Target?.BeginReveal();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        Target?.EndReveal();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        Target?.EndReveal();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.Key is Key.Space or Key.Enter)
        {
            Target?.BeginReveal();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.Key is Key.Space or Key.Enter)
        {
            Target?.EndReveal();
            e.Handled = true;
            return;
        }

        base.OnKeyUp(e);
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        Target?.EndReveal();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Target?.EndReveal();
        base.OnDetachedFromVisualTree(e);
    }
}
