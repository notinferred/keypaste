using System.Text;

namespace Keypaste.Cli.Styling;

/// <summary>The marks keypaste prints beside its output, and their ASCII stand-ins.</summary>
internal static class ConsoleMarks
{
    /// <summary>The mark as the writer's encoding can carry it: the glyph, or its ASCII stand-in.</summary>
    internal static string For(TextWriter writer, Mark mark)
    {
        ArgumentNullException.ThrowIfNull(writer);

        var (glyph, ascii) = mark switch
        {
            Mark.Done => ("✓", "ok"),
            Mark.Prompt => ("›", ">"),
            _ => ("·", "-"),
        };

        try
        {
            if (writer.Encoding.CodePage is 65001 or 1200 or 1201)
            {
                return glyph;
            }

            var strict = (Encoding)writer.Encoding.Clone();
            strict.EncoderFallback = EncoderFallback.ExceptionFallback;
            strict.GetBytes(glyph);
            return glyph;
        }
        catch (Exception)
        {
            return ascii;
        }
    }
}
