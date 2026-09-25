using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Keypaste.App.Controls;

/// <summary>
/// The keypaste mark: a cursor stem and an insert-bracket arm on a 64-unit grid.
/// </summary>
/// <remarks>
/// The 4-unit gap between stem and arm is part of the mark and is never closed, which is why this
/// draws the two shapes rather than one outline. Brushes come from <c>Theme/Brand.axaml</c>; the
/// <c>mono</c> class draws both in the stem colour.
/// </remarks>
internal sealed class BrandMark : Control
{
    private const double _grid = 64;

    internal static readonly StyledProperty<IBrush?> StemBrushProperty =
        AvaloniaProperty.Register<BrandMark, IBrush?>(nameof(StemBrush));

    internal static readonly StyledProperty<IBrush?> ArmBrushProperty =
        AvaloniaProperty.Register<BrandMark, IBrush?>(nameof(ArmBrush));

    private static readonly Geometry _stem = Geometry.Parse("M10,8 H20 V56 H10 Z");
    private static readonly Geometry _arm = Geometry.Parse("M40,24 L54,24 L38,40 L54,56 L40,56 L24,40 Z");

    static BrandMark() => AffectsRender<BrandMark>(StemBrushProperty, ArmBrushProperty);

    internal IBrush? StemBrush
    {
        get => GetValue(StemBrushProperty);
        set => SetValue(StemBrushProperty, value);
    }

    internal IBrush? ArmBrush
    {
        get => GetValue(ArmBrushProperty);
        set => SetValue(ArmBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var side = Math.Min(Bounds.Width, Bounds.Height);
        var scale = side / _grid;
        var matrix = Matrix.CreateScale(scale, scale)
            * Matrix.CreateTranslation((Bounds.Width - side) / 2, (Bounds.Height - side) / 2);

        using (context.PushTransform(matrix))
        {
            context.DrawGeometry(StemBrush, null, _stem);
            context.DrawGeometry(ArmBrush, null, _arm);
        }
    }
}

/// <summary>The mark beside the wordmark, which is live text in Instrument Sans 600 at −0.045em.</summary>
internal sealed class BrandLockup : TemplatedControl
{
    internal static readonly StyledProperty<double> MarkSizeProperty =
        AvaloniaProperty.Register<BrandLockup, double>(nameof(MarkSize), 22d);

    internal double MarkSize
    {
        get => GetValue(MarkSizeProperty);
        set => SetValue(MarkSizeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        base.OnPropertyChanged(change);

        if (change.Property == FontSizeProperty)
        {
            SetCurrentValue(LetterSpacingProperty, -0.045 * FontSize);
        }
    }
}
