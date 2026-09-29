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

/// <summary>
/// The amber elements in a frame the app drew: what BRAND rule 4 allows once per view (D-0375).
/// </summary>
/// <remarks>
/// <para>
/// <b>An amber pixel</b> has the accent's hue, 30° to 48°, and a chroma of at least 48 of 255. That
/// takes in amber and its hover and press shades, and the light theme's amber text, and leaves out
/// the 10–15% tints of a selected row or a focused field's glow, which the eye reads as a surface,
/// and every status colour, whose hues are far from it.
/// </para>
/// <para>
/// <b>An element</b> is a connected region of amber pixels, joined across a word's space along a
/// line (ten pixels) and three pixels between lines, so a label and its fill, or a line of amber text,
/// are one; and at least eight pixels large, so an anti-aliased fleck is none. Only the topmost surface counts: with a dialog up, what the backdrop dims is not in view.
/// </para>
/// <para>
/// <b>Not counted</b>, because BRAND gives them amber as identity or state rather than as a signal:
/// the marks, a selected row's inset bar and the keyboard's focus ring.
/// </para>
/// </remarks>
internal static class AmberElements
{
    private const int _minimumChroma = 48;
    private const double _hueFrom = 30;
    private const double _hueTo = 48;
    private const int _joinAcross = 10;
    private const int _joinDown = 3;
    private const int _minimumPixels = 8;

    /// <summary>The bounds of each amber element in the frame the window draws now.</summary>
    internal static IReadOnlyList<PixelRect> In(TopLevel window)
    {
        // Subpixel text puts orange fringes on white glyphs; grayscale text draws amber only where it is.
        // What was drawn before the switch is redrawn, since a visual's drawing is kept until invalidated.
        if (TextOptions.GetTextRenderingMode(window) != TextRenderingMode.Antialias)
        {
            TextOptions.SetTextRenderingMode(window, TextRenderingMode.Antialias);

            foreach (var visual in window.GetVisualDescendants())
            {
                visual.InvalidateVisual();
            }
        }

        WindowInput.Drain();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(30);
        WindowInput.Drain();

        using var bitmap = window.CaptureRenderedFrame();
        Assert.True(bitmap is not null, "the session drew no frame; it must render with Skia");

        var (pixels, width, height) = Read(bitmap);
        var scaling = window.RenderScaling;
        var mask = new bool[width * height];
        var surface = Surface(window, scaling, width, height);

        for (var y = surface.Y; y < surface.Bottom; y++)
        {
            for (var x = surface.X; x < surface.Right; x++)
            {
                var i = (y * width) + x;
                mask[i] = IsAmber(pixels[i * 4 + 2], pixels[i * 4 + 1], pixels[i * 4]);
            }
        }

        foreach (var excluded in Excluded(window, scaling))
        {
            Clear(mask, width, height, excluded);
        }

        return Components(mask, width, height);
    }

    /// <summary>
    /// Asserts the frame holds at most one amber element and, when the view has a primary action or
    /// live signal, that the one element is it.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="expected">The control that should be the one amber element, or null for none.</param>
    /// <param name="at">Which frame, for the message.</param>
    internal static void AssertOne(TopLevel window, Control? expected, string at)
    {
        var found = In(window);

        if (expected is null)
        {
            Assert.True(found.Count == 0, $"{at}: expected no amber element, found {Describe(window, found)}");
            return;
        }

        Assert.True(found.Count == 1, $"{at}: expected one amber element, {expected.Name}, found {Describe(window, found)}");

        var bounds = Bounds(expected, window, window.RenderScaling, inflate: 3);
        Assert.True(bounds.Contains(found[0]), $"{at}: the amber element {found[0]} is not {expected.Name} at {bounds}");
    }

    internal static bool IsAmber(byte r, byte g, byte b)
    {
        int max = Math.Max(r, Math.Max(g, b));
        int min = Math.Min(r, Math.Min(g, b));
        var chroma = max - min;

        if (chroma < _minimumChroma || max != r)
        {
            return false;
        }

        var hue = 60d * (g - b) / chroma;
        return hue is >= _hueFrom and <= _hueTo;
    }

    private static PixelRect Surface(TopLevel window, double scaling, int width, int height)
    {
        var everything = new PixelRect(0, 0, width, height);
        var dialog = window.GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains("backdrop") && border.IsEffectivelyVisible)
            .SelectMany(backdrop => backdrop.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("dialog")))
            .FirstOrDefault(border => border.IsEffectivelyVisible);

        return dialog is null ? everything : Bounds(dialog, window, scaling, inflate: 0).Intersect(everything);
    }

    private static IEnumerable<PixelRect> Excluded(TopLevel window, double scaling)
    {
        foreach (var control in window.GetVisualDescendants().OfType<Control>().Where(control => control.IsEffectivelyVisible))
        {
            switch (control)
            {
                case BrandMark or BrandLockup:
                    yield return Bounds(control, window, scaling, inflate: 1);
                    break;

                case ListBoxItem { IsSelected: true } row when row.Bounds.Height > 0:
                    var edge = Bounds(row, window, scaling, inflate: 0);
                    yield return new PixelRect(edge.X, edge.Y, (int)Math.Ceiling(3 * scaling), edge.Height);
                    break;
            }
        }

        if (window.FocusManager?.GetFocusedElement() is Control { IsEffectivelyVisible: true } focused)
        {
            var inner = Bounds(focused, window, scaling, inflate: 0);
            var outer = Bounds(focused, window, scaling, inflate: 5);

            // A field shows focus with its own border and glow; anything else with a ring outside it.
            if (focused is TextBox or MaskedInput)
            {
                yield return outer;
            }
            else
            {
                yield return new PixelRect(outer.X, outer.Y, outer.Width, inner.Y - outer.Y);
                yield return new PixelRect(outer.X, inner.Bottom, outer.Width, outer.Bottom - inner.Bottom);
                yield return new PixelRect(outer.X, outer.Y, inner.X - outer.X, outer.Height);
                yield return new PixelRect(inner.Right, outer.Y, outer.Right - inner.Right, outer.Height);
            }
        }
    }

    private static PixelRect Bounds(Visual control, TopLevel window, double scaling, int inflate)
    {
        var origin = control.TranslatePoint(default, window) ?? default;
        var pixels = PixelRect.FromRect(new Rect(origin, control.Bounds.Size), scaling);
        var grow = (int)Math.Ceiling(inflate * scaling);
        return new PixelRect(pixels.X - grow, pixels.Y - grow, pixels.Width + (2 * grow), pixels.Height + (2 * grow));
    }

    private static void Clear(bool[] mask, int width, int height, PixelRect area)
    {
        var clipped = area.Intersect(new PixelRect(0, 0, width, height));

        for (var y = clipped.Y; y < clipped.Bottom; y++)
        {
            Array.Clear(mask, (y * width) + clipped.X, clipped.Width);
        }
    }

    /// <summary>Each connected region, joined across a word's space, of at least <see cref="_minimumPixels"/> pixels.</summary>
    private static List<PixelRect> Components(bool[] mask, int width, int height)
    {
        var seen = new bool[mask.Length];
        var found = new List<PixelRect>();
        var queue = new Queue<int>();

        for (var start = 0; start < mask.Length; start++)
        {
            if (!mask[start] || seen[start])
            {
                continue;
            }

            int left = width, top = height, right = 0, bottom = 0, count = 0;
            seen[start] = true;
            queue.Enqueue(start);

            while (queue.TryDequeue(out var at))
            {
                int x = at % width, y = at / width;
                count++;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);

                for (var dy = -_joinDown; dy <= _joinDown; dy++)
                {
                    for (var dx = -_joinAcross; dx <= _joinAcross; dx++)
                    {
                        int nx = x + dx, ny = y + dy;

                        if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        {
                            continue;
                        }

                        var next = (ny * width) + nx;

                        if (mask[next] && !seen[next])
                        {
                            seen[next] = true;
                            queue.Enqueue(next);
                        }
                    }
                }
            }

            if (count >= _minimumPixels)
            {
                found.Add(new PixelRect(left, top, right - left + 1, bottom - top + 1));
            }
        }

        return found;
    }

    private static string Describe(TopLevel window, IReadOnlyList<PixelRect> found)
    {
        if (found.Count == 0)
        {
            return "none";
        }

        var scaling = window.RenderScaling;
        return string.Join("; ", found.Select(box =>
        {
            var centre = new Point((box.X + (box.Width / 2d)) / scaling, (box.Y + (box.Height / 2d)) / scaling);
            var named = window.GetVisualsAt(centre).OfType<Control>().Select(visual => visual.GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault(control => !string.IsNullOrEmpty(control.Name))).FirstOrDefault(control => control is not null);
            return $"{box} ({named?.Name ?? named?.GetType().Name ?? "unnamed"})";
        }));
    }

    private static (byte[] Pixels, int Width, int Height) Read(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        using var copy = new WriteableBitmap(size, bitmap.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = copy.Lock();

        bitmap.CopyPixels(buffer);

        var bytes = new byte[size.Width * size.Height * 4];

        for (var y = 0; y < size.Height; y++)
        {
            Marshal.Copy(buffer.Address + (y * buffer.RowBytes), bytes, y * size.Width * 4, size.Width * 4);
        }

        return (bytes, size.Width, size.Height);
    }
}
