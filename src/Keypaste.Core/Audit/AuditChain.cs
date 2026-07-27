namespace Keypaste.Core.Audit;

/// <summary>What one physical line of the audit log turned out to be, structurally.</summary>
/// <remarks>
/// This is a judgement about a line's <em>shape</em>, reached from its bytes alone. Whether a
/// <see cref="Chained"/> line's hash actually recomputes, and whether it links to the line before
/// it, are separate questions answered by <see cref="AuditChainVerifier"/>.
/// </remarks>
internal enum AuditLineKind
{
    /// <summary>Not a keypaste record at all. Something else wrote this.</summary>
    Foreign = 0,

    /// <summary>A schema v1 record. Written before the chain existed, and never checked against it.</summary>
    Legacy = 1,

    /// <summary>A schema v2 record carrying both chain fields in the shape this version writes them.</summary>
    Chained = 2,

    /// <summary>A record from a schema this version does not know. Not checked, and not condemned.</summary>
    Newer = 3,

    /// <summary>
    /// It begins like a record and is not shaped like one: an unfinished write.
    /// </summary>
    /// <remarks>
    /// Every record starts with the same few bytes, so a write cut short by a crash or a power loss
    /// is always a <em>prefix</em> of one — which is why this is told apart from
    /// <see cref="Foreign"/> and reported rather than condemned. It costs nothing, because a record
    /// somebody mangled into this shape still breaks the link of the record after it.
    /// </remarks>
    Torn = 4,
}

/// <summary>One inspected line.</summary>
/// <param name="Kind">What the line turned out to be.</param>
/// <param name="Hash">The <c>hash</c> the line declares, or empty when it declares none.</param>
/// <param name="Previous">The <c>prev</c> the line declares, or empty when it declares none.</param>
/// <param name="Sequence">The <c>seq</c> the line declares, or <c>0</c> when it declares none usable.</param>
/// <param name="Timestamp">The <c>ts</c> the line declares, or empty. For naming a line to a person.</param>
internal readonly record struct AuditLine(
    AuditLineKind Kind,
    string Hash,
    string Previous,
    long Sequence,
    string Timestamp);

/// <summary>
/// The bytes-level rules of the audit log's hash chain: what each line commits to, and how to tell
/// one kind of line from another without parsing it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A line commits to its own bytes, not to a re-serialization of its contents.</b> The hash covers
/// the line exactly as it stood immediately before the writer appended the <c>hash</c> member — the
/// whole line minus its final <c>,"hash":"&lt;64 hex&gt;"}</c>, and minus nothing else. No newline,
/// no carriage return. Anything that reconstructed the bytes from parsed fields in order to check
/// them would make a future change to <see cref="System.Text.Json.Utf8JsonWriter"/>'s escaping or
/// number formatting able to turn <em>intact</em> into <em>tampered</em>, which is the worst failure
/// this feature has available to it.
/// </para>
/// <para>
/// <b><c>prev</c> comes before <c>hash</c>, and <c>hash</c> is last.</b> Both are load-bearing. If
/// <c>hash</c> came first the committed bytes would not include <c>prev</c>, and a line's link could
/// be re-pointed without disturbing its hash — a chain that is decorative rather than a chain. If
/// <c>hash</c> were not last, verification could not be a fixed-width slice, and would need a parser.
/// </para>
/// <para>
/// <b>Nothing here parses JSON.</b> Classification reads leading bytes, because <c>v</c> is always
/// the first key and always a number, and the shape check reads a fixed-width footer. That keeps a
/// parser difference from ever being able to change a verdict; JSON parsing is confined to
/// <em>rendering</em> the log, where a failure is cosmetic.
/// </para>
/// </remarks>
internal static class AuditChain
{
    /// <summary>The number of hex characters in a chain field.</summary>
    public const int HashHexLength = 64;

    /// <summary>The length of <c>,"hash":"&lt;64 hex&gt;"}</c> — the bytes a line does <em>not</em> commit to.</summary>
    public const int ChainSuffixBytes = 75;

    /// <summary>The length of <c>,"prev":"&lt;64 hex&gt;","hash":"&lt;64 hex&gt;"}</c>.</summary>
    public const int ChainFooterBytes = 149;

    /// <summary>How many bytes the chain adds to a record that would otherwise be schema v1.</summary>
    /// <remarks>
    /// The footer replaces the object's closing brace, so the growth is one byte less than the
    /// footer. Pinned by a test: this is a straight deduction from
    /// <see cref="AuditLog.MaximumRecordBytes"/>, so a third chain field would silently shrink the
    /// room a record has for its own content.
    /// </remarks>
    public const int ChainOverheadBytes = ChainFooterBytes - 1;

    /// <summary>The width of the <c>ts</c> value, which is fixed by its format.</summary>
    public const int TimestampLength = 24;

    /// <summary>
    /// The <c>prev</c> of the first chained line in a file: the chain starts here.
    /// </summary>
    /// <remarks>
    /// A sentinel with a position rule, never a fallback. Written only when the whole file was
    /// examined and holds no chained record at all, and never when something could not be read. A
    /// genesis value appearing after a chained record is the signature of a file that was truncated
    /// and appended to, which is why the writer must never be able to reach it by accident — a
    /// record planted in the log must not be able to make keypaste report an attack on itself.
    /// </remarks>
    public static string Genesis { get; } = new('0', HashHexLength);

    private static ReadOnlySpan<byte> VersionPrefix => "{\"v\":"u8;

    private static ReadOnlySpan<byte> TimestampPrefix => "{\"v\":2,\"ts\":\""u8;

    private static ReadOnlySpan<byte> SequencePrefix => "\",\"seq\":"u8;

    private static ReadOnlySpan<byte> PreviousPrefix => ",\"prev\":\""u8;

    private static ReadOnlySpan<byte> HashPrefix => "\",\"hash\":\""u8;

    private static ReadOnlySpan<byte> Terminator => "\"}"u8;

    /// <summary>Says what a line is, from its bytes.</summary>
    /// <param name="line">One physical line, with no newline and no carriage return.</param>
    /// <returns>The line's kind, and the chain values it declares if it declares any.</returns>
    public static AuditLine Inspect(ReadOnlySpan<byte> line)
    {
        if (!line.StartsWith(VersionPrefix))
        {
            // A write cut short can stop anywhere, including inside the first five bytes, and what
            // it leaves is a prefix of a record rather than a foreign line. Telling those apart is
            // the difference between "your machine crashed" and "somebody wrote in your audit log".
            return Plain(VersionPrefix.StartsWith(line) ? AuditLineKind.Torn : AuditLineKind.Foreign);
        }

        var after = line[VersionPrefix.Length..];
        var digits = 0;
        while (digits < after.Length && IsDigit(after[digits]))
        {
            digits++;
        }

        // `{"v":` followed by something other than a number and a comma was not written by any
        // version of keypaste, so it is not a record whose schema we are entitled to guess at.
        if (digits == 0 || digits >= after.Length || after[digits] != (byte)',')
        {
            return Plain(AuditLineKind.Torn);
        }

        // A version too large to hold is a number no keypaste wrote, and calling it "newer" would
        // have the log tell its owner to upgrade in answer to garbage.
        if (!TryNumber(after[..digits], out var version))
        {
            return Plain(AuditLineKind.Torn);
        }

        if (version == 1)
        {
            return Plain(AuditLineKind.Legacy);
        }

        if (version < 1)
        {
            return Plain(AuditLineKind.Torn);
        }

        if (version != AuditRecord.SchemaVersion)
        {
            return Plain(AuditLineKind.Newer);
        }

        return Chained(line);
    }

    /// <summary>Recomputes what a chained line's <c>hash</c> should have been.</summary>
    /// <param name="line">A line <see cref="Inspect"/> called <see cref="AuditLineKind.Chained"/>.</param>
    /// <returns>The lowercase hex digest of the bytes the line commits to.</returns>
    /// <exception cref="ArgumentException">The line is too short to be a chained line.</exception>
    public static string Recompute(ReadOnlySpan<byte> line)
    {
        if (line.Length <= ChainSuffixBytes)
        {
            throw new ArgumentException("the line is too short to carry a chain footer", nameof(line));
        }

        return HashOf(line[..^ChainSuffixBytes]);
    }

    /// <summary>Hashes the bytes a line commits to.</summary>
    /// <param name="committed">The line without its <c>,"hash":"…"}</c> suffix.</param>
    /// <returns>The lowercase hex digest.</returns>
    public static string HashOf(ReadOnlySpan<byte> committed) =>
        Convert.ToHexStringLower(SHA256.HashData(committed));

    /// <summary>Whether a string is exactly a chain field: 64 lowercase hex characters.</summary>
    /// <param name="text">The candidate.</param>
    /// <returns><see langword="true"/> when it could be a <c>prev</c> or a <c>hash</c>.</returns>
    /// <remarks>
    /// Lowercase only. Accepting both cases would give one field two spellings, and a verifier whose
    /// inputs have two spellings has two implementations waiting to disagree.
    /// </remarks>
    public static bool IsChainValue(string? text)
    {
        if (text is not { Length: HashHexLength })
        {
            return false;
        }

        foreach (var c in text)
        {
            if (!(char.IsAsciiDigit(c) || c is >= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks a line that claims to be v2 against the exact shape this version writes, and reads out
    /// the three values the chain runs on.
    /// </summary>
    /// <remarks>
    /// The footer is validated rather than assumed. Blindly slicing the last 149 bytes off a
    /// hand-edited line reports "the hash does not match" when the truth is "this line has no chain
    /// fields at all", and those are different things to tell somebody looking for a break.
    /// </remarks>
    private static AuditLine Chained(ReadOnlySpan<byte> line)
    {
        var torn = Plain(AuditLineKind.Torn);

        if (line.Length < TimestampPrefix.Length + TimestampLength + SequencePrefix.Length + ChainFooterBytes)
        {
            return torn;
        }

        var footer = line[^ChainFooterBytes..];

        if (!footer.StartsWith(PreviousPrefix)
            || !footer[(PreviousPrefix.Length + HashHexLength)..].StartsWith(HashPrefix)
            || !footer.EndsWith(Terminator))
        {
            return torn;
        }

        var previous = Hex(footer.Slice(PreviousPrefix.Length, HashHexLength));
        var hash = Hex(footer.Slice(PreviousPrefix.Length + HashHexLength + HashPrefix.Length, HashHexLength));

        if (previous is null || hash is null)
        {
            return torn;
        }

        // The timestamp's width is fixed by its format, so `seq` sits at a known offset. Reading it
        // by position rather than by search is what keeps a JSON reader off the writer's path, where
        // it would be running over bytes somebody else may have authored.
        if (!line.StartsWith(TimestampPrefix))
        {
            return torn;
        }

        var head = line[TimestampPrefix.Length..];

        if (!head[TimestampLength..].StartsWith(SequencePrefix))
        {
            return torn;
        }

        var sequence = head[(TimestampLength + SequencePrefix.Length)..];
        var length = 0;
        while (length < sequence.Length && IsDigit(sequence[length]))
        {
            length++;
        }

        if (length == 0 || length >= sequence.Length || sequence[length] != (byte)',')
        {
            return torn;
        }

        // The value is advisory where the hash is authoritative, so a number too large to hold is
        // recorded as unknown rather than allowed to refuse a line that hashes correctly.
        return new AuditLine(
            AuditLineKind.Chained,
            hash,
            previous,
            TryNumber(sequence[..length], out var position) ? position : 0,
            Printable(head[..TimestampLength]));
    }

    /// <summary>A line about which nothing at all is known.</summary>
    /// <remarks>
    /// Not <c>default</c>: the string members of that are null, and every one of them ends up in a
    /// message somebody reads.
    /// </remarks>
    public static AuditLine Nothing { get; } = Plain(AuditLineKind.Foreign);

    private static AuditLine Plain(AuditLineKind kind) =>
        new(kind, string.Empty, string.Empty, 0, string.Empty);

    private static string? Hex(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
        {
            if (!(IsDigit(b) || b is >= (byte)'a' and <= (byte)'f'))
            {
                return null;
            }
        }

        return Encoding.ASCII.GetString(bytes);
    }

    /// <summary>
    /// The timestamp, but only if it is printable ASCII.
    /// </summary>
    /// <remarks>
    /// It is read out of a line that may have been edited, and it goes straight into a message a
    /// person reads in a terminal. A timestamp is not worth an escape sequence, so anything that is
    /// not plainly printable is dropped rather than repaired.
    /// </remarks>
    private static string Printable(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
        {
            if (b is < 0x20 or > 0x7e)
            {
                return string.Empty;
            }
        }

        return Encoding.ASCII.GetString(bytes);
    }

    private static bool IsDigit(byte b) => b is >= (byte)'0' and <= (byte)'9';

    private static bool TryNumber(ReadOnlySpan<byte> digits, out long value)
    {
        value = 0;

        foreach (var b in digits)
        {
            if (value > (long.MaxValue - (b - '0')) / 10)
            {
                value = 0;
                return false;
            }

            value = (value * 10) + (b - '0');
        }

        return true;
    }
}
