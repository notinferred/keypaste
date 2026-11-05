using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.VisualTree;
using Keypaste.App.Controls;

namespace Keypaste.App.Tests.Rendering;

/// <summary>
/// What a drawn window cuts off: a control or a line of text past its window or a clipping edge with no scrolling that reaches it, a single
/// line of text wider than the box it was given without an ellipsis to say so, and masked text drawn under 14px.
/// </summary>
internal static class ReachableControls
{
    private const double _slack = 1;
    private const double _smallestMasked = 14;

    // A monospace dot's advance as a share of its size, narrow enough to leave a note beside the dots alone.
    private const double _dotAdvance = 0.6;

    internal static IEnumerable<string> Problems(TopLevel window)
    {
        foreach (var control in window.GetVisualDescendants().OfType<Control>().Where(Counts))
        {
            if (Cut(control, window) is { } cut)
            {
                yield return $"{Describe(control)} {cut}";
            }

            if (control is TextBlock { TextWrapping: TextWrapping.NoWrap, TextTrimming: var trimming } line
                && trimming == TextTrimming.None
                && line.TextLayout.Width > line.Bounds.Width - line.Padding.Left - line.Padding.Right + _slack)
            {
                yield return $"{Describe(control)} text is {line.TextLayout.Width:F0} wide in {line.Bounds.Width:F0}";
            }

            if (control is RevealedValue value && Overprinted(value, window) is { } under)
            {
                yield return $"{Describe(control)} at {InWindow(value, window)} overprints {Describe(under)} at {InWindow(under, window)}";
            }

            if (MaskedSize(control) is { } size && size * Scale(control, window) < _smallestMasked - 0.01)
            {
                yield return $"{Describe(control)} draws masked text at {size * Scale(control, window):F1}px";
            }
        }
    }

    /// <summary>Text in the same cell that a drawn value lies over, leaving out the text kept under it to hold the cell's width, which is not painted.</summary>
    private static TextBlock? Overprinted(RevealedValue value, TopLevel window)
    {
        var box = InWindow(value, window);
        var scale = Scale(value, window);
        var dots = new Rect(box.Left, box.Top, Math.Min(box.Width, value.MaskedLength * value.FontSize * _dotAdvance * scale), box.Height);

        return value.FindAncestorOfType<Panel>()?.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(block => block.IsEffectivelyVisible && !block.Classes.Contains("under") && block.Text is { Length: > 0 } && InWindow(block, window).Deflate(_slack).Intersects(dots.Deflate(_slack)));
    }

    private static bool Counts(Control control) =>
        control.IsEffectivelyVisible
        && control.Bounds is { Width: > 0, Height: > 0 }
        && !control.Classes.Contains("live")
        && control is Button or TextBox or MaskedInput or RevealedValue or ComboBox or ToggleSwitch or ListBoxItem or Slider or TextBlock { Text.Length: > 0 };

    private static double? MaskedSize(Control control) => control switch
    {
        MaskedInput input => input.FontSize,
        RevealedValue value => value.FontSize,
        _ => null,
    };

    private static double Scale(Control control, TopLevel window) =>
        control.TransformToVisual(window) is { } transform ? transform.M11 : 1;

    private static Rect InWindow(Visual visual, TopLevel window) =>
        visual.TransformToVisual(window) is { } transform ? new Rect(visual.Bounds.Size).TransformToAABB(transform) : default;

    /// <summary>Follows the clipping edges outward: inside a scroller what must be reached is the scroller, along the axes it scrolls.</summary>
    private static string? Cut(Control control, TopLevel window)
    {
        var rect = InWindow(control, window);

        foreach (var ancestor in control.GetVisualAncestors().OfType<Control>().TakeWhile(ancestor => ancestor != window))
        {
            var edge = InWindow(ancestor, window);

            if (ancestor is ScrollContentPresenter scroller)
            {
                if ((!scroller.CanHorizontallyScroll && Past(rect.Left, rect.Right, edge.Left, edge.Right))
                    || (!scroller.CanVerticallyScroll && Past(rect.Top, rect.Bottom, edge.Top, edge.Bottom)))
                {
                    return $"at {rect} is past a scroller at {edge} along an axis it does not scroll";
                }

                rect = edge;
            }
            else if (ancestor.ClipToBounds
                && (Past(rect.Left, rect.Right, edge.Left, edge.Right) || Past(rect.Top, rect.Bottom, edge.Top, edge.Bottom)))
            {
                return $"at {rect} is cut by {ancestor.GetType().Name} {ancestor.Name} at {edge}";
            }
        }

        var size = window.ClientSize;
        return Past(rect.Left, rect.Right, 0, size.Width) || Past(rect.Top, rect.Bottom, 0, size.Height)
            ? $"at {rect} is outside the window {size}"
            : null;
    }

    private static bool Past(double from, double to, double start, double end) =>
        from < start - _slack || to > end + _slack;

    private static string Describe(Control control)
    {
        var text = control is TextBlock { Text: { } words } ? $" \"{(words.Length > 30 ? words[..30] : words)}\"" : string.Empty;
        return $"{control.GetType().Name} {control.Name}{text}";
    }
}
