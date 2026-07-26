using System.Runtime.InteropServices;
using System.Text;

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
    private readonly TextWriter _prompts;
    private readonly Func<ConsoleKeyInfo> _readKey;
    private readonly Func<bool> _isInputRedirected;
    private readonly Stream _redirectedInput;

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
    {
        _prompts = prompts;
        _readKey = readKey;
        _isInputRedirected = isInputRedirected;

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
