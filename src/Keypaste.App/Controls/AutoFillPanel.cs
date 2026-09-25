using Avalonia;
using Avalonia.Controls;

namespace Keypaste.App.Controls;

/// <summary>
/// CSS's <c>repeat(auto-fill, minmax(MinColumnWidth, 1fr))</c>: as many equal columns as fit at
/// <see cref="MinColumnWidth"/>, children placed row by row, each row as tall as its tallest child.
/// </summary>
internal sealed class AutoFillPanel : Panel
{
    internal static readonly StyledProperty<double> MinColumnWidthProperty =
        AvaloniaProperty.Register<AutoFillPanel, double>(nameof(MinColumnWidth), 240d);

    internal static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<AutoFillPanel, double>(nameof(Spacing), 12d);

    static AutoFillPanel() => AffectsMeasure<AutoFillPanel>(MinColumnWidthProperty, SpacingProperty);

    internal double MinColumnWidth
    {
        get => GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    internal double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? MinColumnWidth : availableSize.Width;
        var (columns, columnWidth) = Columns(width);
        var height = 0d;

        foreach (var row in Rows(columns))
        {
            var tallest = 0d;

            foreach (var child in row)
            {
                child.Measure(new Size(columnWidth, double.PositiveInfinity));
                tallest = Math.Max(tallest, child.DesiredSize.Height);
            }

            height += (height > 0 ? Spacing : 0) + tallest;
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (columns, columnWidth) = Columns(finalSize.Width);
        var top = 0d;

        foreach (var row in Rows(columns))
        {
            var tallest = row.Max(child => child.DesiredSize.Height);

            for (var i = 0; i < row.Count; i++)
            {
                row[i].Arrange(new Rect(i * (columnWidth + Spacing), top, columnWidth, tallest));
            }

            top += tallest + Spacing;
        }

        return finalSize;
    }

    private (int Columns, double Width) Columns(double width)
    {
        var columns = Math.Max(1, (int)Math.Floor((width + Spacing) / (MinColumnWidth + Spacing)));
        return (columns, Math.Max(0, (width - (Spacing * (columns - 1))) / columns));
    }

    private IEnumerable<List<Control>> Rows(int columns) =>
        Children.Where(child => child.IsVisible).Chunk(columns).Select(chunk => chunk.ToList());
}
