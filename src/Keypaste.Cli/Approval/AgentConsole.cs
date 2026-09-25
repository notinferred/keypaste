using Keypaste.Cli.Styling;

namespace Keypaste.Cli.Approval;

/// <summary>
/// The one writer for <c>keypaste agent</c>'s stderr, so a line written while a person is choosing
/// cannot splice into the choice line of the dialog they are reading (THREATS.md T-2).
/// </summary>
/// <remarks>
/// <para>
/// While a choice is pending on a terminal, a line from anywhere else — narration, another
/// connection's refusal — first clears the choice line, is written whole, and the choice line is
/// drawn again under it. Redirected, nothing is cleared or redrawn and lines come out in the order
/// they were written.
/// </para>
/// <para>
/// The choice line is cleared by overwriting it with spaces rather than with an erase-line escape,
/// which a Windows console without virtual terminal processing prints instead of obeying. That
/// needs the choice line to keep one width while it counts down.
/// </para>
/// <para>
/// It locks on the synchronized writer itself, which is the same monitor <see cref="Console.Error"/>
/// takes for each of its own writes, so a prompt drawing the choice line in one write to that
/// writer can never land inside a clear, write and redraw done here.
/// </para>
/// </remarks>
internal sealed class AgentConsole
{
    /// <summary>A choice line drawn over one of the same width.</summary>
    internal static string Drawn(string line) => "\r" + line;

    /// <summary>Blanks a drawn line and leaves the cursor at its start.</summary>
    internal static string Cleared(string line) => "\r" + new string(' ', line.Length) + "\r";

    private readonly TextWriter _writer;
    private readonly bool _interactive;
    private Func<string>? _choice;

    /// <summary>Builds the console over stderr.</summary>
    /// <param name="stderr">Where everything is written.</param>
    /// <param name="interactive">Whether stderr's reader is at a terminal, so the choice line is drawn and redrawn.</param>
    internal AgentConsole(TextWriter stderr, bool interactive)
    {
        ArgumentNullException.ThrowIfNull(stderr);

        _writer = TextWriter.Synchronized(stderr);
        _interactive = interactive;
    }

    /// <summary>The mark as stderr's encoding can carry it.</summary>
    internal string Glyph(Mark mark) => ConsoleMarks.For(_writer, mark);

    /// <summary>Writes one line, or several separated by newlines, as a whole.</summary>
    /// <param name="line">The text.</param>
    internal void WriteLine(string line = "")
    {
        ArgumentNullException.ThrowIfNull(line);

        lock (_writer)
        {
            var pending = _interactive ? _choice : null;

            if (pending is not null)
            {
                _writer.Write(Cleared(pending()));
            }

            _writer.WriteLine(line);

            if (pending is not null)
            {
                _writer.Write(Drawn(pending()));
            }

            _writer.Flush();
        }
    }

    /// <summary>Starts a choice: from now until <see cref="EndChoice"/> other lines keep the choice line whole and last.</summary>
    /// <param name="render">The choice line as it should read now.</param>
    /// <remarks>The prompt reading the choice draws the line and its countdown, each time in one write of <see cref="Drawn"/>, at one width.</remarks>
    internal void BeginChoice(Func<string> render)
    {
        ArgumentNullException.ThrowIfNull(render);

        lock (_writer)
        {
            _choice = render;
        }
    }

    /// <summary>Ends the choice, leaving its line where it is.</summary>
    internal void EndChoice()
    {
        lock (_writer)
        {
            if (_choice is not null && _interactive)
            {
                _writer.WriteLine();
                _writer.Flush();
            }

            _choice = null;
        }
    }
}
