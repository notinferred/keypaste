using Avalonia.Controls;
using Avalonia.Media;

namespace Keypaste.App.Theme;

/// <summary>
/// Loads every weight of the app's two faces before anything draws, so each weight resolves to the same face for the whole run.
/// </summary>
/// <remarks>
/// A weight a system face lacks, such as semibold in macOS's Helvetica Neue or in most Linux faces, is resolved to the nearest of
/// the face's weights loaded so far; resolving all nine at once, lightest first, fixes the answer before any text asks, so no
/// screen draws differently by what was drawn before it.
/// </remarks>
internal static class FontWeights
{
    private static readonly string[] _faces = ["KpSans", "KpMono"];

    /// <summary>Resolves weights 100 to 900, upright and italic, of each face <paramref name="resources"/> names.</summary>
    internal static void Pin(IResourceHost resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        foreach (var face in _faces)
        {
            if (!resources.TryFindResource(face, out var found) || found is not FontFamily family)
            {
                continue;
            }

            foreach (var style in new[] { FontStyle.Normal, FontStyle.Italic })
            {
                for (var weight = 100; weight <= 900; weight += 100)
                {
                    _ = FontManager.Current.TryGetGlyphTypeface(new Typeface(family, style, (FontWeight)weight), out _);
                }
            }
        }
    }
}
