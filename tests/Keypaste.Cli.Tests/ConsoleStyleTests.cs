using Keypaste.Cli.Styling;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// Colour reaches a terminal and nothing else: a pipe, a file, a build log and an opted-out terminal
/// all get exactly the plain text (D-0350).
/// </summary>
/// <remarks>
/// Asserted against the real <see cref="SystemConsoleStyle"/> with a fake terminal, rather than the
/// harness fake, which writes plain text so every other test in this suite can match substrings.
/// </remarks>
public sealed class ConsoleStyleTests : IDisposable
{
    internal const string Escape = "\u001b";

    private readonly StringWriter _stdout = new();
    private readonly StringWriter _stderr = new();

    public void Dispose()
    {
        _stdout.Dispose();
        _stderr.Dispose();
    }

    private SystemConsoleStyle Style(
        bool outputRedirected = false,
        bool errorRedirected = false,
        FakeTerminal? terminal = null,
        params (string Name, string Value)[] environment)
    {
        var values = environment.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);

        return new SystemConsoleStyle(
            new FakeEnvironment(values),
            _stdout,
            outputRedirected,
            _stderr,
            errorRedirected,
            terminal ?? new FakeTerminal());
    }

    [Fact]
    public void Paint_IsPlain_WhenRedirected()
    {
        var style = Style(outputRedirected: true, errorRedirected: true);

        Assert.Equal("text", style.Paint(_stdout, Tone.Accent, "text"));
        Assert.Equal("text", style.Paint(_stderr, Tone.Danger, "text"));

        style.Alarm(_stderr, "danger");
        Assert.Equal("danger" + Environment.NewLine, _stderr.ToString());
    }

    [Theory]
    [InlineData("1")]
    [InlineData("anything")]
    public void Paint_IsPlain_WithNO_COLOR(string value)
    {
        var style = Style(environment: ("NO_COLOR", value));

        Assert.Equal("text", style.Paint(_stderr, Tone.Ok, "text"));
        Assert.False(style.IsTerminal(_stderr));
    }

    [Fact]
    public void Paint_IsPlain_WithTERMDumb()
    {
        var style = Style(environment: ("TERM", "dumb"));

        Assert.Equal("text", style.Paint(_stdout, Tone.Muted, "text"));

        style.Alarm(_stderr, "danger");
        Assert.DoesNotContain(Escape, _stderr.ToString(), StringComparison.Ordinal);
    }

    /// <summary>The convention is that <c>NO_COLOR</c> must be set and non-empty; an empty value opts nothing out.</summary>
    [Fact]
    public void AnEmptyNO_COLOR_IsNotAnOptOut() =>
        Assert.StartsWith(Escape, Style(environment: ("NO_COLOR", string.Empty)).Paint(_stderr, Tone.Ok, "text"), StringComparison.Ordinal);

    [Theory]
    [InlineData("truecolor")]
    [InlineData("24bit")]
    public void Paint_Truecolor_WithCOLORTERM(string colorterm)
    {
        var style = Style(environment: ("COLORTERM", colorterm));

        Assert.Equal("\u001b[38;2;242;181;68mtext" + SystemConsoleStyle.Reset, style.Paint(_stdout, Tone.Accent, "text"));
        Assert.Equal("\u001b[38;2;114;214;153mok" + SystemConsoleStyle.Reset, style.Paint(_stdout, Tone.Ok, "ok"));
        Assert.Equal("\u001b[38;2;247;133;125mno" + SystemConsoleStyle.Reset, style.Paint(_stdout, Tone.Danger, "no"));
        Assert.Equal("\u001b[38;2;142;144;150m-" + SystemConsoleStyle.Reset, style.Paint(_stdout, Tone.Muted, "-"));
    }

    [Fact]
    public void Paint_Truecolor_OnATerminalThatTakesItWithoutBeingTold() =>
        Assert.Equal(
            "\u001b[38;2;242;181;68mtext" + SystemConsoleStyle.Reset,
            Style(terminal: new FakeTerminal { Truecolor = true }).Paint(_stdout, Tone.Accent, "text"));

    [Fact]
    public void Paint_256_Otherwise()
    {
        var style = Style(environment: ("TERM", "xterm-256color"));

        Assert.Equal("\u001b[38;5;214mtext" + SystemConsoleStyle.Reset, style.Paint(_stdout, Tone.Accent, "text"));
        Assert.Equal("\u001b[38;5;78mtext" + SystemConsoleStyle.Reset, style.Paint(_stdout, Tone.Ok, "text"));
        Assert.Equal("\u001b[38;5;210mtext" + SystemConsoleStyle.Reset, style.Paint(_stdout, Tone.Danger, "text"));
        Assert.Equal("\u001b[38;5;246mtext" + SystemConsoleStyle.Reset, style.Paint(_stdout, Tone.Muted, "text"));

        style.Alarm(_stderr, "danger");
        Assert.Equal("\u001b[38;5;210mdanger" + SystemConsoleStyle.Reset + Environment.NewLine, _stderr.ToString());
    }

    /// <summary>A Windows console that refuses escapes gets plain text, and an alarm keeps the console colour route.</summary>
    [Fact]
    public void WindowsWithoutVT_PaintsPlain_AndAlarmsThroughConsoleColour()
    {
        var terminal = new FakeTerminal { Enables = false, Truecolor = true };
        var style = Style(terminal: terminal);

        Assert.Equal("text", style.Paint(_stderr, Tone.Accent, "text"));
        Assert.False(style.IsTerminal(_stderr));

        style.Alarm(_stderr, "danger");

        Assert.Equal([StandardStream.Output, StandardStream.Error], terminal.Asked);
        Assert.Equal(("danger", ConsoleColor.Red), Assert.Single(terminal.Coloured));
        Assert.Equal("danger" + Environment.NewLine, _stderr.ToString());
    }

    [Fact]
    public void IsTerminal_OnlyForTheUnredirectedStream()
    {
        var terminal = new FakeTerminal();
        var style = Style(outputRedirected: true, terminal: terminal);
        using var elsewhere = new StringWriter();

        Assert.False(style.IsTerminal(_stdout));
        Assert.True(style.IsTerminal(_stderr));
        Assert.False(style.IsTerminal(elsewhere));
        Assert.Equal("text", style.Paint(elsewhere, Tone.Accent, "text"));
        Assert.Equal([StandardStream.Error], terminal.Asked);
    }

    private sealed class FakeTerminal : IVirtualTerminal
    {
        internal bool Enables { get; init; } = true;

        public bool Truecolor { get; init; }

        internal List<StandardStream> Asked { get; } = [];

        internal List<(string Text, ConsoleColor Colour)> Coloured { get; } = [];

        public bool TryEnable(StandardStream stream)
        {
            Asked.Add(stream);
            return Enables;
        }

        public void WriteLine(TextWriter writer, string text, ConsoleColor colour)
        {
            Coloured.Add((text, colour));
            writer.WriteLine(text);
        }
    }
}
