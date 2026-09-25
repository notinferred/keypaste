using System.Globalization;
using Keypaste.Cli.Prompting;

namespace Keypaste.Cli.Styling;

/// <summary>Colour on a terminal, plain text everywhere else.</summary>
/// <remarks>
/// <para>
/// <b>Whether to colour is decided once per stream, at construction.</b> A stream is coloured only
/// when it is not redirected, <c>NO_COLOR</c> is unset or empty, <c>TERM</c> is not <c>dumb</c>,
/// and its console accepts escapes, so a pipe, a file, a build log and every test see exactly the
/// plain text (D-0350).
/// </para>
/// <para>
/// <b>Escapes are written into the text itself</b>, never through <see cref="Console.ForegroundColor"/>,
/// which on Unix emits its escape to stdout whichever stream is being coloured. Windows interprets
/// them once <see cref="WindowsVirtualTerminal"/> has turned processing on; a console that refuses
/// gets plain text, apart from an alarm, which keeps the console attribute route.
/// </para>
/// </remarks>
internal sealed class SystemConsoleStyle : IConsoleStyle
{
    /// <summary>Back to whatever the terminal was doing before.</summary>
    internal const string Reset = "\u001b[0m";

    private readonly TextWriter _stdout;
    private readonly TextWriter _stderr;
    private readonly Colouring _stdoutColouring;
    private readonly Colouring _stderrColouring;
    private readonly IVirtualTerminal _terminal;
    private readonly bool _truecolor;

    internal SystemConsoleStyle(IEnvironmentProbe environment, TextWriter stdout, TextWriter stderr)
        : this(
            environment,
            stdout,
            Console.IsOutputRedirected,
            stderr,
            Console.IsErrorRedirected,
            OperatingSystem.IsWindows() ? new WindowsVirtualTerminal() : new UnixVirtualTerminal())
    {
    }

    internal SystemConsoleStyle(
        IEnvironmentProbe environment,
        TextWriter stdout,
        bool isOutputRedirected,
        TextWriter stderr,
        bool isErrorRedirected,
        IVirtualTerminal terminal)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);
        ArgumentNullException.ThrowIfNull(terminal);

        _stdout = stdout;
        _stderr = stderr;
        _terminal = terminal;

        var optedOut = environment.Get("NO_COLOR") is { Length: > 0 }
            || string.Equals(environment.Get("TERM"), "dumb", StringComparison.Ordinal);

        _stdoutColouring = Decide(isOutputRedirected || optedOut, StandardStream.Output, terminal);
        _stderrColouring = Decide(isErrorRedirected || optedOut, StandardStream.Error, terminal);
        _truecolor = terminal.Truecolor
            || environment.Get("COLORTERM") is "truecolor" or "24bit";
    }

    private enum Colouring
    {
        Plain,
        Escapes,
        ConsoleAttribute,
    }

    /// <inheritdoc/>
    public void Alarm(TextWriter writer, string text)
    {
        ArgumentNullException.ThrowIfNull(writer);

        switch (ColouringOf(writer))
        {
            case Colouring.Escapes:
                writer.WriteLine(Escape(Tone.Danger) + text + Reset);
                break;

            case Colouring.ConsoleAttribute:
                _terminal.WriteLine(writer, text, ConsoleColor.Red);
                break;

            default:
                writer.WriteLine(text);
                break;
        }
    }

    /// <inheritdoc/>
    public string Paint(TextWriter writer, Tone tone, string text) =>
        ColouringOf(writer) == Colouring.Escapes ? Escape(tone) + text + Reset : text;

    /// <inheritdoc/>
    public bool IsTerminal(TextWriter writer) => ColouringOf(writer) == Colouring.Escapes;

    private static Colouring Decide(bool plain, StandardStream stream, IVirtualTerminal terminal)
    {
        if (plain)
        {
            return Colouring.Plain;
        }

        return terminal.TryEnable(stream) ? Colouring.Escapes : Colouring.ConsoleAttribute;
    }

    private Colouring ColouringOf(TextWriter writer) =>
        ReferenceEquals(writer, _stdout) ? _stdoutColouring
        : ReferenceEquals(writer, _stderr) ? _stderrColouring
        : Colouring.Plain;

    private string Escape(Tone tone)
    {
        var (red, green, blue, indexed) = tone switch
        {
            Tone.Accent => (0xF2, 0xB5, 0x44, 214),
            Tone.Ok => (0x72, 0xD6, 0x99, 78),
            Tone.Danger => (0xF7, 0x85, 0x7D, 210),
            _ => (0x8E, 0x90, 0x96, 246),
        };

        return _truecolor
            ? string.Create(CultureInfo.InvariantCulture, $"\u001b[38;2;{red};{green};{blue}m")
            : string.Create(CultureInfo.InvariantCulture, $"\u001b[38;5;{indexed}m");
    }
}
