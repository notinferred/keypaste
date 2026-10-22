using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.ViewModels;

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

    /// <summary>The row whose drawn cell a hold shows, for a button outside that cell's template; <see cref="Target"/> wins when both are set.</summary>
    internal static readonly StyledProperty<IRevealSource?> TargetSourceProperty =
        AvaloniaProperty.Register<RevealButton, IRevealSource?>(nameof(TargetSource));

    internal RevealedValue? Target
    {
        get => GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    internal IRevealSource? TargetSource
    {
        get => GetValue(TargetSourceProperty);
        set => SetValue(TargetSourceProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(Button);

    private RevealedValue? Cell => Target ?? CellAsking(TargetSource);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Cell?.BeginReveal();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        Cell?.EndReveal();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        Cell?.EndReveal();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.Key is Key.Space or Key.Enter)
        {
            Cell?.BeginReveal();
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
            Cell?.EndReveal();
            e.Handled = true;
            return;
        }

        base.OnKeyUp(e);
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        Cell?.EndReveal();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Cell?.EndReveal();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>The nearest drawn cell asking <paramref name="source"/>, looked for from the button's closest ancestor outward.</summary>
    private RevealedValue? CellAsking(IRevealSource? source)
    {
        if (source is null)
        {
            return null;
        }

        foreach (var ancestor in this.GetVisualAncestors())
        {
            if (ancestor.GetVisualDescendants().OfType<RevealedValue>().FirstOrDefault(cell => ReferenceEquals(cell.Source, source)) is { } cell)
            {
                return cell;
            }
        }

        return null;
    }
}
