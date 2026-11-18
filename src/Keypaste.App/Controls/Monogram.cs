using System.Globalization;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Media;

namespace Keypaste.App.Controls;

/// <summary>
/// An item's tile: the mark of the service its website belongs to on the brand's colour, or an icon for what it is, or the first letter of
/// its title, on one of seven hues its UUID picks, so the same item keeps its colour everywhere. A project's tile draws its two initials.
/// </summary>
/// <remarks>
/// keypaste never fetches a site's icon, since that would tell a server which sites the vault holds; the marks are bundled
/// (<see cref="ItemMarks"/>). The tile is decoration beside the title it repeats, so it stays out of the accessibility tree.
/// </remarks>
internal sealed class Monogram : Control
{
    internal const int Hues = 7;
    private const double _largeFrom = 40;
    private const double _grid = 24;
    private const double _markShare = 0.54;
    private const double _iconShare = 0.56;
    private const double _markContrast = 3;

    internal static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<Monogram, string?>(nameof(Title));

    /// <summary>Letters drawn in place of the title's first, such as a project's two initials.</summary>
    internal static readonly StyledProperty<string?> InitialsProperty =
        AvaloniaProperty.Register<Monogram, string?>(nameof(Initials));

    internal static readonly StyledProperty<Guid> IdProperty =
        AvaloniaProperty.Register<Monogram, Guid>(nameof(Id));

    /// <summary>A service's mark or an icon drawn in place of the letter.</summary>
    internal static readonly StyledProperty<ItemMark?> MarkProperty =
        AvaloniaProperty.Register<Monogram, ItemMark?>(nameof(Mark));

    /// <summary>The edge length: 30 in a list's row, 52 at the head of an item.</summary>
    internal static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<Monogram, double>(nameof(Size), 30d);

    static Monogram()
    {
        AffectsRender<Monogram>(TitleProperty, InitialsProperty, IdProperty, MarkProperty, SizeProperty);
        AffectsMeasure<Monogram>(SizeProperty);
    }

    internal string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    internal string? Initials
    {
        get => GetValue(InitialsProperty);
        set => SetValue(InitialsProperty, value);
    }

    internal Guid Id
    {
        get => GetValue(IdProperty);
        set => SetValue(IdProperty, value);
    }

    internal ItemMark? Mark
    {
        get => GetValue(MarkProperty);
        set => SetValue(MarkProperty, value);
    }

    internal double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>The first letter or digit of a title, as one written character, in capitals; nothing for a title with neither.</summary>
    internal static string Letter(string? title)
    {
        var elements = StringInfo.GetTextElementEnumerator(title ?? string.Empty);

        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();

            if (char.IsLetterOrDigit(element, 0))
            {
                return element.ToUpper(CultureInfo.CurrentCulture);
            }
        }

        return string.Empty;
    }

    /// <summary>Which of the seven hues an item takes, from 1, the same for the same UUID on every machine.</summary>
    internal static int Hue(Guid id)
    {
        var bytes = id.ToByteArray();
        var folded = 0u;

        foreach (var value in bytes)
        {
            folded = (folded * 31) + value;
        }

        return (int)(folded % Hues) + 1;
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var radius = (CornerRadius)this.FindResource(Size >= _largeFrom ? "KpRadiusLargeTile" : "KpRadiusTile")!;
        var tile = new Rect((Bounds.Width - Size) / 2, (Bounds.Height - Size) / 2, Size, Size);
        var hue = this.FindResource("KpMonogram" + Hue(Id).ToString(CultureInfo.InvariantCulture)) as IBrush;

        if (Mark?.Service is { } service && this.TryFindResource("Service." + service, out var found) && found is Geometry glyph
            && this.TryFindResource("ServiceColor." + service, out var colour) && colour is Color brand)
        {
            DrawService(context, tile, radius.TopLeft, glyph, brand);
            return;
        }

        context.DrawRectangle(hue, null, new RoundedRect(tile, radius.TopLeft));

        if (Mark?.Icon is { } icon && this.TryFindResource("Icon." + icon, out var outline) && outline is Geometry drawn)
        {
            DrawIcon(context, tile, drawn);
            return;
        }

        if ((Initials ?? Letter(Title)) is not { Length: > 0 } letters)
        {
            return;
        }

        var typeface = new Typeface((FontFamily)this.FindResource("KpSans")!, FontStyle.Normal, FontWeight.Bold);
        var text = new FormattedText(letters, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, Math.Round(Size * (letters.Length > 1 ? 0.37 : 0.43)), this.FindResource("KpOnMonogram") as IBrush);
        context.DrawText(text, new Point(tile.X + ((Size - text.Width) / 2), tile.Y + ((Size - text.Height) / 2)));
    }

    /// <summary>The ink a service's mark takes on its brand's colour, or null where neither white nor ink keeps 3:1 on it.</summary>
    internal static Color? InkOn(Color brand) =>
        AccentPalette.Contrast(Colors.White, brand) >= _markContrast ? Colors.White
        : AccentPalette.Contrast(AccentPalette.Ink, brand) >= _markContrast ? AccentPalette.Ink
        : null;

    // The mark on its brand's colour where it reads there, otherwise on the neutral tile in the text's ink.
    private void DrawService(DrawingContext context, Rect tile, double radius, Geometry glyph, Color brand)
    {
        var ink = InkOn(brand);
        IBrush? ground = ink is null ? this.FindResource("KpBgActive") as IBrush : new SolidColorBrush(brand);
        IBrush? mark = ink is { } on ? new SolidColorBrush(on) : this.FindResource("KpTextPrimary") as IBrush;
        context.DrawRectangle(ground, null, new RoundedRect(tile, radius));

        var edge = Size * _markShare;
        var scale = edge / _grid;
        var offset = new Point(tile.X + ((Size - edge) / 2), tile.Y + ((Size - edge) / 2));

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset.X, offset.Y)))
        {
            context.DrawGeometry(mark, null, glyph);
        }
    }

    private void DrawIcon(DrawingContext context, Rect tile, Geometry outline)
    {
        var edge = Size * _iconShare;
        var scale = edge / _grid;
        var offset = new Point(tile.X + ((Size - edge) / 2), tile.Y + ((Size - edge) / 2));
        var pen = new Pen(this.FindResource("KpOnMonogram") as IBrush, Math.Max(1.5, Size / 20) / scale, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset.X, offset.Y)))
        {
            context.DrawGeometry(null, pen, outline);
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new NoneAutomationPeer(this);
}
