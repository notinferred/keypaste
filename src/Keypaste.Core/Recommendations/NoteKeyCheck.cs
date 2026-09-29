namespace Keypaste.Core.Recommendations;

/// <summary>Finds keys left in entry notes, in the process holding the vault (C.2, D-0372).</summary>
/// <remarks>
/// <para>
/// It reads the notes of every entry outside the recycle bin and keypaste's own groups, and finds
/// two kinds of line: a single-line <c>KEY=value</c> or <c>export KEY=value</c> whose key is
/// env-named (<see cref="EnvConvention.IsEnvNamedField"/>) and writable, and a line that is exactly
/// one token from <see cref="TokenPrefixes"/>. Lines inside a PEM block are left alone. A value is
/// either unquoted and free of whitespace, or one quoted string taken literally; anything else is
/// not reported, because the move must never write a guessed value.
/// </para>
/// <para>
/// A finding carries a stamp of the notes it came from: an HMAC under a key this instance draws at
/// random and zeroes when disposed. The move compares it with the notes it finds, so it refuses
/// once they have changed, and the stamp cannot confirm a guessed note outside this process.
/// </para>
/// </remarks>
public sealed class NoteKeyCheck : IDisposable
{
    private const string _pemBegin = "-----BEGIN ";
    private const string _pemEnd = "-----END ";

    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private bool _disposed;

    /// <summary>Checks every live entry's notes.</summary>
    /// <param name="vault">The unlocked vault.</param>
    /// <returns>The findings, by entry in tree order and then by line.</returns>
    public IReadOnlyList<NoteKeyFinding> Scan(Vault vault)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(vault);

        List<NoteKeyFinding> findings = [];

        foreach (var notes in vault.ReadNotes())
        {
            if (ReservedGroups.IsReserved(notes.Entry.GroupPath))
            {
                continue;
            }

            var keys = Parse(notes.Notes);

            if (keys.Count == 0)
            {
                continue;
            }

            var stamp = Stamp(notes.Notes);
            var repeated = Repeated(keys);

            findings.AddRange(keys.Select(key => new NoteKeyFinding(this, notes.Entry, notes.Uuid, key, repeated.Contains(key.Field), stamp)));
        }

        return findings;
    }

    /// <summary>Zeroes the stamp key; findings from this check can no longer be moved.</summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            CryptographicOperations.ZeroMemory(_key);
            _disposed = true;
        }
    }

    internal bool Matches(NoteKeyFinding finding, string notes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return CryptographicOperations.FixedTimeEquals(finding.Stamp, Stamp(notes));
    }

    internal static HashSet<string> Repeated(IReadOnlyList<ParsedNoteKey> keys) =>
        [.. keys.GroupBy(key => key.Field, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key)];

    /// <summary>Every key the notes hold, one per line at most.</summary>
    internal static IReadOnlyList<ParsedNoteKey> Parse(string notes)
    {
        List<ParsedNoteKey> keys = [];
        var inPem = false;
        var line = 0;

        for (var start = 0; start < notes.Length; line++)
        {
            var newline = notes.IndexOf('\n', start);
            var end = newline < 0 ? notes.Length : newline + 1;
            var content = notes.AsSpan(start, end - start).TrimEnd('\n').TrimEnd('\r').Trim([' ', '\t']);

            if (inPem)
            {
                inPem = !content.StartsWith(_pemEnd, StringComparison.Ordinal);
            }
            else if (content.StartsWith(_pemBegin, StringComparison.Ordinal))
            {
                inPem = true;
            }
            else if (Assignment(content) is { } assignment)
            {
                keys.Add(new ParsedNoteKey(line, start, end - start, assignment.Field, NoteKeyKind.Assignment, null, assignment.Value));
            }
            else if (TokenPrefixes.Match(content.ToString()) is { } token)
            {
                keys.Add(new ParsedNoteKey(line, start, end - start, token.Field, NoteKeyKind.Token, token, content.ToString()));
            }

            start = end;
        }

        return keys;
    }

    /// <summary>The notes with the given lines removed, each with its terminator.</summary>
    internal static string Without(string notes, IEnumerable<ParsedNoteKey> keys)
    {
        var removed = keys.OrderBy(key => key.Start).ToList();
        var kept = new StringBuilder(notes.Length);
        var at = 0;

        foreach (var key in removed)
        {
            kept.Append(notes, at, key.Start - at);
            at = key.Start + key.Length;
        }

        kept.Append(notes, at, notes.Length - at);

        // A removed last line leaves the terminator of the line before it dangling.
        if (at == notes.Length && removed.Count > 0 && !notes.EndsWith('\n'))
        {
            var text = kept.ToString();
            return text.EndsWith("\r\n", StringComparison.Ordinal) ? text[..^2] : text.EndsWith('\n') ? text[..^1] : text;
        }

        return kept.ToString();
    }

    private static (string Field, string Value)? Assignment(ReadOnlySpan<char> line)
    {
        if (line.StartsWith("export", StringComparison.Ordinal) && line.Length > 6 && line[6] is ' ' or '\t')
        {
            line = line[6..].TrimStart([' ', '\t']);
        }

        var nameLength = 0;

        while (nameLength < line.Length && (char.IsAsciiLetterOrDigit(line[nameLength]) || line[nameLength] == '_'))
        {
            nameLength++;
        }

        var field = line[..nameLength].ToString();

        if (!EnvConvention.IsEnvNamedField(field) || !FieldNameRules.IsWritable(field, out _))
        {
            return null;
        }

        var rest = line[nameLength..].TrimStart([' ', '\t']);

        if (rest.IsEmpty || rest[0] != '=')
        {
            return null;
        }

        rest = rest[1..].TrimStart([' ', '\t']);

        if (rest.IsEmpty)
        {
            return null;
        }

        ReadOnlySpan<char> value;
        ReadOnlySpan<char> after;

        if (rest[0] is '"' or '\'' or '`')
        {
            var close = rest[1..].IndexOf(rest[0]);

            if (close < 0)
            {
                return null;
            }

            value = rest.Slice(1, close);
            after = rest[(close + 2)..];
        }
        else
        {
            var stop = rest.IndexOfAny(' ', '\t');
            value = stop < 0 ? rest : rest[..stop];
            after = stop < 0 ? ReadOnlySpan<char>.Empty : rest[stop..];

            if (value.IndexOfAny('"', '\'', '`') >= 0)
            {
                return null;
            }
        }

        after = after.TrimStart([' ', '\t']);

        if (value.IsEmpty || (!after.IsEmpty && after[0] != '#'))
        {
            return null;
        }

        return (field, value.ToString());
    }

    private byte[] Stamp(string notes) => HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(notes));
}
