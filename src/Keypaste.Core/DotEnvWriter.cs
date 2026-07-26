using System.Diagnostics.CodeAnalysis;

namespace Keypaste.Core;

/// <summary>Something about a written value that only the person exporting it can judge.</summary>
public enum DotEnvWriteNoteKind
{
    /// <summary>
    /// The value had to be double-quoted, and the escapes that form needs are not processed
    /// identically by every reader. See <see cref="DotEnvWriter"/>.
    /// </summary>
    EscapeDialect = 1,

    /// <summary>
    /// The value ends in a backslash, which sits against the closing quote. Some readers let the
    /// match run past it into the following lines.
    /// </summary>
    TrailingBackslash = 2,
}

/// <summary>Something worth telling the user about one written variable.</summary>
/// <param name="Key">The variable name. Never the value.</param>
/// <param name="Kind">What happened.</param>
public sealed record DotEnvWriteNote(string Key, DotEnvWriteNoteKind Kind);

/// <summary>A formatted <c>.env</c> file, ready to be written or printed.</summary>
public sealed class DotEnvText
{
    internal DotEnvText(string text, ReadOnlyMemory<byte> utf8, IReadOnlyList<DotEnvWriteNote> notes)
    {
        Text = text;
        Utf8 = utf8;
        Notes = notes;
    }

    /// <summary>The file as text, with LF line endings and a trailing newline.</summary>
    public string Text { get; }

    /// <summary>
    /// The same file as bytes: UTF-8, no byte order mark.
    /// </summary>
    /// <remarks>
    /// Exposed so a caller cannot pick the wrong encoding. <see cref="Encoding.UTF8"/> is built
    /// with <c>encoderShouldEmitUTF8Identifier: true</c>, so the obvious
    /// <c>File.WriteAllText(path, text, Encoding.UTF8)</c> emits a mark this writer promises not
    /// to.
    /// </remarks>
    public ReadOnlyMemory<byte> Utf8 { get; }

    /// <summary>Values whose written form is not portable to every reader.</summary>
    public IReadOnlyList<DotEnvWriteNote> Notes { get; }
}

/// <summary>
/// Writes a project's variables back out as a <c>.env</c> file, the inverse of
/// <see cref="DotEnv"/>.
/// </summary>
/// <remarks>
/// <para>
/// It lives beside the reader, in the core, for the same reason the reader does (CORE.md law 4.3):
/// a second serialiser in a frontend would disagree with this one about exactly the values that are
/// hard to write, and two keypaste frontends writing different files from one vault is law 4.6's
/// failure with both parties in-house.
/// </para>
/// <para>
/// <b>Single quotes are preferred to double quotes, and that is the whole design.</b> The obvious
/// choice — double-quote everything and escape it — round-trips perfectly through
/// <see cref="DotEnv"/> and breaks other readers silently. <c>motdotla/dotenv</c> post-processes a
/// double-quoted value by expanding only <c>\n</c> and <c>\r</c>; it does not unescape <c>\\</c>,
/// <c>\"</c> or <c>\t</c>. So a stored <c>C:\logs\app</c> written as <c>"C:\\logs\\app"</c> comes
/// back with both backslashes doubled, while every keypaste test stays green. A single-quoted value
/// is literal in keypaste, <c>motdotla/dotenv</c>, <c>python-dotenv</c>, <c>joho/godotenv</c>,
/// <c>compose-go</c> and <c>sh</c> alike, needs no escaping at all, carries newlines so a PEM key
/// stays a readable block, and suppresses the <c>${VAR}</c> expansion that several of those readers
/// perform by default — which is what keeps
/// <see cref="DotEnvNoteKind.LiteralInterpolation"/> a note rather than a corruption.
/// </para>
/// <list type="bullet">
/// <item><description>Empty values are written bare: <c>KEY=</c>.</description></item>
/// <item><description>
/// Values made only of <c>A-Z a-z 0-9</c> and <c>_ . / : @ % + , - = ^</c> are written unquoted.
/// <c>~</c> is excluded although the reader accepts it: <c>~/bin</c> survives keypaste and then
/// tilde-expands in any shell that sources the file, which bakes one machine's home directory into
/// the value — the hazard this codebase already refuses to accept for <c>$</c>.
/// </description></item>
/// <item><description>
/// Anything else containing neither an apostrophe nor a carriage return is single-quoted.
/// </description></item>
/// <item><description>
/// Only a value containing an apostrophe or a carriage return is double-quoted, escaping
/// <c>\ " </c> and the LF, CR and tab characters — and it earns a
/// <see cref="DotEnvWriteNoteKind.EscapeDialect"/> note, because that is the form readers disagree
/// about.
/// </description></item>
/// </list>
/// <para>
/// <b>An error never quotes a value</b>, the same rule <see cref="DotEnvProblem"/> keeps for the
/// reading direction, and for the same reason: on this path the value is the secret.
/// </para>
/// </remarks>
public static class DotEnvWriter
{
    /// <summary>The comment block every written file starts with.</summary>
    /// <remarks>
    /// Fixed text with no timestamp, path, host or user name in it. Anything else would be durable
    /// metadata about a secrets file, and a timestamp would also make two exports of identical data
    /// differ — noisy diffs for exactly the people who commit this despite the first line.
    /// </remarks>
    public const string Header =
        "# Generated by keypaste. Do not commit this file.\n" +
        "# It holds secrets in plain text; `keypaste run` injects them without one.\n";

    /// <summary>Formats variables as a <c>.env</c> file, or explains why it will not.</summary>
    /// <param name="variables">The project's variables, written in the order given.</param>
    /// <param name="file">The formatted file, or <see langword="null"/> on failure.</param>
    /// <param name="error">Why nothing was formatted, or an empty string on success.</param>
    /// <returns><see langword="true"/> if the whole set could be written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="variables"/> is null.</exception>
    /// <remarks>
    /// It is all or nothing. A partially written <c>.env</c> is a file whose absent variables look
    /// like a decision somebody made.
    /// </remarks>
    public static bool TryFormat(
        IReadOnlyList<EnvVariable> variables,
        [NotNullWhen(true)] out DotEnvText? file,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(variables);

        file = null;

        if (!TryCheckWritable(variables, out error))
        {
            return false;
        }

        List<DotEnvWriteNote> notes = [];
        var builder = new StringBuilder(Header);

        foreach (var variable in variables)
        {
            builder.Append(variable.Key).Append('=');
            Render(variable, builder, notes);
            builder.Append('\n');
        }

        var text = builder.ToString();
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        var bytes = encoding.GetBytes(text);

        // The same comparison the reader uses, so the two agree on the boundary exactly. Without
        // this, keypaste could write a file keypaste refuses to read: the double-quoted form can
        // nearly double a value's length, and non-ASCII costs up to four bytes a character.
        if (bytes.Length > DotEnv.MaximumBytes)
        {
            error = $"would be larger than {DotEnv.MaximumBytes / 1024} KiB as a .env file, " +
                "which keypaste itself would refuse to read back";
            return false;
        }

        file = new DotEnvText(text, bytes, notes);
        return true;
    }

    private static bool TryCheckWritable(IReadOnlyList<EnvVariable> variables, out string error)
    {
        if (!EnvNameRules.TryCheck(variables, out error))
        {
            return false;
        }

        // Exact duplicates are refused here rather than in EnvNameRules because they are only a
        // problem for a file: injection would simply set the variable twice, while a .env saying
        // the same key twice is one DotEnv.TryParse rejects outright.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variable in variables)
        {
            if (!seen.Add(variable.Key))
            {
                error = $"lists '{variable.Key}' more than once, which a .env file cannot express";
                return false;
            }
        }

        foreach (var variable in variables)
        {
            if (variable.Value.Contains('\0', StringComparison.Ordinal))
            {
                error = $"holds a NUL character under '{variable.Key}', which a .env file cannot carry";
                return false;
            }

            if (HasLoneSurrogate(variable.Value))
            {
                error = $"holds a value under '{variable.Key}' that is not valid text and cannot be encoded";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static void Render(EnvVariable variable, StringBuilder builder, List<DotEnvWriteNote> notes)
    {
        var value = variable.Value;

        if (value.EndsWith('\\'))
        {
            notes.Add(new DotEnvWriteNote(variable.Key, DotEnvWriteNoteKind.TrailingBackslash));
        }

        if (value.Length == 0 || IsSafeUnquoted(value))
        {
            builder.Append(value);
            return;
        }

        if (!value.Contains('\'', StringComparison.Ordinal)
            && !value.Contains('\r', StringComparison.Ordinal))
        {
            builder.Append('\'').Append(value).Append('\'');
            return;
        }

        notes.Add(new DotEnvWriteNote(variable.Key, DotEnvWriteNoteKind.EscapeDialect));

        builder.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default: builder.Append(c); break;
            }
        }

        builder.Append('"');
    }

    /// <summary>Whether the value can be written with no quotes at all.</summary>
    /// <remarks>
    /// Excluding space is what makes the reader's inline-comment rule unreachable: a <c>#</c> can
    /// only start a comment when a space or tab precedes it, and neither can appear here.
    /// </remarks>
    private static bool IsSafeUnquoted(string value)
    {
        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c)
                && c is not ('_' or '.' or '/' or ':' or '@' or '%' or '+' or ',' or '-' or '=' or '^'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasLoneSurrogate(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (!char.IsSurrogate(value[i]))
            {
                continue;
            }

            if (!char.IsHighSurrogate(value[i]) || i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
            {
                return true;
            }

            i++;
        }

        return false;
    }
}
