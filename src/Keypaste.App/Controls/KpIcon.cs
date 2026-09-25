using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Keypaste.App.Controls;

/// <summary>
/// A Lucide icon, stroked at a fixed pixel width whatever size it is drawn at.
/// </summary>
/// <remarks>
/// Lucide is a stroke set on a 24-unit grid, so a filled <see cref="PathIcon"/> draws it as blobs.
/// Name the icon with <see cref="Icon"/> (<c>Icon="lock"</c> finds <c>Icon.lock</c> in
/// <c>Theme/Icons.axaml</c>) or pass a geometry as <see cref="Data"/>. The colour is the inherited
/// foreground, so an icon inside a button or a selected row follows its text.
/// </remarks>
internal sealed class KpIcon : Control
{
    private const double _grid = 24;

    internal static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<KpIcon, Geometry?>(nameof(Data));

    /// <summary>A Lucide name such as <c>lock</c> or <c>key-round</c>; used when <see cref="Data"/> is not set.</summary>
    internal static readonly StyledProperty<string?> IconProperty =
        AvaloniaProperty.Register<KpIcon, string?>(nameof(Icon));

    /// <summary>The drawn edge length in pixels: 14 in fields, 16 in lists, 20 in the sidebar.</summary>
    internal static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<KpIcon, double>(nameof(Size), 16d);

    internal static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<KpIcon, double>(nameof(StrokeThickness), 1.5d);

    internal static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<KpIcon>();

    static KpIcon()
    {
        AffectsRender<KpIcon>(DataProperty, IconProperty, SizeProperty, StrokeThicknessProperty, ForegroundProperty);
        AffectsMeasure<KpIcon>(SizeProperty);
    }

    internal Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    internal string? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    internal double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    internal double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    internal IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if ((Data ?? Named()) is not { } data || Foreground is not { } brush || Size <= 0)
        {
            return;
        }

        var scale = Size / _grid;
        var offset = new Point((Bounds.Width - Size) / 2, (Bounds.Height - Size) / 2);
        var pen = new Pen(brush, StrokeThickness / scale, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset.X, offset.Y)))
        {
            context.DrawGeometry(null, pen, data);
        }
    }

    private Geometry? Named() =>
        Icon is { Length: > 0 } name && this.TryFindResource($"Icon.{name}", out var found)
            ? found as Geometry
            : null;
}
