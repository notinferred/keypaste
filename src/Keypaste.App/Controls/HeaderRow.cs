using Avalonia;
using Avalonia.Controls;

namespace Keypaste.App.Controls;

/// <summary>
/// A title on the left and its actions on the right, or the actions on a line of their own under the
/// title, still at the right, when the two do not fit side by side.
/// </summary>
/// <remarks>
/// The first child is the title and the second the actions; any others are ignored. The design's
/// header wraps its actions below a long key instead of squeezing the key to a few characters: a
/// wrapping flex row whose gap is <see cref="Spacing"/> both ways.
/// </remarks>
internal sealed class HeaderRow : Panel
{
    internal static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<HeaderRow, double>(nameof(Spacing), 16d);

    static HeaderRow() => AffectsMeasure<HeaderRow>(SpacingProperty);

    internal double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Parts() is not { } parts)
        {
            return default;
        }

        var (title, actions) = parts;

        actions.Measure(availableSize);
        var beside = availableSize.Width - actions.DesiredSize.Width - Spacing;

        title.Measure(availableSize.WithWidth(Math.Max(0, availableSize.Width)));

        if (title.DesiredSize.Width <= beside)
        {
            return new Size(
                title.DesiredSize.Width + Spacing + actions.DesiredSize.Width,
                Math.Max(title.DesiredSize.Height, actions.DesiredSize.Height));
        }

        return new Size(
            Math.Max(title.DesiredSize.Width, actions.DesiredSize.Width),
            title.DesiredSize.Height + Spacing + actions.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Parts() is not { } parts)
        {
            return finalSize;
        }

        var (title, actions) = parts;

        var beside = finalSize.Width - actions.DesiredSize.Width - Spacing;

        if (title.DesiredSize.Width <= beside)
        {
            title.Arrange(new Rect(0, 0, beside, finalSize.Height));
            actions.Arrange(new Rect(finalSize.Width - actions.DesiredSize.Width, 0, actions.DesiredSize.Width, actions.DesiredSize.Height));
        }
        else
        {
            title.Arrange(new Rect(0, 0, finalSize.Width, title.DesiredSize.Height));
            actions.Arrange(new Rect(
                Math.Max(0, finalSize.Width - actions.DesiredSize.Width),
                title.DesiredSize.Height + Spacing,
                actions.DesiredSize.Width,
                actions.DesiredSize.Height));
        }

        return finalSize;
    }

    private (Control Title, Control Actions)? Parts() =>
        Children.Count >= 2 ? (Children[0], Children[1]) : null;
}
