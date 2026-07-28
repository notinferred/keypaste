using System.Globalization;
using System.Text;
using Keypaste.Cli.Prompting;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// The hidden-input loop itself. The keystroke source is injected precisely so this is
/// reachable: <c>Console.SetIn</c> does not intercept <c>Console.ReadKey</c>, so without the
/// seam none of this behaviour could be asserted at all (docs/PRODUCT.md law 4.5).
/// </summary>
/// <remarks>
/// Control characters are written as <c>(char)0x..</c> rather than escape sequences so they
/// survive every editor, diff and code review unambiguously.
/// </remarks>
public sealed class ConsoleSecretPromptTests
{
    internal const char Backspace = (char)0x08;
    internal const char Delete = (char)0x7F;
    internal const char CtrlU = (char)0x15;
    internal const char Escape = (char)0x1B;
    internal const char Enter = (char)0x0D;

    [Fact]
    public void Interactive_ReadsUntilEnter_AndEchoesNothing()
    {
        using var prompts = new StringWriter(CultureInfo.InvariantCulture);
        var prompt = Build(prompts, "hunter2" + Enter);

        using var secret = prompt.ReadSecret("Master password: ");

        Assert.NotNull(secret);
        Assert.Equal("hunter2", new string(secret.Value));

        // The prompt appears; the password does not.
        Assert.Contains("Master password: ", prompts.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", prompts.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Not even asterisks: their count leaks the password's length.</summary>
    [Fact]
    public void Interactive_WritesNoMaskCharacters()
    {
        using var prompts = new StringWriter(CultureInfo.InvariantCulture);
        var prompt = Build(prompts, "abcdef" + Enter);

        using var secret = prompt.ReadSecret("Password: ");

        Assert.NotNull(secret);
        Assert.DoesNotContain("*", prompts.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Windows sends U+0008 for backspace; most Unix terminals send U+007F.</summary>
    [Fact]
    public void Interactive_AcceptsBothBackspaceEncodings()
    {
        Assert.Equal("ac", Type("ab" + Backspace + "c" + Enter));
        Assert.Equal("ac", Type("ab" + Delete + "c" + Enter));
    }

    [Fact]
    public void Interactive_CtrlU_ClearsWhatWasTypedSoFar()
    {
        Assert.Equal("xy", Type("abc" + CtrlU + "xy" + Enter));
    }

    [Fact]
    public void Interactive_BackspaceOnAnEmptyBufferIsHarmless()
    {
        Assert.Equal("a", Type(Backspace.ToString() + Backspace + "a" + Enter));
    }

    [Fact]
    public void Interactive_EscapeCancels()
    {
        using var prompts = new StringWriter(CultureInfo.InvariantCulture);
        var prompt = Build(prompts, "secret" + Escape);

        Assert.Null(prompt.ReadSecret("Password: "));
    }

    /// <summary>
    /// Modifier and navigation keys arrive with no character. Appending them would put
    /// invisible characters into the password, which then fails to open the vault later with no
    /// explanation the user could act on.
    /// </summary>
    [Fact]
    public void Interactive_IgnoresKeysWithNoCharacter()
    {
        Assert.Equal("ab", Type("a\0b" + Enter));
    }

    [Fact]
    public void Interactive_AcceptsNonAsciiCharacters()
    {
        Assert.Equal("pässwörd", Type("pässwörd" + Enter));
    }

    /// <summary>
    /// The piped path: no prompt is written at all, and exactly one line is consumed per read.
    /// This is how CI and the compatibility gate drive every verb.
    /// </summary>
    [Fact]
    public void Redirected_ReadsOneLinePerPrompt_AndWritesNoPrompt()
    {
        using var prompts = new StringWriter(CultureInfo.InvariantCulture);
        using var input = Piped("first-line\nsecond-line\n");
        var prompt = new ConsoleSecretPrompt(prompts, ThrowingKeySource, () => true, input);

        using var first = prompt.ReadSecret("Master password: ");
        using var second = prompt.ReadSecret("Password: ");

        Assert.Equal("first-line", new string(first!.Value));
        Assert.Equal("second-line", new string(second!.Value));
        Assert.Empty(prompts.ToString());
        Assert.False(prompt.IsInteractive);
    }

    /// <summary>
    /// The read must consume the line and nothing beyond it. A <see cref="StreamReader"/> would
    /// buffer ahead and swallow the rest, which is invisible until <c>keypaste run</c> hands stdin
    /// to a child that then receives nothing.
    /// </summary>
    [Fact]
    public void Redirected_LeavesEverythingAfterTheLine_ForWhoeverReadsNext()
    {
        using var prompts = new StringWriter(CultureInfo.InvariantCulture);
        using var input = Piped("master-password\nthis belongs to the child\nand this\n");
        var prompt = new ConsoleSecretPrompt(prompts, ThrowingKeySource, () => true, input);

        using var secret = prompt.ReadSecret("Master password: ");
        Assert.Equal("master-password", new string(secret!.Value));

        using var rest = new StreamReader(input, Encoding.UTF8);
        Assert.Equal("this belongs to the child\nand this\n", rest.ReadToEnd());
    }

    /// <summary>A pipe written on Windows carries CRLF, and the CR is not part of the password.</summary>
    [Fact]
    public void Redirected_StripsACarriageReturn()
    {
        using var prompts = new StringWriter(CultureInfo.InvariantCulture);
        using var input = Piped("hunter2\r\n");
        var prompt = new ConsoleSecretPrompt(prompts, ThrowingKeySource, () => true, input);

        using var secret = prompt.ReadSecret("Password: ");
        Assert.Equal("hunter2", new string(secret!.Value));
    }

    /// <summary>
    /// A pipe has no code page and every shell writes UTF-8, so the bytes are decoded as UTF-8
    /// rather than through <c>Console.In</c>. Reading byte-wise must not split a character.
    /// </summary>
    [Fact]
    public void Redirected_DecodesMultiByteCharacters()
    {
        using var prompts = new StringWriter(CultureInfo.InvariantCulture);
        using var input = Piped("pässwörd-é中\n");
        var prompt = new ConsoleSecretPrompt(prompts, ThrowingKeySource, () => true, input);

        using var secret = prompt.ReadSecret("Password: ");
        Assert.Equal("pässwörd-é中", new string(secret!.Value));
    }

    [Fact]
    public void Redirected_ReadsAFinalLineWithNoNewline()
    {
        using var prompts = new StringWriter(CultureInfo.InvariantCulture);
        using var input = Piped("no-trailing-newline");
        var prompt = new ConsoleSecretPrompt(prompts, ThrowingKeySource, () => true, input);

        using var secret = prompt.ReadSecret("Password: ");
        Assert.Equal("no-trailing-newline", new string(secret!.Value));
    }

    [Fact]
    public void Redirected_ReturnsNullAtEndOfInput()
    {
        using var prompts = new StringWriter(CultureInfo.InvariantCulture);
        using var input = Piped(string.Empty);
        var prompt = new ConsoleSecretPrompt(prompts, ThrowingKeySource, () => true, input);

        Assert.Null(prompt.ReadSecret("Master password: "));
    }

    /// <summary>Stdin as the CLI really sees it: a byte stream, not a decoded reader.</summary>
    private static MemoryStream Piped(string text) => new(Encoding.UTF8.GetBytes(text));

    private static string Type(string keystrokes)
    {
        using var prompts = new StringWriter(CultureInfo.InvariantCulture);
        var prompt = Build(prompts, keystrokes);

        using var secret = prompt.ReadSecret("Password: ");

        Assert.NotNull(secret);
        return new string(secret.Value);
    }

    private static ConsoleSecretPrompt Build(TextWriter prompts, string keystrokes)
    {
        var index = 0;
        return new ConsoleSecretPrompt(
            prompts,
            () =>
            {
                var c = keystrokes[index++];
                return new ConsoleKeyInfo(c, ConsoleKey.None, false, false, false);
            },
            () => false,
            null);
    }

    /// <summary>Proves the redirected path never touches the keystroke source.</summary>
    private static ConsoleKeyInfo ThrowingKeySource()
    {
        throw new InvalidOperationException("ReadKey must not be called when stdin is redirected");
    }
}
