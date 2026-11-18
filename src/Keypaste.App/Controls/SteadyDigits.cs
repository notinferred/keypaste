using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Keypaste.App.Controls;

/// <summary>
/// A text whose digits count, as wide as it is with its widest digit in every place in the face it is drawn in,
/// so what stands beside it stays where it is as it counts, whichever system font draws it.
/// </summary>
internal sealed class SteadyDigits : TextBlock
{
    protected override Type StyleKeyOverride => typeof(TextBlock);

    protected override Size MeasureOverride(Size availableSize)
    {
        var said = base.MeasureOverride(availableSize);

        if (Text is not { Length: > 0 } text || !text.Any(char.IsAsciiDigit))
        {
            return said;
        }

        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var widest = "0123456789".Max(digit =>
        {
            using var layout = new TextLayout(new string([.. text.Select(character => char.IsAsciiDigit(character) ? digit : character)]), typeface, FontSize, Foreground);
            return layout.WidthIncludingTrailingWhitespace + Padding.Left + Padding.Right;
        });

        return said.WithWidth(Math.Max(said.Width, Math.Ceiling(widest)));
    }
}
