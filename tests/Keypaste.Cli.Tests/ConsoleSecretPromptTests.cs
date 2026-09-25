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

    [Fact]
    public void ReadChoice_ReturnsAChoiceKey_IgnoringOthers()
    {
        using var prompts = new StringWriter(CultureInfo.InvariantCulture);
        var prompt = Choosing(prompts, "x?1 O");

        Assert.Equal('o', prompt.ReadChoice(() => "[d] deny  [o] once  ", "oh", TestContext.Current.CancellationToken));

        // Drawn without echoing what was typed.
        Assert.Equal("\r[d] deny  [o] once  ", prompts.ToString());
    }

    /// <summary>
    /// A key typed before the choice was drawn — a double-tap or auto-repeat on the last prompt —
    /// answers nothing, however long the choice then stays up.
    /// </summary>
    [Fact]
    public void ReadChoice_DiscardsKeysTypedBeforeItWasDrawn()
    {
        using var prompts = new StringWriter(CultureInfo.InvariantCulture);
        var clock = new SteppedClock();
        var typedAhead = new Queue<char>("hH");
        var drawn = false;
        var prompt = new ConsoleSecretPrompt(
            prompts,
            () =>
            {
                clock.Advance(ConsoleSecretPrompt.ArmingDelay);
                return Key(typedAhead.Count > 0 ? typedAhead.Dequeue() : 'o');
            },
            () => typedAhead.Count > 0 || drawn,
            () => false,
            null,
            clock);

        Assert.Equal('o', prompt.ReadChoice(
            () =>
            {
                drawn = true;
                return "choose ";
            },
            "oh",
            TestContext.Current.CancellationToken));
    }

    /// <summary>An allowing key counts only after the choice has been on screen for the arming delay (D-0326).</summary>
    [Fact]
    public void ReadChoice_IgnoresAnAllowingKey_UntilArmed()
    {
        var keys = new Queue<(char Key, TimeSpan After)>(
        [
            ('h', TimeSpan.Zero),
            ('o', ConsoleSecretPrompt.ArmingDelay - TimeSpan.FromMilliseconds(1)),
            ('h', TimeSpan.FromMilliseconds(1)),
        ]);

        Assert.Equal('h', Timed(keys));
        Assert.Empty(keys);
    }

    [Fact]
    public void ReadChoice_DenyingKey_CountsAtOnce()
    {
        var keys = new Queue<(char Key, TimeSpan After)>([('o', TimeSpan.Zero), ('d', TimeSpan.Zero)]);

        Assert.Equal('d', Timed(keys));
    }

    [Theory]
    [InlineData(Enter)]
    [InlineData('\n')]
    [InlineData(Escape)]
    [InlineData((char)0x03)]
    [InlineData('n')]
    [InlineData('d')]
    public void ReadChoice_DenyKeys_ReturnD(char key)
    {
        using var prompts = new StringWriter(CultureInfo.InvariantCulture);
        var prompt = Choosing(prompts, key + "o");

        Assert.Equal('d', prompt.ReadChoice(() => "choose ", "oh", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ReadChoice_Redirected_ReadsOneLine()
    {
        using var prompts = new StringWriter(CultureInfo.InvariantCulture);
        using var input = Piped("  Once \nhour please\ny\nh");
        var prompt = new ConsoleSecretPrompt(prompts, ThrowingKeySource, () => true, input);
        var token = TestContext.Current.CancellationToken;

        Assert.Equal('o', prompt.ReadChoice(() => "choose ", "oh", token));
        Assert.Equal('h', prompt.ReadChoice(() => "choose ", "oh", token));
        Assert.Equal('d', prompt.ReadChoice(() => "choose ", "oh", token));
        Assert.Equal('d', prompt.ReadChoice(() => "choose ", "o", token));
        Assert.Null(prompt.ReadChoice(() => "choose ", "oh", token));
        Assert.Empty(prompts.ToString());
    }

    [Fact]
    public async Task ReadChoice_Cancelled_StopsPolling()
    {
        using var prompts = new StringWriter(CultureInfo.InvariantCulture);
        using var withdraw = new CancellationTokenSource();
        var polls = 0;
        var prompt = new ConsoleSecretPrompt(
            prompts,
            ThrowingKeySource,
            () =>
            {
                Interlocked.Increment(ref polls);
                return false;
            },
            () => false,
            null,
            TimeProvider.System);

        var reading = Task.Run(() => prompt.ReadChoice(() => "choose ", "oh", withdraw.Token), TestContext.Current.CancellationToken);
        await Task.Delay(ConsoleSecretPrompt.PollInterval * 3, TestContext.Current.CancellationToken);
        await withdraw.CancelAsync();

        Assert.Null(await reading.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        var stopped = Volatile.Read(ref polls);
        await Task.Delay(ConsoleSecretPrompt.PollInterval * 3, TestContext.Current.CancellationToken);
        Assert.Equal(stopped, Volatile.Read(ref polls));
    }

    [Fact]
    public void ReadChoice_RedrawsTheCountdown()
    {
        using var prompts = new StringWriter(CultureInfo.InvariantCulture);
        var clock = new SteppedClock();
        var polls = 0;
        var left = 10;
        var prompt = new ConsoleSecretPrompt(
            prompts,
            () => Key('o'),
            () =>
            {
                if (prompts.GetStringBuilder().Length == 0)
                {
                    return false;
                }

                if (++polls > 3)
                {
                    return true;
                }

                clock.Advance(TimeSpan.FromSeconds(1));
                return false;
            },
            () => false,
            null,
            clock);

        Assert.Equal('o', prompt.ReadChoice(() => $"{left--}s ", "oh", TestContext.Current.CancellationToken));

        // No escape sequence, which a console without virtual terminal processing prints: a line
        // narrower than the last is padded over it with spaces and drawn again.
        Assert.Equal("\r10s \r9s  \r9s \r8s \r7s ", prompts.ToString());
    }

    private static ConsoleKeyInfo Key(char key) => new(key, ConsoleKey.None, false, false, false);

    /// <summary>Keys that arrive once the choice is drawn, all of them armed.</summary>
    private static ConsoleSecretPrompt Choosing(StringWriter prompts, string keystrokes)
    {
        var clock = new SteppedClock();
        var index = 0;
        return new ConsoleSecretPrompt(
            prompts,
            () =>
            {
                clock.Advance(ConsoleSecretPrompt.ArmingDelay);
                return Key(keystrokes[index++]);
            },
            () => prompts.GetStringBuilder().Length > 0 && index < keystrokes.Length,
            () => false,
            null,
            clock);
    }

    /// <summary>Reads a choice from keys that each arrive a set time after the one before, the first after the draw.</summary>
    private static char? Timed(Queue<(char Key, TimeSpan After)> keys)
    {
        using var prompts = new StringWriter(CultureInfo.InvariantCulture);
        var clock = new SteppedClock();
        var prompt = new ConsoleSecretPrompt(
            prompts,
            () =>
            {
                var (key, after) = keys.Dequeue();
                clock.Advance(after);
                return Key(key);
            },
            () => prompts.GetStringBuilder().Length > 0 && keys.Count > 0,
            () => false,
            null,
            clock);

        return prompt.ReadChoice(() => "choose ", "oh", TestContext.Current.CancellationToken);
    }

    /// <summary>A monotonic clock the test moves, so a redraw is due exactly when the test says.</summary>
    private sealed class SteppedClock : TimeProvider
    {
        private long _stamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Read(ref _stamp);

        internal void Advance(TimeSpan by) => Interlocked.Add(ref _stamp, by.Ticks);
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
