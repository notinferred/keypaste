using System.Text;

namespace Keypaste.Cli.Styling;

/// <summary>The marks keypaste prints beside its output, and their ASCII stand-ins.</summary>
internal static class ConsoleMarks
{
    /// <summary>The mark as the writer's encoding can carry it: the glyph, or its ASCII stand-in.</summary>
    internal static string For(TextWriter writer, Mark mark)
    {
        var (glyph, ascii) = mark switch
        {
            Mark.Done => ("✓", "ok"),
            Mark.Prompt => ("›", ">"),
            _ => ("·", "-"),
        };

        return Carried(writer, glyph, ascii);
    }

    /// <summary>A shortened name as the writer's encoding can carry it: its ellipsis, or three dots.</summary>
    internal static string Shortened(TextWriter writer, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.Replace("…", Carried(writer, "…", "..."), StringComparison.Ordinal);
    }

    /// <summary>The glyph when the writer's encoding carries it unchanged, else its ASCII stand-in.</summary>
    internal static string Carried(TextWriter writer, string glyph, string ascii)
    {
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            if (writer.Encoding.CodePage is 65001 or 1200 or 1201)
            {
                return glyph;
            }

            // The round trip catches a Windows console code page, whose encoding substitutes a
            // best-fit character instead of honouring the exception fallback.
            var strict = (Encoding)writer.Encoding.Clone();
            strict.EncoderFallback = EncoderFallback.ExceptionFallback;
            return strict.GetString(strict.GetBytes(glyph)) == glyph ? glyph : ascii;
        }
        catch (Exception)
        {
            return ascii;
        }
    }
}
