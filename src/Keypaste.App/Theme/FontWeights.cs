using Avalonia.Controls;
using Avalonia.Media;

namespace Keypaste.App.Theme;

/// <summary>
/// Loads the weights the app draws in its two faces before anything draws, so each weight resolves to the same face for the whole run.
/// </summary>
/// <remarks>
/// <para>
/// A weight a system face lacks, such as semibold in macOS's Helvetica Neue or in most Linux faces, is resolved to the nearest of
/// the face's weights loaded so far; resolving them at once, in one order, fixes the answer before any text asks, so no
/// screen draws differently by what was drawn before it.
/// </para>
/// <para>
/// A face asked for a weight of 600 or more whose nearest loaded weight is lighter, or for a slant it lacks, gets a synthesized
/// copy made from the whole font file, and Avalonia 12.1 crashes the process when a collection runs during that copy. So bold
/// resolves before semibold, which then takes the face's bold, and nothing heavier than bold or slanted, which the app never
/// draws, is asked for.
/// </para>
/// </remarks>
internal static class FontWeights
{
    private static readonly string[] _faces = ["KpSans", "KpMono"];

    private static readonly FontWeight[] _weights =
    [
        FontWeight.Thin, FontWeight.ExtraLight, FontWeight.Light, FontWeight.Normal, FontWeight.Medium, FontWeight.Bold, FontWeight.SemiBold,
    ];

    /// <summary>Resolves the upright weights thin to bold of each face <paramref name="resources"/> names in <paramref name="fonts"/>.</summary>
    /// <returns>The face each weight resolved to.</returns>
    internal static IReadOnlyList<GlyphTypeface> Pin(IResourceHost resources, FontManager fonts)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(fonts);

        var resolved = new List<GlyphTypeface>();

        foreach (var face in _faces)
        {
            if (!resources.TryFindResource(face, out var found) || found is not FontFamily family)
            {
                continue;
            }

            foreach (var weight in _weights)
            {
                if (fonts.TryGetGlyphTypeface(new Typeface(family, FontStyle.Normal, weight), out var glyphTypeface))
                {
                    resolved.Add(glyphTypeface);
                }
            }
        }

        return resolved;
    }
}
