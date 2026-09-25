namespace Keypaste.Cli.Styling;

internal enum Tone { Muted = 0, Accent = 1, Ok = 2, Danger = 3 }

internal enum Mark { Done = 0, Prompt = 1, Dot = 2 }

/// <summary>How keypaste colours and marks what it prints: amber for what needs the person, green for done, red for refused, grey for the rest.</summary>
/// <remarks>
/// The only place keypaste decides colour. Every member takes the writer, because only the stream
/// that reaches a terminal is ever coloured, and a caller must not have to know which that is.
/// </remarks>
internal interface IConsoleStyle
{
    /// <summary>Writes one line loudly, or plainly when the terminal cannot show it.</summary>
    void Alarm(TextWriter writer, string text);

    /// <summary>The text in its tone's colour on a terminal that shows colour, else unchanged.</summary>
    string Paint(TextWriter writer, Tone tone, string text) => text;

    /// <summary>✓, › or ·, or ok, &gt; or - where the writer's encoding cannot carry them.</summary>
    string Glyph(TextWriter writer, Mark mark) => ConsoleMarks.For(writer, mark);

    /// <summary>Whether the writer reaches an interactive terminal.</summary>
    bool IsTerminal(TextWriter writer) => false;
}
