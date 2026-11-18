using System.Globalization;
using System.Text.Json;
using Keypaste.Core.Ownership;
using Keypaste.Infrastructure;

namespace Keypaste.Core.Audit;

/// <summary>How long the vault's owner keeps the audit log, as the person set it.</summary>
public static class AuditRetention
{
    /// <summary>How long a lookup keeps its own line before it is folded into its agent's summary of the day.</summary>
    public const int FoldAfterDays = 30;

    /// <summary>The shortest period the log may be kept for.</summary>
    public const int MinimumDays = FoldAfterDays;

    /// <summary>The period a person who never chose one keeps it for: one year.</summary>
    public const int DefaultDays = 365;

    /// <summary>Written for "forever": no day is ever removed.</summary>
    public const int Forever = 0;

    /// <summary>A period as the person may set it: <see cref="Forever"/>, or at least <see cref="MinimumDays"/>.</summary>
    /// <param name="days">The period asked for.</param>
    /// <returns>The period kept.</returns>
    public static int Clamp(int days) => days == Forever ? Forever : Math.Max(days, MinimumDays);
}

/// <summary>What one compaction did.</summary>
public enum AuditCompactionOutcome
{
    /// <summary>The log was rewritten, sealed under the vault's key.</summary>
    Compacted = 0,

    /// <summary>Nothing in the log was old enough to fold or remove, so the file was not touched.</summary>
    Unchanged = 1,

    /// <summary>The log was left as it is, for <see cref="AuditCompactionResult.Reason"/>.</summary>
    Refused = 2,
}

/// <summary>What one compaction did, and how much.</summary>
/// <param name="Outcome">Whether the log was rewritten.</param>
/// <param name="Folded">How many lookup lines were folded into summaries.</param>
/// <param name="Removed">How many lines were removed with their days.</param>
/// <param name="Reason">Why the log was left as it is, or empty.</param>
public sealed record AuditCompactionResult(AuditCompactionOutcome Outcome, int Folded, int Removed, string Reason)
{
    /// <summary>How many bytes of the log were read under the writers' lock: the last line read and what was appended since.</summary>
    internal int LockedBytesRead { get; init; }

    internal static AuditCompactionResult Refused(string reason) => new(AuditCompactionOutcome.Refused, 0, 0, reason);
}

/// <summary>
/// Folds an agent's old lookups into one summary line per agent per UTC day, removes whole days older than the period
/// the log is kept for, and seals what it kept under the vault's checkpoint key (PRODUCT §3.3).
/// </summary>
/// <remarks>
/// <para>
/// <b>Kept lines keep their bytes.</b> A value release, a refusal, a run, a save, a change, a checkpoint and every line
/// that is not a lookup is copied byte for byte, so its own hash still recomputes to the hash it was written with. What
/// removing lines breaks is the links between them, and the seal answers for exactly that: the new file's first line,
/// the compaction record, carries the previous file's line count and last hash, a digest of every chained line it kept
/// or wrote, and an HMAC over those under the vault's key. <see cref="AuditChainVerifier"/> walks the sealed lines to
/// that last hash, accepts their broken links only when the digest matches, and checks the HMAC when given the key.
/// </para>
/// <para>
/// <b>Only a log that verifies is compacted.</b> Resealing a broken chain would launder the break, so a log that does
/// not verify under the key, ends mid-line, holds lines of a newer schema or was sealed under the key of a vault this
/// process does not hold is left as it is. Several vaults' owners in one process write to one log, so each compacts
/// under its own key with the others' given, which check what those owners signed and sealed before it seals over them.
/// The day of the last line is never compacted, which keeps the line the next append links to.
/// </para>
/// <para>
/// <b>One rename, under the writers' lock.</b> The log is read under the sidecar lock every appender takes, then
/// compacted and staged beside it without the lock. Under the lock again, the work is bounded by what was appended
/// rather than by the log: the last line read is checked to still be where it was read, the lines appended since are
/// added to the staged file, and it is renamed over the log. A crash before the rename leaves the old file as it was.
/// </para>
/// </remarks>
public static class AuditCompaction
{
    internal const string Method = "compaction";

    private static ReadOnlySpan<byte> VaultPrefix => ",\"vault\":\""u8;

    private static ReadOnlySpan<byte> FieldPrefix => "\",\"compaction\":\""u8;

    /// <summary>The fixed width of <c>.head.digest.mac"</c> at the end of the field.</summary>
    private static int SealWidth => (3 * (AuditChain.HashHexLength + 1)) + 1;

    /// <summary>Compacts the log at <paramref name="path"/>, if anything in it is old enough.</summary>
    /// <param name="path">The log.</param>
    /// <param name="key">The vault's checkpoint key, which signs the seal.</param>
    /// <param name="keepDays">How long the log is kept, as <see cref="AuditRetention.Clamp"/> allows.</param>
    /// <param name="now">The time the periods are measured from.</param>
    /// <param name="session">The owner's session writing the seal.</param>
    /// <param name="others">The checkpoint keys of the other vaults whose owners write to this log in this process, or none.</param>
    /// <returns>What was done.</returns>
    public static AuditCompactionResult Compact(string path, AuditCheckpointKey key, int keepDays, DateTimeOffset now, string session, IReadOnlyCollection<AuditCheckpointKey>? others = null) =>
        Compact(path, key, keepDays, now, session, beforeCommit: null, others);

    /// <summary>Compacts the log, running <paramref name="beforeCommit"/> before the old file is replaced.</summary>
    /// <param name="path">The log.</param>
    /// <param name="key">The vault's checkpoint key.</param>
    /// <param name="keepDays">How long the log is kept.</param>
    /// <param name="now">The time the periods are measured from.</param>
    /// <param name="session">The owner's session writing the seal.</param>
    /// <param name="beforeCommit">Run between composing the new file and taking the lock to replace the old one, for tests.</param>
    /// <param name="others">The checkpoint keys of the other vaults whose owners write to this log in this process, or none.</param>
    internal static AuditCompactionResult Compact(string path, AuditCheckpointKey key, int keepDays, DateTimeOffset now, string session, Action? beforeCommit, IReadOnlyCollection<AuditCheckpointKey>? others = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(session);

        IReadOnlyCollection<AuditCheckpointKey> keys = [key, .. (others ?? []).Where(other => !string.Equals(other.Vault, key.Vault, StringComparison.Ordinal))];

        var full = Path.GetFullPath(path);

        if (FileLocking.IsDisabled)
        {
            return AuditCompactionResult.Refused($"the audit log cannot be locked: {FileLocking.Refusal}");
        }

        try
        {
            if (!TryReadLocked(full, out var snapshot, out var refusal))
            {
                return AuditCompactionResult.Refused(refusal);
            }

            var plan = Plan(full, snapshot, key, keys, AuditRetention.Clamp(keepDays), now, session);

            if (plan.Contents is not { } compacted)
            {
                return plan.Result;
            }

            // Staged whole before the lock is taken, so the writers' wait does not grow with the log.
            using var staged = AtomicFile.Stage(full, compacted);
            beforeCommit?.Invoke();

            using var held = AuditLog.AcquireWriteLock(full);
            if (held is null)
            {
                return AuditCompactionResult.Refused("the audit log is locked by another keypaste process");
            }

            byte[] appended;
            int lockedRead;

            using (var current = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                // keypaste's writers only append, so a file that still holds the last line read, where it was read, holds what was read.
                var lastLine = snapshot.AsSpan(0, snapshot.Length - 1).LastIndexOf((byte)'\n') + 1;
                var length = current.Length;

                if (length < snapshot.Length)
                {
                    return AuditCompactionResult.Refused("the audit log grew shorter while it was compacted");
                }

                var tail = new byte[length - lastLine];
                current.Seek(lastLine, SeekOrigin.Begin);
                current.ReadExactly(tail);
                lockedRead = tail.Length;

                if (!tail.AsSpan(0, snapshot.Length - lastLine).SequenceEqual(snapshot.AsSpan(lastLine)))
                {
                    return AuditCompactionResult.Refused("the audit log changed while it was compacted");
                }

                appended = tail[(snapshot.Length - lastLine)..];
            }

            staged.Append(appended);
            staged.Commit();
            return plan.Result with { LockedBytesRead = lockedRead };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return AuditCompactionResult.Refused($"the audit log could not be compacted: {ex.Message}");
        }
    }

    /// <summary>Reads a compaction record's seal from its bytes, by position, as the verifier reads a checkpoint.</summary>
    /// <param name="line">A chained line.</param>
    /// <param name="seal">What it seals, when it is a compaction record.</param>
    /// <returns>Whether the line carries a seal.</returns>
    internal static bool TryRead(ReadOnlySpan<byte> line, out AuditSeal seal)
    {
        seal = default;

        if (line.Length < AuditChain.ChainFooterBytes + SealWidth)
        {
            return false;
        }

        var field = line[..^AuditChain.ChainFooterBytes];

        if (field[^1] != (byte)'"')
        {
            return false;
        }

        var hashes = field[^SealWidth..^1];
        var head = Hex(hashes.Slice(1, AuditChain.HashHexLength));
        var digest = Hex(hashes.Slice(AuditChain.HashHexLength + 2, AuditChain.HashHexLength));
        var mac = Hex(hashes.Slice((2 * AuditChain.HashHexLength) + 3, AuditChain.HashHexLength));

        if (hashes[0] != (byte)'.' || hashes[AuditChain.HashHexLength + 1] != (byte)'.' || hashes[(2 * AuditChain.HashHexLength) + 2] != (byte)'.'
            || head is null || digest is null || mac is null)
        {
            return false;
        }

        var before = field[..^SealWidth];
        var digits = 0;
        while (digits < before.Length && char.IsAsciiDigit((char)before[^(digits + 1)]))
        {
            digits++;
        }

        if (digits is 0 or > 18 || !before[..^digits].EndsWith(FieldPrefix))
        {
            return false;
        }

        var lines = long.Parse(Encoding.ASCII.GetString(before[^digits..]), CultureInfo.InvariantCulture);
        var named = before[..^(digits + FieldPrefix.Length)];

        if (named.Length < VaultPrefix.Length + VaultIdentity.KeyLength || !named[..^VaultIdentity.KeyLength].EndsWith(VaultPrefix))
        {
            return false;
        }

        var vault = Encoding.ASCII.GetString(named[^VaultIdentity.KeyLength..]);

        if (!vault.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f'))
        {
            return false;
        }

        seal = new AuditSeal(vault, lines, head, digest, mac);
        return true;
    }

    /// <summary>Whether the vault's key signed a seal.</summary>
    internal static bool Verifies(AuditCheckpointKey key, AuditSeal seal) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Mac(key, seal.PreviousLines, seal.Head, seal.Digest)),
            Encoding.ASCII.GetBytes(seal.Mac));

    /// <summary>A running digest over chained lines' hashes, the commitment a seal, a summary and a removal line carry.</summary>
    internal sealed class Digest : IDisposable
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        internal void Add(string lineHash) => _hash.AppendData(Encoding.ASCII.GetBytes(lineHash + "\n"));

        internal string Finish() => Convert.ToHexStringLower(_hash.GetHashAndReset());

        public void Dispose() => _hash.Dispose();
    }

    private static string Mac(AuditCheckpointKey key, long previousLines, string head, string digest) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(
            key.Key,
            Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"keypaste audit compaction 1\n{key.Vault}\n{previousLines}\n{head}\n{digest}"))));

    private static bool TryReadLocked(string path, out byte[] snapshot, out string refusal)
    {
        snapshot = [];
        refusal = string.Empty;

        using var held = AuditLog.AcquireWriteLock(path);
        if (held is null)
        {
            refusal = "the audit log is locked by another keypaste process";
            return false;
        }

        if (!File.Exists(path))
        {
            refusal = "there is no audit log";
            return false;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        snapshot = new byte[stream.Length];
        stream.ReadExactly(snapshot);
        return true;
    }

    private readonly record struct Planned(AuditCompactionResult Result, byte[]? Contents);

    /// <summary>One physical line of the snapshot, as compaction sees it.</summary>
    private sealed record Line(byte[] Bytes, AuditLine Inspected)
    {
        internal bool Chained => Inspected.Kind == AuditLineKind.Chained;

        internal DateOnly? Day { get; init; }

        internal string Tool { get; init; } = string.Empty;

        internal AuditDecision Decision { get; init; }

        internal string? Name { get; init; }

        internal string? Label { get; init; }

        internal string? Vault { get; init; }

        /// <summary>How many requests the line records: a summary's folded lookups, a removal's count, none for a seal.</summary>
        internal int Requests { get; init; } = 1;

        /// <summary>On a removal line, its first removed day.</summary>
        internal DateOnly? RemovedFrom { get; init; }
    }

    private static Planned Plan(string path, byte[] snapshot, AuditCheckpointKey key, IReadOnlyCollection<AuditCheckpointKey> keys, int keepDays, DateTimeOffset now, string session)
    {
        if (snapshot.Length == 0)
        {
            return new Planned(new AuditCompactionResult(AuditCompactionOutcome.Unchanged, 0, 0, string.Empty), null);
        }

        if (snapshot[^1] != (byte)'\n')
        {
            return new Planned(AuditCompactionResult.Refused("the audit log ends mid-line"), null);
        }

        using (var stream = new MemoryStream(snapshot, writable: false))
        {
            var report = AuditChainVerifier.Verify(stream, path, null, keys);

            if (report.Verdict != AuditChainVerdict.Intact || report.Newer > 0 || report.Rewritten)
            {
                return new Planned(AuditCompactionResult.Refused("the audit log does not verify; 'keypaste log verify' says why"), null);
            }

            if (report.Compaction is not (AuditCompactionCheck.None or AuditCompactionCheck.Verified))
            {
                return new Planned(AuditCompactionResult.Refused("the audit log was sealed under the key of a vault this process does not hold"), null);
            }
        }

        var lines = Split(snapshot);
        var head = lines.FindLastIndex(line => line.Chained);
        if (head < 0 || lines[head].Day is not { } headDay)
        {
            return new Planned(new AuditCompactionResult(AuditCompactionOutcome.Unchanged, 0, 0, string.Empty), null);
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var foldBefore = Min(today.AddDays(-AuditRetention.FoldAfterDays), headDay);
        var removeBefore = keepDays == AuditRetention.Forever ? DateOnly.MinValue : Min(today.AddDays(-keepDays), headDay);

        bool Sealing(Line line) => line.Chained && string.Equals(line.Tool, AuditTools.Compaction, StringComparison.Ordinal);
        bool Removed(Line line) => line.Chained && !Sealing(line) && line.Day is { } day && day < removeBefore;
        bool Folded(Line line) => line.Chained && !Removed(line) && line.Day is { } day && day < foldBefore
            && line.Tool is AuditTools.List or AuditTools.Find;

        var removed = lines.Where(Removed).ToList();
        var folded = lines.Where(Folded).ToList();

        if (removed.Count == 0 && folded.Count == 0)
        {
            return new Planned(new AuditCompactionResult(AuditCompactionOutcome.Unchanged, 0, 0, string.Empty), null);
        }

        var groups = folded.GroupBy(line => (line.Label, line.Name, line.Vault, line.Day)).ToDictionary(group => group.Last(), group => group.ToList());

        using var body = new MemoryStream(snapshot.Length);
        using var sealedLines = new Digest();
        var previous = string.Empty;

        void Write(byte[] bytes, string hash)
        {
            body.Write(bytes);
            sealedLines.Add(hash);
            previous = hash;
        }

        void Compose(AuditRecord record, string timestamp, IReadOnlyList<Line> replaced)
        {
            var last = replaced[^1].Inspected;
            var bytes = AuditLog.Compose(record, timestamp, last.Sequence, previous.Length > 0 ? previous : last.Hash, withReasonExcerpt: true, out var hash);
            Write(bytes, hash);
        }

        if (removed.Count > 0)
        {
            Compose(Removal(removed), removed[^1].Inspected.Timestamp, removed);
        }

        foreach (var line in lines)
        {
            if (Sealing(line) || Removed(line))
            {
                continue;
            }

            if (Folded(line))
            {
                if (groups.TryGetValue(line, out var group))
                {
                    Compose(Summary(group), line.Inspected.Timestamp, group);
                }

                continue;
            }

            if (line.Chained)
            {
                Write([.. line.Bytes, (byte)'\n'], line.Inspected.Hash);
            }
            else
            {
                body.Write(line.Bytes);
                body.WriteByte((byte)'\n');
            }
        }

        var seal = new AuditSeal(key.Vault, lines.Count, lines[head].Inspected.Hash, sealedLines.Finish(), string.Empty);
        var record = new AuditRecord
        {
            Tool = AuditTools.Compaction,
            Client = AuditClient.Unknown,
            Decision = AuditDecision.Denied,
            Method = AuditMethod.Compaction,
            Reason = "the vault's owner folded or removed older lines and sealed the rest",
            Session = session,
            Vault = key.Vault,
            Compaction = string.Create(
                CultureInfo.InvariantCulture,
                $"{seal.PreviousLines}.{seal.Head}.{seal.Digest}.{Mac(key, seal.PreviousLines, seal.Head, seal.Digest)}"),
        };

        var first = AuditLog.Compose(record, now.UtcDateTime.ToString(AuditLog.TimestampFormat, CultureInfo.InvariantCulture), 0, AuditChain.Genesis, withReasonExcerpt: true, out _);
        var contents = new byte[first.Length + body.Length];
        first.CopyTo(contents, 0);
        body.GetBuffer().AsSpan(0, (int)body.Length).CopyTo(contents.AsSpan(first.Length));

        // The new file is checked as the verifier will check it before it replaces anything.
        using (var check = new MemoryStream(contents, writable: false))
        {
            var report = AuditChainVerifier.Verify(check, path, null, keys);

            if (report.Verdict != AuditChainVerdict.Intact || report.Compaction != AuditCompactionCheck.Verified)
            {
                return new Planned(AuditCompactionResult.Refused("the compacted log did not verify"), null);
            }
        }

        return new Planned(new AuditCompactionResult(AuditCompactionOutcome.Compacted, folded.Count, removed.Count, string.Empty), contents);
    }

    private static AuditRecord Removal(IReadOnlyList<Line> removed)
    {
        using var digest = new Digest();
        foreach (var line in removed)
        {
            digest.Add(line.Inspected.Hash);
        }

        var from = removed.Select(line => line.RemovedFrom ?? line.Day!.Value).Min();
        var to = removed.Select(line => line.Day!.Value).Max();
        var count = removed.Sum(line => line.Requests);

        return new AuditRecord
        {
            Tool = AuditTools.Removed,
            Client = AuditClient.Unknown,
            Decision = AuditDecision.Denied,
            Method = AuditMethod.Expired,
            Reason = "the vault's owner removed the days older than the period the log is kept for",
            Removal = new AuditRemoval(Day(from), Day(to), count, digest.Finish()),
        };
    }

    private static AuditRecord Summary(IReadOnlyList<Line> group)
    {
        using var digest = new Digest();
        foreach (var line in group)
        {
            digest.Add(line.Inspected.Hash);
        }

        var counts = group
            .GroupBy(line => (line.Tool, line.Decision))
            .OrderBy(kind => kind.Key.Tool, StringComparer.Ordinal)
            .ThenBy(kind => kind.Key.Decision)
            .Select(kind => new AuditFoldCount(kind.Key.Tool, kind.Key.Decision, kind.Count()))
            .ToList();

        var first = group[0];

        return new AuditRecord
        {
            Tool = AuditTools.Summary,
            Client = new AuditClient(first.Name, null, first.Label),
            Decision = group.Any(line => line.Decision == AuditDecision.Granted) ? AuditDecision.Granted : AuditDecision.Denied,
            Method = AuditMethod.Folded,
            Reason = string.Create(CultureInfo.InvariantCulture, $"the vault's owner folded {group.Count} lookups of one day into this line"),
            Vault = first.Vault,
            Summary = new AuditSummary(Day(first.Day!.Value), first.Inspected.Timestamp, group[^1].Inspected.Timestamp, counts, digest.Finish()),
        };
    }

    private static List<Line> Split(byte[] snapshot)
    {
        var lines = new List<Line>();
        var rest = snapshot.AsSpan(0, snapshot.Length - 1);

        while (true)
        {
            var at = rest.IndexOf((byte)'\n');
            var raw = at < 0 ? rest : rest[..at];
            lines.Add(Classify(raw.ToArray()));

            if (at < 0)
            {
                return lines;
            }

            rest = rest[(at + 1)..];
        }
    }

    /// <summary>Reads what compaction decides by from a line the verifier has already vouched for.</summary>
    /// <remarks>A line whose fields do not parse is kept as it is: the only cost of misreading one is keeping it.</remarks>
    private static Line Classify(byte[] bytes)
    {
        var line = new Line(bytes, AuditChain.Inspect(bytes));

        if (!line.Chained)
        {
            return line;
        }

        try
        {
            using var parsed = JsonDocument.Parse(bytes);
            var root = parsed.RootElement;
            var client = root.TryGetProperty("client", out var who) && who.ValueKind == JsonValueKind.Object ? who : default;
            var tool = Text(root, "tool") ?? string.Empty;

            return line with
            {
                Day = DateTime.TryParseExact(line.Inspected.Timestamp, AuditLog.TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)
                    ? DateOnly.FromDateTime(at)
                    : null,
                Tool = tool,
                Decision = string.Equals(Text(root, "decision"), "granted", StringComparison.Ordinal) ? AuditDecision.Granted : AuditDecision.Denied,
                Name = Text(client, "name"),
                Label = Text(client, "label"),
                Vault = Text(root, "vault"),
                Requests = tool switch
                {
                    AuditTools.Summary => Counted(root, "summary"),
                    AuditTools.Removed => Counted(root, "removed"),
                    AuditTools.Compaction => 0,
                    _ when string.Equals(Text(root, "method"), AuditCheckpoint.Method, StringComparison.Ordinal) => 0,
                    _ => 1,
                },
                RemovedFrom = root.TryGetProperty("removed", out var removed) && removed.ValueKind == JsonValueKind.Object
                    && DateOnly.TryParseExact(Text(removed, "from"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
                        ? from
                        : null,
            };
        }
        catch (JsonException)
        {
            return line;
        }
    }

    private static int Counted(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var section) || section.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        if (section.TryGetProperty("count", out var count) && count.TryGetInt32(out var total))
        {
            return total;
        }

        return section.TryGetProperty("counts", out var counts) && counts.ValueKind == JsonValueKind.Array
            ? counts.EnumerateArray().Sum(kind => kind.TryGetProperty("count", out var each) && each.TryGetInt32(out var n) ? n : 0)
            : 0;
    }

    private static string? Text(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? Hex(ReadOnlySpan<byte> bytes)
    {
        var text = Encoding.ASCII.GetString(bytes);
        return AuditChain.IsChainValue(text) ? text : null;
    }
}

/// <summary>What a compaction record seals.</summary>
/// <param name="Vault">The vault whose key signed it.</param>
/// <param name="PreviousLines">How many physical lines the compacted file held.</param>
/// <param name="Head">The hash of that file's last chained line, which the sealed lines end with.</param>
/// <param name="Digest">The digest of every chained line after the record, through <paramref name="Head"/>.</param>
/// <param name="Mac">HMAC-SHA256 under the vault's key over the vault and the three values before it.</param>
internal readonly record struct AuditSeal(string Vault, long PreviousLines, string Head, string Digest, string Mac);
