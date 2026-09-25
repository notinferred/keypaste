using System.Text;
using Keypaste.Cli.Styling;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>The marks beside keypaste's output fall back to ASCII where the stream cannot carry them.</summary>
public sealed class ConsoleMarksTests
{
    [Fact]
    public void UnicodeWriter_ShowsGlyphs()
    {
        using var writer = new StreamWriter(new MemoryStream(), new UTF8Encoding(false));

        Assert.Equal("✓", ConsoleMarks.For(writer, Mark.Done));
        Assert.Equal("›", ConsoleMarks.For(writer, Mark.Prompt));
        Assert.Equal("·", ConsoleMarks.For(writer, Mark.Dot));
    }

    [Fact]
    public void AsciiWriter_FallsBackToOkGtDash()
    {
        using var writer = new StreamWriter(new MemoryStream(), Encoding.ASCII);

        Assert.Equal("ok", ConsoleMarks.For(writer, Mark.Done));
        Assert.Equal(">", ConsoleMarks.For(writer, Mark.Prompt));
        Assert.Equal("-", ConsoleMarks.For(writer, Mark.Dot));
    }

    /// <summary>
    /// A Windows console code page substitutes a look-alike rather than throwing, as code page 437
    /// did in the acceptance transcript: the mark it cannot carry falls back, the one it can stays.
    /// </summary>
    [Fact]
    public void ABestFitCodePage_FallsBackOnlyWhereItSubstitutes()
    {
        using var writer = new StreamWriter(new MemoryStream(), new BestFit());

        Assert.Equal("ok", ConsoleMarks.For(writer, Mark.Done));
        Assert.Equal("·", ConsoleMarks.For(writer, Mark.Dot));
    }

    [Fact]
    public void AShortenedTokenPrefix_KeepsItsEllipsisOnlyWhereItCanBeShown()
    {
        using var unicode = new StreamWriter(new MemoryStream(), new UTF8Encoding(false));
        using var bestFit = new StreamWriter(new MemoryStream(), new BestFit());

        Assert.Equal("kpt_7d2e91c0…", ConsoleMarks.Shortened(unicode, "kpt_7d2e91c0…"));
        Assert.Equal("kpt_7d2e91c0...", ConsoleMarks.Shortened(bestFit, "kpt_7d2e91c0…"));
    }

    /// <summary>Latin-1 that writes <c>?</c> for anything else whatever fallback it is given.</summary>
    private sealed class BestFit : Encoding
    {
        public override int GetByteCount(char[] chars, int index, int count) => count;

        public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
        {
            for (var i = 0; i < charCount; i++)
            {
                var c = chars[charIndex + i];
                bytes[byteIndex + i] = c <= 'ÿ' ? (byte)c : (byte)'?';
            }

            return charCount;
        }

        public override int GetCharCount(byte[] bytes, int index, int count) => count;

        public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
        {
            for (var i = 0; i < byteCount; i++)
            {
                chars[charIndex + i] = (char)bytes[byteIndex + i];
            }

            return byteCount;
        }

        public override int GetMaxByteCount(int charCount) => charCount;

        public override int GetMaxCharCount(int byteCount) => byteCount;
    }
}
