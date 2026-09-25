using Avalonia;
using Avalonia.Controls;

namespace Keypaste.App.Controls;

/// <summary>
/// Equal-width columns, as many as fit at <see cref="MinItemWidth"/>: the design's
/// <c>repeat(auto-fit, minmax(220px, 1fr))</c>. Hidden children take no column.
/// </summary>
internal sealed class AutoFitGrid : Panel
{
    internal static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<AutoFitGrid, double>(nameof(MinItemWidth), 220d);

    internal static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<AutoFitGrid, double>(nameof(Spacing), 12d);

    static AutoFitGrid() => AffectsMeasure<AutoFitGrid>(MinItemWidthProperty, SpacingProperty);

    internal double MinItemWidth
    {
        get => GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    internal double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var items = Visible();

        if (items.Count == 0)
        {
            return default;
        }

        var width = double.IsInfinity(availableSize.Width) ? MinItemWidth * items.Count : availableSize.Width;
        var (columns, itemWidth) = Columns(width, items.Count);
        var height = 0d;

        for (var start = 0; start < items.Count; start += columns)
        {
            var row = 0d;

            foreach (var item in items.Skip(start).Take(columns))
            {
                item.Measure(new Size(itemWidth, double.PositiveInfinity));
                row = Math.Max(row, item.DesiredSize.Height);
            }

            height += row + (start > 0 ? Spacing : 0);
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var items = Visible();
        var (columns, itemWidth) = Columns(finalSize.Width, Math.Max(1, items.Count));
        var top = 0d;

        for (var start = 0; start < items.Count; start += columns)
        {
            var row = items.Skip(start).Take(columns).ToList();
            var height = row.Max(item => item.DesiredSize.Height);

            for (var i = 0; i < row.Count; i++)
            {
                row[i].Arrange(new Rect(i * (itemWidth + Spacing), top, itemWidth, height));
            }

            top += height + Spacing;
        }

        return finalSize;
    }

    private List<Control> Visible() => [.. Children.Where(child => child.IsVisible)];

    private (int Columns, double ItemWidth) Columns(double width, int count)
    {
        var fit = (int)Math.Floor((width + Spacing) / (MinItemWidth + Spacing));
        var columns = Math.Clamp(fit, 1, count);

        return (columns, Math.Max(0, (width - (Spacing * (columns - 1))) / columns));
    }
}
