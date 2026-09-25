using Avalonia;
using Avalonia.Controls;

namespace Keypaste.App.Controls;

/// <summary>
/// Equal columns that stack when there is no room for them, as the design's
/// <c>grid-template-columns: repeat(auto-fit, minmax(MinColumnWidth, 1fr))</c> with a <see cref="Spacing"/> gap.
/// </summary>
internal sealed class AutoFitColumns : Panel
{
    internal static readonly StyledProperty<double> MinColumnWidthProperty =
        AvaloniaProperty.Register<AutoFitColumns, double>(nameof(MinColumnWidth), 320);

    internal static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<AutoFitColumns, double>(nameof(Spacing), 12);

    static AutoFitColumns() => AffectsMeasure<AutoFitColumns>(MinColumnWidthProperty, SpacingProperty);

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
        var shown = Shown();

        if (shown.Count == 0)
        {
            return default;
        }

        var columns = double.IsInfinity(availableSize.Width) ? shown.Count : Columns(availableSize.Width, shown.Count);
        var width = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : ColumnWidth(availableSize.Width, columns);
        var rows = new double[(shown.Count + columns - 1) / columns];
        var widest = 0d;

        for (var i = 0; i < shown.Count; i++)
        {
            shown[i].Measure(new Size(width, double.PositiveInfinity));
            rows[i / columns] = Math.Max(rows[i / columns], shown[i].DesiredSize.Height);
            widest = Math.Max(widest, shown[i].DesiredSize.Width);
        }

        var height = rows.Sum() + (Spacing * (rows.Length - 1));

        return double.IsInfinity(availableSize.Width)
            ? new Size((widest * columns) + (Spacing * (columns - 1)), height)
            : new Size(availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var shown = Shown();

        if (shown.Count == 0)
        {
            return finalSize;
        }

        var columns = Columns(finalSize.Width, shown.Count);
        var width = ColumnWidth(finalSize.Width, columns);
        var top = 0d;

        for (var start = 0; start < shown.Count; start += columns)
        {
            var row = shown.Skip(start).Take(columns).ToList();
            var height = row.Max(child => child.DesiredSize.Height);

            for (var column = 0; column < row.Count; column++)
            {
                row[column].Arrange(new Rect(column * (width + Spacing), top, width, height));
            }

            top += height + Spacing;
        }

        return finalSize;
    }

    private List<Control> Shown() => [.. Children.Where(child => child.IsVisible)];

    private int Columns(double width, int count) =>
        Math.Clamp((int)Math.Floor((width + Spacing) / (MinColumnWidth + Spacing)), 1, count);

    private double ColumnWidth(double width, int columns) => Math.Max(0, (width - (Spacing * (columns - 1))) / columns);
}
