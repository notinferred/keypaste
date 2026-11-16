namespace Keypaste.Core.Audit;

/// <summary>What a whole audit log turned out to be.</summary>
public enum AuditChainVerdict
{
    /// <summary>There was nothing to verify.</summary>
    Empty = 0,

    /// <summary>Every chained record is the record that was written, in the order it was written.</summary>
    Intact = 1,

    /// <summary>At least one record has been altered, removed, inserted, or written by something else.</summary>
    Broken = 2,

    /// <summary>The file could not be read, so nothing is claimed either way.</summary>
    Unreadable = 3,
}

/// <summary>What is wrong with one line.</summary>
public enum AuditChainFault
{
    /// <summary>The line's own bytes are not the bytes its hash covers. It was edited.</summary>
    Altered = 0,

    /// <summary>The line's hash is its own, but it does not follow the chained line before it.</summary>
    Unlinked = 1,

    /// <summary>The line starts a new chain in the middle of a file. Truncate-and-continue looks like this.</summary>
    Restarted = 2,

    /// <summary>
    /// The line begins like a record and stops. A warning, not a break.
    /// </summary>
    /// <remarks>
    /// This is what a write interrupted by a crash leaves behind, so condemning it would mean
    /// condemning every machine that lost power mid-append — and a record edited into this shape is
    /// caught anyway, by the <see cref="Unlinked"/> record that follows it.
    /// </remarks>
    Torn = 3,

    /// <summary>The line is not a keypaste record. Something else wrote into the log.</summary>
    Foreign = 4,

    /// <summary>The line's position number does not follow the one before it. A warning, not a break.</summary>
    SequenceGap = 5,

    /// <summary>A record from a newer schema. Nothing here can check it, so nothing here vouches for it.</summary>
    Unverifiable = 8,

    /// <summary>A checkpoint or compaction record naming the vault whose key was given, which that key did not sign. The chain was rewritten without the vault.</summary>
    Forged = 9,

    /// <summary>A compaction record whose sealed lines are not the lines it sealed: one was changed, removed or inserted.</summary>
    Unsealed = 10,
}

/// <summary>What the check of a compaction record found.</summary>
public enum AuditCompactionCheck
{
    /// <summary>The log was never compacted.</summary>
    None = 0,

    /// <summary>The sealed lines match the record, whose signature was not checked because no key was given.</summary>
    NotChecked = 1,

    /// <summary>The sealed lines match the record, which names a vault other than the one whose key was given.</summary>
    OtherVault = 2,

    /// <summary>The sealed lines match the record, and the vault's key signed it.</summary>
    Verified = 3,

    /// <summary>The record does not answer for its lines, or the key did not sign it: a break, among the findings.</summary>
    Failed = 4,
}

/// <summary>One thing the verifier found, and where.</summary>
/// <param name="Line">The 1-based physical line number.</param>
/// <param name="Fault">What is wrong with it.</param>
/// <param name="Sequence">The <c>seq</c> the line declares, or <c>0</c> when it declares none.</param>
/// <param name="Timestamp">The <c>ts</c> the line declares, or empty when it declares none.</param>
public sealed record AuditChainFinding(int Line, AuditChainFault Fault, long Sequence, string Timestamp)
{
    /// <summary>Whether this finding is a break in the chain rather than an observation about it.</summary>
    /// <remarks>
    /// The ones that are not breaks are the ones an ordinary machine produces on its own: a write
    /// cut short, a position number that restarted, and records from a schema this version cannot
    /// check. None of them can hide an <em>edit</em>,
    /// because every chained record is pinned by the chained record after it. What they can do is
    /// sit in a table looking like records, which is why every one of them is still a finding and
    /// every one of them is marked in the rendering.
    /// </remarks>
    public bool IsBreak => Fault is not (AuditChainFault.SequenceGap
        or AuditChainFault.Torn
        or AuditChainFault.Unverifiable);
}

/// <summary>The result of checking a log against its own hash chain.</summary>
public sealed record AuditChainReport
{
    /// <summary>The overall answer.</summary>
    public required AuditChainVerdict Verdict { get; init; }

    /// <summary>The file that was checked.</summary>
    public required string Path { get; init; }

    /// <summary>How many chained records were verified.</summary>
    public int Records { get; init; }

    /// <summary>How many records were written by a schema this version does not know.</summary>
    public int Newer { get; init; }

    /// <summary>The position of the last chained record.</summary>
    public long LatestSequence { get; init; }

    /// <summary>The hash of the last chained record — the anchor <c>--expect</c> consumes.</summary>
    public string LatestHash { get; init; } = string.Empty;

    /// <summary>Whether the file ends mid-line, which is what an interrupted write looks like.</summary>
    public bool Unfinished { get; init; }

    /// <summary>
    /// Whether the file's bytes are not the bytes keypaste wrote — CRLF line endings, or a
    /// byte-order mark — even though the records themselves verify.
    /// </summary>
    public bool Rewritten { get; init; }

    /// <summary>Everything found, in line order.</summary>
    public IReadOnlyList<AuditChainFinding> Findings { get; init; } = [];

    /// <summary>
    /// Whether a hash the caller asked about is carried by a record this pass verified.
    /// </summary>
    /// <remarks>
    /// Null when no hash was asked about. It is answered from the chain rather than from the file's
    /// text on purpose: a hash that merely <em>appears</em> somewhere in the log proves nothing,
    /// because an entry name is written by the agent and would be a place to put one.
    /// </remarks>
    public bool? Anchored { get; init; }

    /// <summary>How many checkpoint lines the chain holds, signed or not.</summary>
    public int Checkpoints { get; init; }

    /// <summary>How many checkpoints the vault's key signed, or null when no key was given.</summary>
    public int? SignedCheckpoints { get; init; }

    /// <summary>How many chained records follow the last checkpoint the vault's key signed, or all of them when none was.</summary>
    public int UnsignedRecords { get; init; }

    /// <summary>What the check of the log's compaction record found.</summary>
    public AuditCompactionCheck Compaction { get; init; }

    /// <summary>How many chained lines the compaction record seals, itself excluded.</summary>
    public int SealedRecords { get; init; }

    /// <summary>How many physical lines the log held before it was last compacted.</summary>
    public long CompactedFrom { get; init; }

    /// <summary>Why the file could not be read, when that is the verdict.</summary>
    public string Error { get; init; } = string.Empty;

    /// <summary>The lines this pass could not vouch for, whether or not they broke the chain.</summary>
    /// <remarks>
    /// What a renderer needs in order to mark a row it should not present as a record. Every
    /// unchained line produces a finding precisely so that this set is complete.
    /// </remarks>
    public IReadOnlySet<int> Unverified =>
        Findings.Where(f => f.Fault != AuditChainFault.SequenceGap).Select(f => f.Line).ToHashSet();
}

/// <summary>
/// Checks an audit log against the hash chain its records carry.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not crying wolf is half the job.</b> A verifier that reddens after an ordinary crash, or when
/// it is run against a log a server is appending to at that moment, teaches its user to ignore it —
/// and an alarm nobody reads is worth less than no alarm, because it also costs the trust of the one
/// that matters. So a file that ends mid-line, a file whose records predate the chain, and a file
/// that has been through a tool that rewrote its line endings are all reported <em>and</em> called
/// intact, in those words.
/// </para>
/// <para>
/// <b>The linking rule.</b> <c>prev</c> links to the nearest preceding <em>chained</em> line.
/// Anything that is not part of the chain — a v1 record, a crash fragment, a line something else
/// wrote — is stepped over. That costs no strength against an <em>edit</em>, because every chained
/// line is pinned by the line after it rather than by itself: change a record's bytes and its own
/// hash stops matching, change its hash too and the next line's link stops matching.
/// </para>
/// <para>
/// <b>What stepping over does cost, and what pays for it.</b> A line the chain skips can be
/// <em>inserted</em> without breaking anything, and it renders as a record like any other. So every
/// skipped line is a finding, not merely a counter — <see cref="AuditChainReport.Unverified"/> is
/// what lets a renderer mark the row — and the one skipped shape that cannot be innocent is a break
/// outright: keypaste never writes a v1 record after a v2 one, so one sitting there is an insertion
/// rather than a log that grew across an upgrade.
/// </para>
/// <para>
/// <b>What it cannot do.</b> The chain holds no secret, so anyone who can write the file can
/// recompute it, unless the vault's checkpoint key is given to check the checkpoints its owner
/// signed, and then only up to the last of them; and records deleted from the end leave a chain
/// that is internally perfect. See
/// THREATS.md T-5, which states both as residuals rather than leaving them to be discovered — and
/// <see cref="AuditText.Limits"/>, which prints them on a <em>passing</em> check rather than only on
/// a failing one.
/// </para>
/// </remarks>
public static class AuditChainVerifier
{
    private static ReadOnlySpan<byte> ByteOrderMark => [0xEF, 0xBB, 0xBF];

    /// <summary>Whether some text is shaped like a hash this verifier produces.</summary>
    /// <param name="text">The candidate, typically one a user typed back from an earlier run.</param>
    /// <returns><see langword="true"/> for exactly 64 lowercase hex characters.</returns>
    public static bool IsHash(string? text) => AuditChain.IsChainValue(text);

    /// <summary>
    /// The longest line this reads whole. Anything past it is not a record and is not buffered.
    /// </summary>
    /// <remarks>
    /// Four times the writer's own cap. A file is attacker-writable (THREATS.md assumption 1), and
    /// a single line of a few hundred megabytes would otherwise be read into memory in order to
    /// discover that it is not a record.
    /// </remarks>
    internal const int MaximumLineBytes = 4 * AuditLog.MaximumRecordBytes;

    /// <summary>Verifies the log at a path.</summary>
    /// <param name="path">The log.</param>
    /// <param name="anchor">
    /// A hash from an earlier run to look for, or null. Answered from the chain, never from the
    /// file's text: <see cref="AuditChainReport.Anchored"/> says why that distinction matters.
    /// </param>
    /// <param name="checkpoints">The vault's checkpoint key, to check the checkpoints naming that vault, or null.</param>
    /// <returns>What was found. Never throws for an ordinary I/O failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <remarks>
    /// Opened <see cref="FileShare.ReadWrite"/>, because a bridge may be holding the log open
    /// for writing and on Windows any narrower share mode fails outright against it.
    /// </remarks>
    public static AuditChainReport Verify(string path, string? anchor = null, AuditCheckpointKey? checkpoints = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Verify(stream, path, anchor, checkpoints);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or NotSupportedException or ArgumentException)
        {
            return new AuditChainReport
            {
                Verdict = AuditChainVerdict.Unreadable,
                Path = path,
                Error = ex.Message,
            };
        }
    }

    /// <summary>Verifies a log that is already open.</summary>
    /// <param name="stream">The bytes of the log, positioned at its start.</param>
    /// <param name="path">The name to report, for messages.</param>
    /// <param name="anchor">A hash from an earlier run to look for, or null.</param>
    /// <param name="checkpoints">The vault's checkpoint key, or null.</param>
    /// <returns>What was found.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> or <paramref name="path"/> is null.</exception>
    public static AuditChainReport Verify(Stream stream, string path, string? anchor = null, AuditCheckpointKey? checkpoints = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(path);

        var walk = new Walk(path, anchor, checkpoints);
        var buffer = new byte[8192];

        using var pending = new MemoryStream(1024);
        var overlong = false;

        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            var offset = 0;

            while (offset < read)
            {
                var at = Array.IndexOf(buffer, (byte)'\n', offset, read - offset);
                var take = (at < 0 ? read : at) - offset;

                if (pending.Length + take > MaximumLineBytes)
                {
                    // Kept only up to the cap: enough to classify the line, and no more. A line
                    // this long is not a record, and reading all of it is the only thing a file
                    // full of one could make keypaste do.
                    overlong = true;
                    take = (int)Math.Max(0, MaximumLineBytes - pending.Length);
                }

                pending.Write(buffer, offset, take);

                if (at < 0)
                {
                    break;
                }

                walk.Line(pending.GetBuffer().AsSpan(0, (int)pending.Length), overlong);
                pending.SetLength(0);
                overlong = false;
                offset = at + 1;
            }
        }

        // Anything left is a line with no terminator, which by construction can only be the last
        // one. It is still checked: a record that verifies is a record whether or not the file ends
        // with a newline, and treating the last line as unexaminable would make deleting one byte
        // the way to edit it freely.
        if (pending.Length > 0)
        {
            walk.Last(pending.GetBuffer().AsSpan(0, (int)pending.Length), overlong);
        }

        return walk.Report();
    }

    /// <summary>The state of one pass over a file.</summary>
    /// <remarks>
    /// A class rather than a set of locals because <see cref="Line"/> takes a span, and a span
    /// cannot cross into an iterator or a closure.
    /// </remarks>
    private sealed class Walk(string path, string? anchor, AuditCheckpointKey? key)
    {
        private readonly List<AuditChainFinding> _findings = [];
        private int _number;
        private int _records;
        private int _newer;
        private int _checkpoints;
        private int _signed;
        private int _unsigned;
        private bool _chained;
        private bool _unfinished;
        private bool _rewritten;
        private bool _anchored;
        private long _sequence;
        private string _hash = string.Empty;
        private Sealed? _seal;
        private AuditCompactionCheck _compaction;
        private int _sealedRecords;
        private long _compactedFrom;

        /// <summary>A compaction record whose lines are still being walked, and the findings its seal may answer for.</summary>
        private sealed class Sealed(AuditSeal seal, int line, AuditLine record)
        {
            internal AuditSeal Seal { get; } = seal;

            internal int Line { get; } = line;

            internal AuditLine Record { get; } = record;

            internal AuditCompaction.Digest Digest { get; } = new();

            internal List<AuditChainFinding> Deferred { get; } = [];

            internal int Records { get; set; }
        }

        /// <summary>The last line of a file that does not end with a newline.</summary>
        /// <remarks>
        /// Classified like any other. Only a line that is <em>not</em> a whole record is the
        /// interrupted write this forgives — and that is what a crash actually leaves, because a
        /// record and its newline are one write.
        /// </remarks>
        public void Last(ReadOnlySpan<byte> raw, bool overlong)
        {
            if (AuditChain.Inspect(Trim(raw, first: _number == 0)).Kind == AuditLineKind.Chained)
            {
                Line(raw, overlong);
                return;
            }

            _number++;
            _unfinished = true;
        }

        public void Line(ReadOnlySpan<byte> raw, bool overlong)
        {
            _number++;

            // Too long to have been written by keypaste, and only its opening bytes were kept, so
            // there is nothing here to classify honestly.
            if (overlong)
            {
                Found(AuditChainFault.Foreign, AuditChain.Nothing);
                return;
            }

            var line = Trim(raw, first: _number == 1);
            var inspected = AuditChain.Inspect(line);

            switch (inspected.Kind)
            {
                case AuditLineKind.Newer:
                    _newer++;
                    Found(AuditChainFault.Unverifiable, inspected);
                    return;

                case AuditLineKind.Chained:
                    Chained(line, inspected);
                    return;

                case AuditLineKind.Torn:
                    Found(AuditChainFault.Torn, inspected);
                    return;

                default:
                    Found(AuditChainFault.Foreign, inspected);
                    return;
            }
        }

        public AuditChainReport Report()
        {
            if (_seal is { } open)
            {
                Unseal(open);
            }

            var broken = _findings.Exists(f => f.IsBreak);

            var verdict = broken
                ? AuditChainVerdict.Broken
                : _records == 0 && _newer == 0
                    ? AuditChainVerdict.Empty
                    : AuditChainVerdict.Intact;

            return new AuditChainReport
            {
                Verdict = verdict,
                Path = path,
                Records = _records,
                Newer = _newer,
                LatestSequence = _sequence,
                LatestHash = _hash,
                Unfinished = _unfinished,
                Rewritten = _rewritten,
                Anchored = anchor is null ? null : _anchored,
                Checkpoints = _checkpoints,
                SignedCheckpoints = key is null ? null : _signed,
                UnsignedRecords = _unsigned,
                Findings = [.. _findings.OrderBy(f => f.Line)],
                Compaction = _compaction,
                SealedRecords = _sealedRecords,
                CompactedFrom = _compactedFrom,
            };
        }

        /// <summary>Removes the marks a tool that copied the file left, and notes that it did.</summary>
        /// <remarks>
        /// Exactly one carriage return, and only at the end: a log copied through a Windows tool has
        /// grown a <c>\r</c> on every line, which changes every byte-for-byte comparison in this
        /// file while changing no record. Forgiving it is what keeps a copied log from reading as an
        /// attacked one; forgiving more than one would be forgiving an edit.
        /// </remarks>
        private ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> raw, bool first)
        {
            var line = raw;

            if (first && line.StartsWith(ByteOrderMark))
            {
                _rewritten = true;
                line = line[ByteOrderMark.Length..];
            }

            if (!line.IsEmpty && line[^1] == (byte)'\r')
            {
                _rewritten = true;
                line = line[..^1];
            }

            return line;
        }

        private void Chained(ReadOnlySpan<byte> line, AuditLine inspected)
        {
            var intact = string.Equals(AuditChain.Recompute(line), inspected.Hash, StringComparison.Ordinal);

            // Only a record whose own bytes still hash to what it claims can answer for an anchor.
            // A hash that merely appears in the file proves nothing: an entry name is text the agent
            // wrote, and would be the obvious place to put one.
            if (intact && string.Equals(inspected.Hash, anchor, StringComparison.Ordinal))
            {
                _anchored = true;
            }

            if (!intact)
            {
                Found(AuditChainFault.Altered, inspected);
            }
            else if (_chained)
            {
                // A genesis link after a chained line is not a broken link but a second beginning,
                // and it is what a file that was cut short and then appended to looks like. Naming
                // it separately is the difference between "something is wrong here" and "the end of
                // this log was removed".
                if (string.Equals(inspected.Previous, AuditChain.Genesis, StringComparison.Ordinal))
                {
                    Found(AuditChainFault.Restarted, inspected);
                }
                else if (!string.Equals(inspected.Previous, _hash, StringComparison.Ordinal))
                {
                    Found(AuditChainFault.Unlinked, inspected);
                }
            }
            else if (!string.Equals(inspected.Previous, AuditChain.Genesis, StringComparison.Ordinal))
            {
                // The first chained line in a file keypaste wrote always starts the chain. One that
                // does not means the lines before it are gone.
                Found(AuditChainFault.Unlinked, inspected);
            }

            if (Signs(line, inspected, intact))
            {
                _unsigned = 0;
            }
            else
            {
                _unsigned++;
            }

            if (_chained && inspected.Sequence != _sequence + 1)
            {
                Found(AuditChainFault.SequenceGap, inspected);
            }

            var first = !_chained;

            _records++;
            _chained = true;
            _sequence = inspected.Sequence;
            _hash = inspected.Hash;

            if (_seal is { } open)
            {
                Walked(open, inspected);
            }
            else if (first && intact && AuditCompaction.TryRead(line, out var seal)
                && string.Equals(inspected.Previous, AuditChain.Genesis, StringComparison.Ordinal))
            {
                _seal = new Sealed(seal, _number, inspected);
                _compactedFrom = seal.PreviousLines;
            }
        }

        /// <summary>Adds a sealed line to its record's digest, and closes the seal at the line it ends with.</summary>
        private void Walked(Sealed open, AuditLine inspected)
        {
            open.Digest.Add(inspected.Hash);
            open.Records++;

            if (!string.Equals(inspected.Hash, open.Seal.Head, StringComparison.Ordinal))
            {
                return;
            }

            _seal = null;
            var digest = open.Digest.Finish();
            open.Digest.Dispose();

            if (!string.Equals(digest, open.Seal.Digest, StringComparison.Ordinal))
            {
                Unseal(open);
                return;
            }

            _sealedRecords = open.Records;

            if (key is null)
            {
                _compaction = AuditCompactionCheck.NotChecked;
                return;
            }

            if (!string.Equals(open.Seal.Vault, key.Vault, StringComparison.Ordinal))
            {
                _compaction = AuditCompactionCheck.OtherVault;
                return;
            }

            if (!AuditCompaction.Verifies(key, open.Seal))
            {
                _compaction = AuditCompactionCheck.Failed;
                _findings.AddRange(open.Deferred);
                _findings.Add(new AuditChainFinding(open.Line, AuditChainFault.Forged, open.Record.Sequence, open.Record.Timestamp));
                return;
            }

            // The key vouches for every sealed line, so none of them is unsigned.
            _compaction = AuditCompactionCheck.Verified;
            _unsigned = 0;
        }

        /// <summary>A seal that does not answer for its lines: what it deferred is a break after all.</summary>
        private void Unseal(Sealed open)
        {
            _seal = null;
            _compaction = AuditCompactionCheck.Failed;
            _findings.AddRange(open.Deferred);
            _findings.Add(new AuditChainFinding(open.Line, AuditChainFault.Unsealed, open.Record.Sequence, open.Record.Timestamp));
        }

        private bool Signs(ReadOnlySpan<byte> line, AuditLine inspected, bool intact)
        {
            if (!AuditCheckpoint.TryRead(line, out var vault, out var mac))
            {
                return false;
            }

            _checkpoints++;

            if (!intact || key is null || !string.Equals(vault, key.Vault, StringComparison.Ordinal))
            {
                return false;
            }

            if (!AuditCheckpoint.Verifies(key, inspected.Sequence, inspected.Previous, mac))
            {
                Found(AuditChainFault.Forged, inspected);
                return false;
            }

            _signed++;
            return true;
        }

        /// <remarks>
        /// Inside a compaction record's sealed lines a broken link or a position gap is where folded or removed lines
        /// were, so it waits for the seal: kept when the seal does not answer for its lines, dropped when it does.
        /// </remarks>
        private void Found(AuditChainFault fault, AuditLine inspected)
        {
            var finding = new AuditChainFinding(_number, fault, inspected.Sequence, inspected.Timestamp);

            if (_seal is { } open && fault is AuditChainFault.Unlinked or AuditChainFault.SequenceGap)
            {
                open.Deferred.Add(finding);
                return;
            }

            _findings.Add(finding);
        }
    }
}
