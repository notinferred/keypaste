using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Keypaste.App.Controls;

/// <summary>
/// A keypaste mark drawn from its outlines: the ink, and the dot, which is never omitted.
/// </summary>
/// <remarks>
/// The outlines are Hepta Slab SemiBold, written into <c>Theme/BrandOutlines.axaml</c> by
/// <c>scripts/outline-brand-marks.py</c>, so no font is vendored. The mark keeps its proportions
/// and centres in its bounds; a set Height gives its width, a set Width its height, and with neither
/// it takes its default height. Geometry and brushes come from <c>Theme/Brand.axaml</c>; the
/// <c>mono</c> class draws the dot in the ink.
/// </remarks>
internal abstract class BrandOutline : Control
{
    internal static readonly StyledProperty<Geometry?> InkGeometryProperty =
        AvaloniaProperty.Register<BrandOutline, Geometry?>(nameof(InkGeometry));

    internal static readonly StyledProperty<Geometry?> DotGeometryProperty =
        AvaloniaProperty.Register<BrandOutline, Geometry?>(nameof(DotGeometry));

    internal static readonly StyledProperty<IBrush?> InkBrushProperty =
        AvaloniaProperty.Register<BrandOutline, IBrush?>(nameof(InkBrush));

    internal static readonly StyledProperty<IBrush?> DotBrushProperty =
        AvaloniaProperty.Register<BrandOutline, IBrush?>(nameof(DotBrush));

    static BrandOutline()
    {
        AffectsRender<BrandOutline>(InkGeometryProperty, DotGeometryProperty, InkBrushProperty, DotBrushProperty);
        AffectsMeasure<BrandOutline>(InkGeometryProperty, DotGeometryProperty);
    }

    internal Geometry? InkGeometry
    {
        get => GetValue(InkGeometryProperty);
        set => SetValue(InkGeometryProperty, value);
    }

    internal Geometry? DotGeometry
    {
        get => GetValue(DotGeometryProperty);
        set => SetValue(DotGeometryProperty, value);
    }

    internal IBrush? InkBrush
    {
        get => GetValue(InkBrushProperty);
        set => SetValue(InkBrushProperty, value);
    }

    internal IBrush? DotBrush
    {
        get => GetValue(DotBrushProperty);
        set => SetValue(DotBrushProperty, value);
    }

    /// <summary>The height the mark takes when neither its Width nor its Height is set.</summary>
    protected abstract double DefaultHeight { get; }

    private Rect Box => (InkGeometry?.Bounds ?? default).Union(DotGeometry?.Bounds ?? default);

    protected override Size MeasureOverride(Size availableSize)
    {
        var box = Box;

        if (box.Width <= 0 || box.Height <= 0)
        {
            return default;
        }

        var aspect = box.Width / box.Height;

        if (!double.IsNaN(Height))
        {
            return new Size(Height * aspect, Height);
        }

        if (!double.IsNaN(Width))
        {
            return new Size(Width, Width / aspect);
        }

        return new Size(DefaultHeight * aspect, DefaultHeight);
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var box = Box;

        if (box.Width <= 0 || box.Height <= 0)
        {
            return;
        }

        var scale = Math.Min(Bounds.Width / box.Width, Bounds.Height / box.Height);
        var matrix = Matrix.CreateTranslation(-box.X, -box.Y)
            * Matrix.CreateScale(scale, scale)
            * Matrix.CreateTranslation((Bounds.Width - (box.Width * scale)) / 2, (Bounds.Height - (box.Height * scale)) / 2);

        using (context.PushTransform(matrix))
        {
            if (InkGeometry is not null)
            {
                context.DrawGeometry(InkBrush, null, InkGeometry);
            }

            if (DotGeometry is not null)
            {
                context.DrawGeometry(DotBrush, null, DotGeometry);
            }
        }
    }
}

/// <summary>The icon: "k" with its dot. Never set beside the wordmark.</summary>
internal sealed class BrandMark : BrandOutline
{
    protected override double DefaultHeight => 22;
}

/// <summary>The wordmark: "keypaste" with its dot. Never set beside the icon.</summary>
internal sealed class BrandWordmark : BrandOutline
{
    protected override double DefaultHeight => 20;
}
