using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Xunit;

namespace Keypaste.App.Tests.Rendering;

/// <summary>How a control draws text: what a reference rendering has to match.</summary>
internal readonly record struct TextStyle(Typeface Typeface, double Size)
{
    /// <summary>The style <see cref="RevealedValue"/> draws its value and its dots in.</summary>
    internal static TextStyle Of(RevealedValue cell) => new(new Typeface(cell.FontFamily), cell.FontSize);

    internal static TextStyle Of(TextBlock block) =>
        new(new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch), block.FontSize);
}

/// <summary>
/// One frame Skia drew for a window, and the question of whether some text is in it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read from pixels, not from the control.</b> <see cref="RevealedValue.Rendered"/> says what the
/// control meant to draw; this says what the frame holds. A text is found by drawing it alone in the
/// drawing control's own typeface and size, then searching the frame for that picture by normalized
/// cross-correlation, which ignores the colours of the theme and of either polarity.
/// </para>
/// <para>
/// The reference is cropped to its ink, so what is matched is the glyphs and their spacing. A
/// score of 1 is the same picture; <see cref="Threshold"/> was set from the scores measured in 4.6's
/// record, where a drawn value scores near 1 and dots, or another value of the same length drawn in
/// the same cell, score far below it.
/// </para>
/// </remarks>
internal sealed class DrawnFrame
{
    internal const double Threshold = 0.9;

    private const int _sampleCount = 96;
    private const double _coarseThreshold = 0.6;

    private readonly float[] _luminance;

    private DrawnFrame(float[] luminance, int width, int height, double scaling)
    {
        _luminance = luminance;
        Width = width;
        Height = height;
        Scaling = scaling;
    }

    internal int Width { get; }

    internal int Height { get; }

    internal double Scaling { get; }

    /// <summary>The whole frame.</summary>
    internal PixelRect Everywhere => new(0, 0, Width, Height);

    /// <summary>Renders <paramref name="window"/> now and keeps a copy of what was drawn.</summary>
    /// <remarks>
    /// Fails rather than returning an empty frame: a session drawing with the headless stub
    /// produces none, and every claim below would then be vacuously true.
    /// </remarks>
    internal static DrawnFrame Capture(TopLevel window)
    {
        using var bitmap = window.CaptureRenderedFrame();
        Assert.True(bitmap is not null, "the session drew no frame; it must render with Skia");

        var (luminance, width, height) = ReadLuminance(bitmap);
        var frame = new DrawnFrame(luminance, width, height, window.RenderScaling);
        Assert.True(frame.IsDrawn, "the captured frame is one flat colour");
        return frame;
    }

    /// <summary>The pixels <paramref name="control"/> occupies in this frame, with a small margin.</summary>
    /// <remarks>Asserts the control is on screen, since a check of an off-screen control proves nothing.</remarks>
    internal PixelRect Of(Visual control, TopLevel window)
    {
        Assert.True(control.IsEffectivelyVisible, $"{control.GetType().Name} is not visible");

        var origin = control.TranslatePoint(default, window)
            ?? throw new InvalidOperationException($"{control.GetType().Name} is not in the window");
        var bounds = new Rect(origin, control.Bounds.Size);
        var pixels = PixelRect.FromRect(bounds, Scaling);

        Assert.True(
            pixels.Width > 0 && pixels.Height > 0 && Everywhere.Contains(pixels),
            $"{control.GetType().Name} at {pixels} is not inside the {Width}x{Height} frame");

        return new PixelRect(pixels.X - 4, pixels.Y - 4, pixels.Width + 8, pixels.Height + 8).Intersect(Everywhere);
    }

    /// <summary>Whether <paramref name="text"/>, drawn in <paramref name="style"/>, is in <paramref name="within"/>.</summary>
    internal bool Shows(string text, TextStyle style, PixelRect within) => Score(text, style, within) >= Threshold;

    /// <summary>The best match for <paramref name="text"/> anywhere in <paramref name="within"/>, from -1 to 1.</summary>
    internal double Score(string text, TextStyle style, PixelRect within)
    {
        var reference = Reference.Of(text, style, Scaling);

        if (reference.Width > within.Width || reference.Height > within.Height)
        {
            return 0;
        }

        // The coarse pass on a sample of pixels rules out most offsets; only a candidate gets the
        // full correlation. With no candidate the best coarse score is reported, which is below
        // the cut and so below the threshold.
        var best = 0d;
        var bestCoarse = 0d;

        for (var y = within.Y; y + reference.Height <= within.Bottom; y++)
        {
            for (var x = within.X; x + reference.Width <= within.Right; x++)
            {
                var coarse = Correlate(reference, x, y, reference.Samples);
                bestCoarse = Math.Max(bestCoarse, coarse);

                if (coarse >= _coarseThreshold)
                {
                    best = Math.Max(best, Correlate(reference, x, y, reference.All));
                }
            }
        }

        return bestCoarse < _coarseThreshold ? bestCoarse : best;
    }

    private bool IsDrawn
    {
        get
        {
            var first = _luminance[0];
            return _luminance.Any(value => Math.Abs(value - first) > 0.05f);
        }
    }

    /// <summary>Absolute normalized cross-correlation of the reference against the frame at one offset.</summary>
    private double Correlate(Reference reference, int left, int top, int[] indices)
    {
        double sumF = 0, sumFF = 0, sumRF = 0, sumR = 0, sumRR = 0;

        foreach (var i in indices)
        {
            var r = reference.Luminance[i];
            var f = _luminance[((top + (i / reference.Width)) * Width) + left + (i % reference.Width)];

            sumR += r;
            sumRR += r * r;
            sumF += f;
            sumFF += f * f;
            sumRF += r * f;
        }

        var n = indices.Length;
        var covariance = sumRF - (sumR * sumF / n);
        var varianceR = sumRR - (sumR * sumR / n);
        var varianceF = sumFF - (sumF * sumF / n);

        if (varianceR <= 1e-9 || varianceF <= 1e-9)
        {
            return 0;
        }

        return Math.Abs(covariance / Math.Sqrt(varianceR * varianceF));
    }

    private static (float[] Luminance, int Width, int Height) ReadLuminance(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        using var copy = new WriteableBitmap(size, bitmap.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = copy.Lock();

        bitmap.CopyPixels(buffer);

        var bytes = new byte[buffer.RowBytes * size.Height];
        Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);

        var luminance = new float[size.Width * size.Height];

        for (var y = 0; y < size.Height; y++)
        {
            for (var x = 0; x < size.Width; x++)
            {
                var at = (y * buffer.RowBytes) + (x * 4);

                // Composited over white, so a transparent pixel reads as background rather than ink.
                var alpha = bytes[at + 3] / 255f;
                var average = (bytes[at] + bytes[at + 1] + bytes[at + 2]) / (3 * 255f);
                luminance[(y * size.Width) + x] = average + (1 - alpha);
            }
        }

        return (luminance, size.Width, size.Height);
    }

    /// <summary>A text drawn alone, black on white, cropped to its ink.</summary>
    private sealed class Reference
    {
        private Reference(float[] luminance, int width, int height)
        {
            Luminance = luminance;
            Width = width;
            Height = height;
            All = Enumerable.Range(0, luminance.Length).ToArray();

            // The coarse pass looks at an even spread of the ink and of the background around it.
            var ink = All.Where(i => luminance[i] < 0.5f).ToArray();
            var paper = All.Where(i => luminance[i] >= 0.5f).ToArray();
            Samples = Spread(ink, _sampleCount / 2).Concat(Spread(paper, _sampleCount / 2)).ToArray();
        }

        internal float[] Luminance { get; }

        internal int Width { get; }

        internal int Height { get; }

        internal int[] All { get; }

        internal int[] Samples { get; }

        internal static Reference Of(string text, TextStyle style, double scaling)
        {
            var layout = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                style.Typeface,
                style.Size,
                Brushes.Black);

            var size = new PixelSize(
                (int)Math.Ceiling(layout.WidthIncludingTrailingWhitespace * scaling) + 2,
                (int)Math.Ceiling(layout.Height * scaling) + 2);

            using var target = new RenderTargetBitmap(size, new Vector(96 * scaling, 96 * scaling));
            using (var context = target.CreateDrawingContext())
            {
                context.FillRectangle(Brushes.White, new Rect(0, 0, size.Width / scaling, size.Height / scaling));
                context.DrawText(layout, new Point(1, 1));
            }

            var (luminance, width, height) = ReadLuminance(target);
            return CroppedToInk(text, luminance, width, height);
        }

        private static Reference CroppedToInk(string text, float[] luminance, int width, int height)
        {
            int left = width, right = -1, top = height, bottom = -1;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (luminance[(y * width) + x] < 0.9f)
                    {
                        left = Math.Min(left, x);
                        right = Math.Max(right, x);
                        top = Math.Min(top, y);
                        bottom = Math.Max(bottom, y);
                    }
                }
            }

            Assert.True(right >= 0, $"a reference rendering of {text.Length} characters drew no ink");

            var inkWidth = right - left + 1;
            var inkHeight = bottom - top + 1;
            var cropped = new float[inkWidth * inkHeight];

            for (var y = 0; y < inkHeight; y++)
            {
                Array.Copy(luminance, ((top + y) * width) + left, cropped, y * inkWidth, inkWidth);
            }

            return new Reference(cropped, inkWidth, inkHeight);
        }

        private static IEnumerable<int> Spread(int[] indices, int count) =>
            indices.Length <= count
                ? indices
                : Enumerable.Range(0, count).Select(k => indices[(int)((long)k * indices.Length / count)]);
    }
}

/// <summary>Where on screen a control's drawn text is.</summary>
internal static class DrawnText
{
    /// <summary>The text block inside <paramref name="control"/>'s template that shows <paramref name="text"/>.</summary>
    internal static TextBlock Showing(Control control, string text) =>
        control.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == text);
}
