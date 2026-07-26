namespace Keypaste.Core;

/// <summary>Something a <c>.env</c> file said that the reader had to change or interpret.</summary>
public enum DotEnvNoteKind
{
    /// <summary>
    /// An unquoted value carried a trailing <c> #</c> comment, which was removed. Quoting the
    /// value would have kept it.
    /// </summary>
    InlineCommentRemoved = 1,

    /// <summary>
    /// A value contains <c>${NAME}</c> or <c>$NAME</c>, which is stored literally rather than
    /// expanded. See <see cref="DotEnv"/> for why.
    /// </summary>
    LiteralInterpolation = 2,
}

/// <summary>One variable read out of a <c>.env</c> file.</summary>
/// <param name="Key">The variable name.</param>
/// <param name="Value">The value, after quote and escape processing.</param>
/// <param name="Line">The 1-based line the assignment started on.</param>
public sealed record DotEnvVariable(string Key, string Value, int Line);

/// <summary>A reason a <c>.env</c> file could not be imported.</summary>
/// <param name="Line">The 1-based line the problem is on.</param>
/// <param name="Message">
/// What is wrong, in the shape <c>line 7: ...</c>. Never contains the value or the raw line — see
/// <see cref="DotEnv"/>.
/// </param>
public sealed record DotEnvProblem(int Line, string Message);

/// <summary>Something worth telling the user about a variable that was read successfully.</summary>
/// <param name="Line">The 1-based line the variable is on.</param>
/// <param name="Key">The variable name.</param>
/// <param name="Kind">What happened.</param>
public sealed record DotEnvNote(int Line, string Key, DotEnvNoteKind Kind);

/// <summary>The result of reading a <c>.env</c> file.</summary>
public sealed class DotEnvDocument
{
    internal DotEnvDocument(
        IReadOnlyList<DotEnvVariable> variables,
        IReadOnlyList<DotEnvProblem> problems,
        IReadOnlyList<DotEnvNote> notes)
    {
        Variables = variables;
        Problems = problems;
        Notes = notes;
    }

    /// <summary>The variables that parsed, in the order they appeared.</summary>
    /// <remarks>
    /// May be non-empty even when <see cref="Problems"/> is too. A caller on the secret path must
    /// import <b>nothing</b> in that case — see <see cref="DotEnv"/>.
    /// </remarks>
    public IReadOnlyList<DotEnvVariable> Variables { get; }

    /// <summary>Everything wrong with the file, not only the first thing.</summary>
    public IReadOnlyList<DotEnvProblem> Problems { get; }

    /// <summary>Things the reader interpreted, which the user may want to know about.</summary>
    public IReadOnlyList<DotEnvNote> Notes { get; }
}

/// <summary>
/// Reads <c>.env</c> files: <c>export</c> prefixes, comments, the three quoting styles, escapes,
/// and values that span lines.
/// </summary>
/// <remarks>
/// <para>
/// There is no <c>.env</c> standard, only implementations that disagree, so the rules below were
/// chosen against the two that most likely produced the file being read — <c>motdotla/dotenv</c>
/// (JavaScript) and <c>joho/godotenv</c> (Go). Three divergences are deliberate:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>An unquoted <c>#</c> starts a comment only when a space or tab precedes it.</b> dotenv
/// truncates at any <c>#</c>, which silently turns <c>PASSWORD=hunter2#42</c> into <c>hunter2</c>
/// — a shortened secret that fails much later somewhere else. When a comment is removed, an
/// <see cref="DotEnvNoteKind.InlineCommentRemoved"/> note says so.
/// </description></item>
/// <item><description>
/// <b>A key repeated in one file is an error.</b> dotenv keeps the first, godotenv keeps the last;
/// since they disagree there is no answer to give, so it fails closed (CORE.md law 3.7), exactly
/// as <see cref="EnvStore.Read"/> does for two entries sharing a name.
/// </description></item>
/// <item><description>
/// <b>A key outside <see cref="EnvConvention.IsValidKey"/> is an error.</b> dotenv's key pattern
/// allows <c>-</c> and <c>.</c>; a variable named that way cannot be exported to a child process,
/// which is the only reason to store it.
/// </description></item>
/// </list>
/// <para>
/// <b><c>${NAME}</c> and <c>$NAME</c> are stored literally, never expanded.</b> Expanding against
/// the importing machine's environment would bake one laptop's <c>$HOME</c> — or a CI runner's —
/// into a vault that is then synced elsewhere, so the same file would mean different things on
/// different machines. Expanding against the vault's own variables would invent an evaluation
/// order inside a KDBX group that has none and that KeePassXC cannot see or maintain. Both are
/// guessing about a secret.
/// </para>
/// <para>
/// <b>A problem never quotes the text it rejected.</b> The obvious phrasing of "unterminated quote
/// on line 7" includes the line, and the line is the secret. Messages name the line number, the
/// key, and the rule — nothing else — and a test asserts it.
/// </para>
/// <para>
/// <b>What is not claimed:</b> the file's plaintext exists here as ordinary strings and cannot be
/// zeroed, which is the same limitation SECURITY.md already states for every value keypaste
/// handles. What keypaste does promise is narrower and true: it writes nothing in plaintext of its
/// own: the parser touches no file, and the only write its caller performs is the encrypted vault.
/// </para>
/// </remarks>
public static class DotEnv
{
    /// <summary>The largest file the reader will accept, in bytes.</summary>
    /// <remarks>
    /// A <c>.env</c> is a hand-maintained list of variables. A megabyte of it is a wrong path — a
    /// binary, a log, a database dump — and reading it whole into memory to say so is the wrong
    /// order of operations.
    /// </remarks>
    public const int MaximumBytes = 1024 * 1024;

    internal const char ByteOrderMark = '\uFEFF';

    /// <summary>Decodes the bytes of a <c>.env</c> file to text.</summary>
    /// <param name="bytes">The file's contents.</param>
    /// <param name="text">The decoded text, or an empty string on failure.</param>
    /// <param name="error">What went wrong, or an empty string on success.</param>
    /// <returns><see langword="true"/> if the bytes decoded.</returns>
    /// <remarks>
    /// UTF-8 unless a byte order mark says otherwise. UTF-16 is recognised because Windows
    /// PowerShell 5.1 writes it from <c>&gt;</c> and <c>Set-Content</c>, and without that every
    /// line of such a file would be reported as malformed. Invalid UTF-8 is an error rather than
    /// being replaced with <c>U+FFFD</c>: a secret quietly rewritten is worse than a refusal.
    /// </remarks>
    public static bool TryDecode(ReadOnlySpan<byte> bytes, out string text, out string error)
    {
        text = string.Empty;
        error = string.Empty;

        if (bytes.Length > MaximumBytes)
        {
            error = $"the file is larger than {MaximumBytes / 1024} KiB, which is not a .env file";
            return false;
        }

        // Written out rather than compared against a literal array: a constant array passed as an
        // argument is a build error here (CA1861), and hoisting it to a field runs into the
        // private-field naming rule instead.
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            bytes = bytes[3..];
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return TryDecode(Encoding.Unicode, bytes[2..], out text, out error);
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return TryDecode(Encoding.BigEndianUnicode, bytes[2..], out text, out error);
        }

        return TryDecode(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            bytes, out text, out error);
    }

    /// <summary>Parses the text of a <c>.env</c> file.</summary>
    /// <param name="text">The decoded file contents.</param>
    /// <param name="document">The variables, problems, and notes found.</param>
    /// <returns>
    /// <see langword="true"/> only when the file is entirely well-formed. When it is
    /// <see langword="false"/>, <see cref="DotEnvDocument.Variables"/> may still hold the lines
    /// that did parse — they are there to be counted, not to be imported.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public static bool TryParse(string text, out DotEnvDocument document)
    {
        ArgumentNullException.ThrowIfNull(text);

        var scanner = new Scanner(Normalize(text));
        document = scanner.Run();
        return document.Problems.Count == 0;
    }

    private static bool TryDecode(Encoding encoding, ReadOnlySpan<byte> bytes, out string text, out string error)
    {
        try
        {
            text = encoding.GetString(bytes);
            error = string.Empty;
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            error = "the file is not valid UTF-8 text";
            return false;
        }
    }

    /// <summary>
    /// Collapses CRLF to LF everywhere, including inside values that span lines, and drops a
    /// leading byte order mark that survived decoding.
    /// </summary>
    /// <remarks>
    /// Done once, up front, so no scanning rule below has to mention <c>\r</c>. Line numbering is
    /// unaffected because only the carriage return is removed. Stripping the mark defensively
    /// matters for callers that decoded the text themselves: otherwise the first key becomes
    /// <c>\uFEFFDATABASE_URL</c> and is rejected for a reason nobody can see.
    /// </remarks>
    private static string Normalize(string text)
    {
        if (text.StartsWith(ByteOrderMark))
        {
            text = text[1..];
        }

        return text.Contains('\r', StringComparison.Ordinal)
            ? text.Replace("\r\n", "\n", StringComparison.Ordinal)
            : text;
    }

    /// <summary>
    /// Walks the file once, carrying the position across line boundaries so a quoted value can
    /// span them.
    /// </summary>
    private sealed class Scanner(string text)
    {
        private readonly List<DotEnvVariable> _variables = [];
        private readonly List<DotEnvProblem> _problems = [];
        private readonly List<DotEnvNote> _notes = [];
        private readonly Dictionary<string, int> _seen = new(StringComparer.Ordinal);

        private int _index;
        private int _line = 1;

        internal DotEnvDocument Run()
        {
            while (_index < text.Length)
            {
                ReadAssignment();
            }

            return new DotEnvDocument(_variables, _problems, _notes);
        }

        private void ReadAssignment()
        {
            int startLine = _line;

            SkipBlanks();
            if (_index >= text.Length)
            {
                return;
            }

            int keyStart = _index;
            SkipToEndOfLine();
            string statement = text[keyStart.._index];
            SkipNewLine();

            int equals = statement.IndexOf('=');
            if (equals < 0)
            {
                Fail(startLine, "expected KEY=value");
                return;
            }

            string key = StripExport(statement[..equals]).Trim();
            if (key.Length == 0)
            {
                Fail(startLine, "the variable name is empty");
                return;
            }

            if (!EnvConvention.IsValidKey(key, out string keyError))
            {
                Fail(startLine, keyError);
                return;
            }

            // Rewind to just after the '=' so a quoted value can carry on past the end of this
            // line; the statement above only ever covers one line, and a multiline value does not.
            _index = keyStart + equals + 1;
            _line = startLine;

            if (!TryReadValue(key, startLine, out string value))
            {
                return;
            }

            if (_seen.TryGetValue(key, out int firstLine))
            {
                Fail(startLine, $"'{key}' is set more than once (first on line {firstLine})");
                return;
            }

            if (value.Contains('\0', StringComparison.Ordinal))
            {
                Fail(startLine, $"'{key}' contains a NUL character, which cannot be exported");
                return;
            }

            _seen[key] = startLine;
            _variables.Add(new DotEnvVariable(key, value, startLine));

            if (LooksInterpolated(value))
            {
                _notes.Add(new DotEnvNote(startLine, key, DotEnvNoteKind.LiteralInterpolation));
            }
        }

        /// <summary>Reads the value after an <c>=</c>, leaving the scanner past its final line.</summary>
        private bool TryReadValue(string key, int startLine, out string value)
        {
            value = string.Empty;

            SkipSpaces();

            char quote = _index < text.Length ? text[_index] : '\n';
            if (quote is '\'' or '"' or '`')
            {
                _index++;
                return TryReadQuoted(key, quote, startLine, out value);
            }

            value = ReadUnquoted(key, startLine);
            SkipNewLine();
            return true;
        }

        private bool TryReadQuoted(string key, char quote, int startLine, out string value)
        {
            value = string.Empty;
            var builder = new StringBuilder();

            while (_index < text.Length)
            {
                char c = text[_index++];

                if (c == quote)
                {
                    return TryFinishQuoted(builder, startLine, out value);
                }

                if (c == '\n')
                {
                    _line++;
                    builder.Append('\n');
                    continue;
                }

                // Only double quotes process escapes. A single- or backtick-quoted value is
                // literal, which is the whole reason those forms exist.
                if (c == '\\' && quote == '"' && _index < text.Length)
                {
                    char escaped = text[_index];
                    switch (escaped)
                    {
                        case 'n': builder.Append('\n'); _index++; continue;
                        case 'r': builder.Append('\r'); _index++; continue;
                        case 't': builder.Append('\t'); _index++; continue;
                        case '\\': builder.Append('\\'); _index++; continue;
                        case '"': builder.Append('"'); _index++; continue;

                        // Anything else keeps the backslash, so "C:\temp" survives being read.
                        default: builder.Append('\\'); continue;
                    }
                }

                builder.Append(c);
            }

            // Reported against the line the quote opened on, not the end of the file, because that
            // is the line the user has to fix.
            Fail(startLine, $"the value of '{key}' opens with {Describe(quote)} that is never closed");
            return false;
        }

        /// <summary>Checks that nothing but whitespace and a comment follows the closing quote.</summary>
        private bool TryFinishQuoted(StringBuilder builder, int startLine, out string value)
        {
            value = string.Empty;

            SkipSpaces();

            if (_index < text.Length && text[_index] != '\n' && text[_index] != '#')
            {
                // dotenv ignores it silently. Here it is the difference between a value that ends
                // where the user thinks it does and one that does not.
                Fail(startLine, "there is text after the closing quote");
                SkipToEndOfLine();
                SkipNewLine();
                return false;
            }

            SkipToEndOfLine();
            SkipNewLine();
            value = builder.ToString();
            return true;
        }

        /// <summary>
        /// Reads to the end of the line, honouring a trailing <c> #</c> comment.
        /// </summary>
        /// <remarks>
        /// The <c>#</c> must be preceded by a space or tab. Without that rule
        /// <c>PASSWORD=hunter2#42</c> silently becomes <c>hunter2</c>.
        /// </remarks>
        private string ReadUnquoted(string key, int startLine)
        {
            int start = _index;
            int commentAt = -1;

            while (_index < text.Length && text[_index] != '\n')
            {
                // The character before the '#' is read from the file, not from the value, so
                // `KEY= #c` is an empty value with a comment while `COLOR=#ff0000` is a colour:
                // in the second the '#' follows the '=' with nothing between them.
                if (text[_index] == '#' && (text[_index - 1] is ' ' or '\t'))
                {
                    commentAt = _index;
                    break;
                }

                _index++;
            }

            string raw = text[start..(commentAt < 0 ? _index : commentAt)];

            if (commentAt >= 0)
            {
                _notes.Add(new DotEnvNote(startLine, key, DotEnvNoteKind.InlineCommentRemoved));
                SkipToEndOfLine();
            }

            return raw.Trim();
        }

        private void SkipBlanks()
        {
            while (_index < text.Length)
            {
                int lineStart = _index;
                SkipSpaces();

                if (_index < text.Length && text[_index] != '\n' && text[_index] != '#')
                {
                    _index = lineStart;
                    return;
                }

                SkipToEndOfLine();
                if (_index >= text.Length)
                {
                    return;
                }

                SkipNewLine();
            }
        }

        private void SkipSpaces()
        {
            while (_index < text.Length && (text[_index] is ' ' or '\t'))
            {
                _index++;
            }
        }

        private void SkipToEndOfLine()
        {
            while (_index < text.Length && text[_index] != '\n')
            {
                _index++;
            }
        }

        private void SkipNewLine()
        {
            if (_index < text.Length && text[_index] == '\n')
            {
                _index++;
                _line++;
            }
        }

        private void Fail(int line, string message) =>
            _problems.Add(new DotEnvProblem(line, $"line {line}: {message}"));

        /// <summary>Strips a leading <c>export</c> keyword, which must be followed by whitespace.</summary>
        /// <remarks>
        /// <c>exportKEY=v</c> is a variable called <c>exportKEY</c>, not an exported <c>KEY</c>.
        /// </remarks>
        private static string StripExport(string beforeEquals)
        {
            string trimmed = beforeEquals.TrimStart();
            if (!trimmed.StartsWith("export", StringComparison.Ordinal) || trimmed.Length == 6)
            {
                return beforeEquals;
            }

            return trimmed[6] is ' ' or '\t' ? trimmed[7..] : beforeEquals;
        }

        private static bool LooksInterpolated(string value)
        {
            int dollar = value.IndexOf('$');
            if (dollar < 0 || dollar + 1 >= value.Length)
            {
                return false;
            }

            char next = value[dollar + 1];
            return next == '{' || char.IsAsciiLetter(next) || next == '_';
        }

        private static string Describe(char quote) => quote switch
        {
            '\'' => "a single quote",
            '"' => "a double quote",
            _ => "a backtick",
        };
    }
}
