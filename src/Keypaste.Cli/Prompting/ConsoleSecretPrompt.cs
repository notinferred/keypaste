using System.Runtime.InteropServices;
using System.Text;
using Keypaste.Core;

namespace Keypaste.Cli.Prompting;

/// <summary>
/// The real prompt: hidden input from a terminal, one line of UTF-8 from a pipe.
/// </summary>
/// <remarks>
/// <para>
/// <b>Redirection is checked before anything is written.</b> <c>Console.ReadKey(intercept: true)</c>
/// throws when stdin is redirected, but the two platforms disagree about when: Unix pre-checks
/// <c>Console.IsInputRedirected</c> and throws immediately, while Windows calls
/// <c>ReadConsoleInput</c> and throws only after it fails — by which point the prompt has already
/// been printed. Deciding up front makes both platforms behave identically and keeps piped runs
/// from littering stderr with prompts nobody read.
/// </para>
/// <para>
/// <b>The redirected path decodes UTF-8 explicitly</b> rather than using <c>Console.In</c>, which
/// on Windows decodes with the console input code page — typically an OEM page — and silently
/// mangles a non-ASCII password arriving through a pipe. A pipe has no code page, and every shell
/// on all three platforms writes UTF-8.
/// </para>
/// <para>
/// <b>Nothing is echoed, not even asterisks</b>, which would leak the secret's length to anyone
/// reading the screen or a recorded terminal session. Backspace therefore has no visible effect.
/// </para>
/// <para>
/// <b>The redirected path reads one byte at a time and buffers nothing.</b> A
/// <see cref="StreamReader"/> would be the obvious way to read a line, and it reads ahead — up to
/// a bufferful of stdin disappears into managed memory that nothing else can reach. For every
/// verb that is invisible, because nothing downstream wants stdin. For <c>keypaste run</c>, whose
/// child inherits it, <c>printf 'pw\nhello\n' | keypaste run p -- cat</c> would print nothing at
/// all. Byte-wise reads cost nothing at the length of a password.
/// </para>
/// </remarks>
internal sealed class ConsoleSecretPrompt : ISecretPrompt
{
    /// <summary>How often a choice looks for a key, and so how soon it notices a withdrawal.</summary>
    internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>How long a choice is on screen before a key allows anything: a key pressed for the last prompt must not answer this one (D-0326).</summary>
    internal static readonly TimeSpan ArmingDelay = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan _redrawInterval = TimeSpan.FromSeconds(1);

    private readonly TextWriter _prompts;
    private readonly Func<ConsoleKeyInfo> _readKey;
    private readonly Func<bool> _keyAvailable;
    private readonly Func<bool> _isInputRedirected;
    private readonly Stream _redirectedInput;
    private readonly TimeProvider _clock;

    /// <summary>Creates a prompt writing to <paramref name="prompts"/> (in practice stderr).</summary>
    internal ConsoleSecretPrompt(TextWriter prompts)
        : this(prompts, () => Console.ReadKey(intercept: true), () => Console.IsInputRedirected, null)
    {
    }

    internal ConsoleSecretPrompt(
        TextWriter prompts,
        Func<ConsoleKeyInfo> readKey,
        Func<bool> isInputRedirected,
        Stream? redirectedInput)
        : this(prompts, readKey, () => Console.KeyAvailable, isInputRedirected, redirectedInput, TimeProvider.System)
    {
    }

    internal ConsoleSecretPrompt(
        TextWriter prompts,
        Func<ConsoleKeyInfo> readKey,
        Func<bool> keyAvailable,
        Func<bool> isInputRedirected,
        Stream? redirectedInput,
        TimeProvider clock)
    {
        _prompts = prompts;
        _readKey = readKey;
        _keyAvailable = keyAvailable;
        _isInputRedirected = isInputRedirected;
        _clock = clock;

        // The raw stdin stream, decoded as UTF-8 by hand below. Console.In would decode with the
        // console input code page — typically an OEM page on Windows — and silently mangle a
        // non-ASCII password arriving through a pipe.
        _redirectedInput = redirectedInput ?? Console.OpenStandardInput();
    }

    /// <inheritdoc/>
    public bool IsInteractive => !_isInputRedirected();

    /// <inheritdoc/>
    public SecretBuffer? ReadSecret(string prompt)
    {
        if (!IsInteractive)
        {
            var line = ReadRedirectedLine();
            if (line is null)
            {
                return null;
            }

            var piped = new SecretBuffer();
            piped.Append(line);
            return piped;
        }

        _prompts.Write(prompt);
        _prompts.Flush();

        var buffer = new SecretBuffer();
        try
        {
            while (true)
            {
                var key = _readKey();

                // Characters are matched before ConsoleKey values: Unix terminfo often reports
                // ConsoleKey.None for control characters that still carry the right KeyChar.
                switch (key.KeyChar)
                {
                    case '\r':
                    case '\n':
                        _prompts.WriteLine();
                        return buffer;

                    // '\b' is what Windows sends; U+007F is what most Unix terminals send.
                    case '\b':
                    case '\u007F':
                        buffer.Backspace();
                        continue;

                    // Ctrl+U, the readline convention for "discard the line".
                    case '\u0015':
                        buffer.Clear();
                        continue;

                    // Ctrl+C and Escape cancel. Ctrl+C usually never reaches here,
                    // because the runtime raises SIGINT or CTRL_C_EVENT first and
                    // terminates; Escape is the cancel key that reliably works.
                    case '\u0003':
                    case '\u001B':
                        _prompts.WriteLine();
                        buffer.Dispose();
                        return null;

                    // Modifier and navigation keys arrive with no character; appending '\0'
                    // would put invisible characters into the password.
                    case '\0':
                        continue;

                    default:
                        buffer.Append(key.KeyChar);
                        continue;
                }
            }
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public string? ReadLine(string prompt)
    {
        if (IsInteractive)
        {
            _prompts.Write(prompt);
            _prompts.Flush();
            return Console.ReadLine();
        }

        return ReadRedirectedLine();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Keys are polled rather than awaited, so a withdrawn question stops the read instead of leaving
    /// a reader parked on the terminal. Keys typed before the choice was drawn are discarded, and a
    /// key that allows counts only once the choice has been on screen for <see cref="ArmingDelay"/>;
    /// a key that denies counts at once. Each draw is one write with no escape sequence, which a
    /// Windows console without virtual terminal processing would print, and is what lets
    /// <c>AgentConsole</c> keep other lines from splicing into it.
    /// </remarks>
    public char? ReadChoice(Func<string> prompt, string choices, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(choices);

        if (!IsInteractive)
        {
            return ReadRedirectedLine() is { } line ? Choice(line, choices) : null;
        }

        while (_keyAvailable())
        {
            _readKey();
        }

        var width = 0;
        var shownAt = Draw(prompt, ref width);
        var drawnAt = shownAt;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (_keyAvailable())
            {
                var key = _readKey().KeyChar;

                if (IsDenyKey(key))
                {
                    return 'd';
                }

                var lower = char.ToLowerInvariant(key);
                if (choices.Contains(lower, StringComparison.Ordinal) && _clock.GetElapsedTime(shownAt) >= ArmingDelay)
                {
                    return lower;
                }

                continue;
            }

            if (_clock.GetElapsedTime(drawnAt) >= _redrawInterval)
            {
                drawnAt = Draw(prompt, ref width);
            }

            cancellationToken.WaitHandle.WaitOne(PollInterval);
        }

        return null;
    }

    /// <summary>What one line of redirected input chooses: a key in <paramref name="choices"/>, spelled as the key or as its word, and otherwise <c>'d'</c>.</summary>
    internal static char Choice(string line, string choices)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(choices);

        var word = line.Trim().Split(' ', 2)[0].ToLowerInvariant() switch
        {
            "once" => "o",
            "hour" => "h",
            var other => other,
        };

        return word.Length == 1 && choices.Contains(word[0], StringComparison.Ordinal) ? word[0] : 'd';
    }

    private static bool IsDenyKey(char key) =>
        key is '\r' or '\n' or '\u001B' or '\u0003' or 'd' or 'D' or 'n' or 'N';

    /// <summary>Draws the line over the last one, blanking whatever of a wider last line it would leave behind.</summary>
    private long Draw(Func<string> prompt, ref int width)
    {
        var line = prompt();
        _prompts.Write(line.Length < width ? "\r" + line.PadRight(width) + "\r" + line : "\r" + line);
        _prompts.Flush();
        width = line.Length;
        return _clock.GetTimestamp();
    }

    /// <summary>
    /// Reads exactly one line from the redirected stream, consuming not one byte more.
    /// </summary>
    /// <remarks>
    /// Everything after the newline is left in the pipe for whoever reads next — which for
    /// <c>keypaste run</c> is the child process. Decoding happens once at the end rather than per
    /// byte, so a multi-byte character split across the loop still arrives intact.
    /// </remarks>
    private string? ReadRedirectedLine()
    {
        var bytes = new List<byte>(SecretBuffer.InitialCapacity);

        while (true)
        {
            var next = _redirectedInput.ReadByte();

            if (next < 0)
            {
                // End of input. A final line with no newline still counts; nothing at all does not.
                return bytes.Count == 0 ? null : Decode(bytes);
            }

            if (next == '\n')
            {
                return Decode(bytes);
            }

            bytes.Add((byte)next);
        }
    }

    private static string Decode(List<byte> bytes)
    {
        // A CRLF pipe leaves the carriage return behind, and it would otherwise become part of
        // the password.
        if (bytes.Count > 0 && bytes[^1] == '\r')
        {
            bytes.RemoveAt(bytes.Count - 1);
        }

        return Encoding.UTF8.GetString(CollectionsMarshal.AsSpan(bytes));
    }
}
