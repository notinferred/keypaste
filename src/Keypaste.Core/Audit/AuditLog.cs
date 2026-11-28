using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Keypaste.Core.Internal;
using Polly;

namespace Keypaste.Core.Audit;

/// <summary>
/// The append-only local record of everything an agent asked keypaste for.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a precondition, not observability.</b> If a line cannot be written the call it
/// describes must be refused — otherwise breaking the logger becomes the way to obtain access that
/// leaves no trace, and an attacker's first move is to fill the disk or remove write permission.
/// SECURITY.md law 3 requires every access to be logged and law 7 requires every error path to
/// deny; together they mean no record, no disclosure. Callers are expected to honour that, and
/// THREATS.md T-6 states it as a property of the product.
/// </para>
/// <para>
/// <b>What "append-only" claims.</b> This class only ever adds records, one whole record at a time, at the
/// end of the file, and its only seek is to the end immediately before a write. The one code path that
/// rewrites the file is the vault's owner's <see cref="AuditCompaction"/>, which keeps every line but old
/// lookups and expired days byte for byte and seals the result. That is a statement about keypaste's own behaviour and
/// nothing more: the file belongs to the user's account, so anything running as that user can
/// rewrite it. Every record also carries a <see cref="AuditChain"/> link to the one
/// before it, so a rewrite that does not recompute the chain is detectable. Append-only by
/// construction within keypaste; tamper-<em>evident</em>; never tamper-<em>proof</em>.
/// </para>
/// <para>
/// <b>Two servers really do share one file</b> — Claude Desktop and Claude Code each spawn their
/// own. <see cref="FileMode.Append"/> is not enough to make that safe: .NET's
/// <see cref="FileStream"/> keeps its own idea of the file's length and writes at that offset, so
/// two streams opened on one path will happily overwrite each other's records. Appends therefore
/// take a sidecar lock file for the moment of the write and seek to the real end first. A lock file
/// left behind by a crash is harmless, because it is the open handle that excludes — not the file's
/// existence — and the operating system releases that when the process dies. The chain makes the
/// lock load-bearing twice over: it now also serialises <em>reading</em> the line a record links to.
/// </para>
/// <para>
/// <b>Why the log is opened twice, and for each append.</b> Deriving a record's <c>prev</c> means reading the end of the
/// file, and <see cref="FileMode.Append"/> forbids <see cref="FileAccess.Read"/>, so each append opens a writer in append
/// mode and a read-only handle on the same file. .NET throws on a seek before the append start, so an append cannot
/// rewrite a line. Both are opened under the sidecar lock rather than held for the process's life, because the vault's
/// owner replaces the file in one rename under that lock when it compacts the log (<see cref="AuditCompaction"/>):
/// a handle held across that rename would append to the file it replaced, and the line would be lost.
/// </para>
/// <para>
/// <b>Reading it while a server is running.</b> The file is opened <see cref="FileShare.ReadWrite"/>
/// so it can be read live, but a reader has to grant the same courtesy back:
/// <see cref="File.ReadAllLines(string)"/> and friends ask for <see cref="FileShare.Read"/>, which
/// denies other <em>writers</em> and therefore fails outright on Windows while any bridge
/// holds the log. <c>keypaste log</c> opens with <see cref="FileShare.ReadWrite"/> or it would not
/// work on the platform where people are most likely to have two clients running at once.
/// </para>
/// <para>
/// <b>Every line is one line.</b> Records are written with the default JSON encoder, which escapes
/// every non-ASCII character and every control character, so a newline inside a value can never
/// split a record across two physical lines — which is what keeps the file readable by
/// <c>jq</c>, by <c>keypaste log</c>, and by the chain verifier.
/// </para>
/// </remarks>
public sealed class AuditLog : IDisposable
{
    /// <summary>
    /// The most bytes one record may occupy, newline included.
    /// </summary>
    /// <remarks>
    /// Two appenders are kept apart by the sidecar lock every keypaste appender takes, not by the size
    /// of a write. The cap bounds the readers instead, since anything running as the person can write
    /// the file (THREATS.md assumption 1): the writer reads <see cref="TailWindowBytes"/> back to find
    /// the line a record links to, and the verifier buffers no line past four times the cap. It
    /// matches the 64 KiB the owner reads a request under, which lets a run's line name about a
    /// thousand items. A record over it is rewritten without the agent's reason, then
    /// without a run's command head, then without its caller, and refused if it still does not fit. The chain spends
    /// <see cref="AuditChain.ChainOverheadBytes"/> of it.
    /// </remarks>
    public const int MaximumRecordBytes = 64 * 1024;

    /// <summary>How much of the end of the file is read to find the record a new one links to.</summary>
    /// <remarks>
    /// Four times the record cap. It has to exceed twice the cap to be correct at all — an
    /// unterminated fragment of up to <see cref="MaximumRecordBytes"/> may sit after a last complete
    /// line of up to <see cref="MaximumRecordBytes"/> — and the rest is margin, because a window
    /// that fails to contain a complete line is indistinguishable from a corrupted log and would
    /// deny every credential request.
    /// </remarks>
    internal const int TailWindowBytes = 4 * MaximumRecordBytes;

    internal const string Transport = "stdio";

    /// <summary>The suffix of the sidecar file that serialises writers across processes.</summary>
    internal const string LockSuffix = ".lock";

    /// <summary>How many times to retry taking the lock before giving up and denying the call.</summary>
    internal const int LockAttempts = 50;

    /// <summary>How long to wait between attempts, in milliseconds.</summary>
    internal const int LockWaitMilliseconds = 20;

    private static readonly ResiliencePipeline _lockRetries = Retries.OnThisThread(
        LockAttempts, outcome => outcome.Exception is IOException or UnauthorizedAccessException, _ => Thread.Sleep(LockWaitMilliseconds));

    /// <summary>
    /// The timestamp format. Its width is fixed, which is what lets
    /// <see cref="AuditChain"/> find <c>seq</c> by position instead of by parsing.
    /// </summary>
    internal const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    private static readonly UnixFileMode _ownerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private readonly Lock _gate = new();
    private readonly TimeProvider _clock;
    private bool _disposed;

    private AuditLog(string path, TimeProvider clock, bool tightened)
    {
        Path = path;
        _clock = clock;
        TightenedPermissions = tightened;
    }

    /// <summary>The file being appended to.</summary>
    public string Path { get; }

    /// <summary>Raised after each append with why it failed, or null when the line was written.</summary>
    public event Action<string?>? AppendFinished;

    /// <summary>
    /// Whether an existing log was found readable by more than its owner and was tightened.
    /// </summary>
    /// <remarks>
    /// Reported rather than acted on silently. Changing the permissions of a user's file without
    /// saying so is not something a credentials tool should do; leaving a world-readable record of
    /// which credentials an agent asked for is worse. So it does both: tightens, and tells.
    /// Always false on Windows, which has no equivalent.
    /// </remarks>
    public bool TightenedPermissions { get; }

    /// <summary>Opens the log, creating the file and its directory if needed.</summary>
    /// <param name="path">Where the log lives.</param>
    /// <param name="clock">The clock timestamps come from.</param>
    /// <param name="log">The open log, on success.</param>
    /// <param name="error">A message naming the problem, or empty on success.</param>
    /// <returns><see langword="true"/> if the log is open, writable, and can be chained onto.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> or <paramref name="clock"/> is null.</exception>
    /// <remarks>
    /// Opened and inspected here, so "the log is writable" is a startup invariant rather than a surprise
    /// on the first call: the rare log that cannot be chained onto at all — one written by a newer
    /// keypaste — should stop the server at startup rather than surface as a mysterious denial an
    /// hour later. Everything else the end of the file might hold is recorded around rather than
    /// refused; <see cref="TryReadTail"/> says why. Each append opens the file again.
    /// </remarks>
    public static bool TryOpen(
        string path,
        TimeProvider clock,
        [NotNullWhen(true)] out AuditLog? log,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(clock);

        log = null;

        try
        {
            var full = System.IO.Path.GetFullPath(path);
            CreateDirectory(System.IO.Path.GetDirectoryName(full));

            using (OpenWriter(full))
            {
            }

            var tightened = Tighten(full);
            var opened = new AuditLog(full, clock, tightened);

            using (var tail = OpenTail(full))
            {
                if (!opened.TryReadTail(tail, out _, out error))
                {
                    return false;
                }
            }

            log = opened;
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or NotSupportedException or ArgumentException)
        {
            error = $"the audit log at '{path}' could not be opened: {ex.Message}";
            return false;
        }
    }

    /// <summary>Takes the sidecar lock every keypaste writer of the log at <paramref name="path"/> takes, or returns null when another process will not let go.</summary>
    /// <param name="path">The log's full path.</param>
    /// <returns>The held lock, released by disposing it.</returns>
    /// <remarks>
    /// Exclusion comes from holding the handle open, not from the file existing, so a lock file left
    /// behind by a killed process blocks nothing: the operating system closes its handles. That is
    /// what keeps this free of the stale-lock problem every "delete the lock file on exit" scheme
    /// eventually has. It excludes other keypaste processes and nothing else — a text editor or a
    /// `sed -i` writing at the same moment is a case the chain reports rather than prevents.
    /// </remarks>
    internal static FileStream? AcquireWriteLock(string path)
    {
        try
        {
            return _lockRetries.Execute(static lockPath => HomeFiles.Hold(lockPath), path + LockSuffix);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The writer an append uses: append mode, so it cannot seek back over a line.</summary>
    /// <remarks>
    /// Readable and deletable while it is held, so `keypaste log` works on a live file — including on
    /// Windows, where the default share mode would lock other readers out.
    /// </remarks>
    private static FileStream OpenWriter(string path) => HomeFiles.Append(path);

    /// <summary>The same file, for reading the line a new record links to.</summary>
    /// <remarks>
    /// FileShare.ReadWrite is not optional: a share mode is checked against every existing handle including this
    /// process's own writer, so asking for less collides with ourselves on Windows. Unbuffered, so there is no
    /// second copy of the end of the file to reason about.
    /// </remarks>
    private static FileStream OpenTail(string path) => new(path, new FileStreamOptions
    {
        Mode = FileMode.Open,
        Access = FileAccess.Read,
        Share = FileShare.ReadWrite | FileShare.Delete,
        BufferSize = 0,
    });

    /// <summary>Appends one record.</summary>
    /// <param name="record">What happened.</param>
    /// <param name="error">A message naming the problem, or empty on success.</param>
    /// <returns><see langword="false"/> when nothing was written, in which case the caller must deny.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="record"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The log has been disposed.</exception>
    public bool TryAppend(AuditRecord record, out string error)
    {
        ArgumentNullException.ThrowIfNull(record);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var written = Append([(_, _) => record], out error);
        AppendFinished?.Invoke(written ? null : error);
        return written;
    }

    /// <summary>Appends records as one write, chained in order, or none of them.</summary>
    /// <param name="records">What happened, one line each.</param>
    /// <param name="error">A message naming the problem, or empty on success.</param>
    /// <returns><see langword="false"/> when nothing was written, in which case the caller must deny every one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="records"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The log has been disposed.</exception>
    /// <remarks>Every line is composed and measured before any is written, so one that cannot be leaves the log as it was.</remarks>
    public bool TryAppendAll(IReadOnlyList<AuditRecord> records, out string error)
    {
        ArgumentNullException.ThrowIfNull(records);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var written = Append([.. records.Select(record => (Func<long, string, AuditRecord>)((_, _) => record))], out error);

        if (!written)
        {
            AppendFinished?.Invoke(error);
            return false;
        }

        foreach (var _ in records)
        {
            AppendFinished?.Invoke(null);
        }

        return true;
    }

    /// <summary>Appends a checkpoint signing the chain up to it, without raising <see cref="AppendFinished"/>.</summary>
    /// <param name="key">The vault's checkpoint key.</param>
    /// <param name="session">The unlocked session writing it.</param>
    /// <param name="error">Why it was not written, otherwise empty.</param>
    /// <returns>Whether the line was written.</returns>
    internal bool TryCheckpoint(AuditCheckpointKey key, string session, out string error)
    {
        ArgumentNullException.ThrowIfNull(key);
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Append([(sequence, previous) => AuditCheckpoint.Record(key, session, sequence, previous)], out error);
    }

    private bool Append(IReadOnlyList<Func<long, string, AuditRecord>> recordsAt, out string error)
    {
        lock (_gate)
        {
            // Declared before the try and released unconditionally in the finally: the shape CA2000
            // insists on, which is also the shape that survives an exception mid-write. Taken before
            // anything else now, because what a record links to is read from the file and must not
            // change between the read and the write.
            FileStream? writeLock = null;

            try
            {
                if (FileLocking.IsDisabled)
                {
                    error = $"the audit log at '{Path}' cannot be locked: {FileLocking.Refusal}";
                    return false;
                }

                writeLock = AcquireWriteLock(Path);
                if (writeLock is null)
                {
                    error = $"the audit log at '{Path}' is locked by another keypaste process";
                    return false;
                }

                using var stream = OpenWriter(Path);
                using var tailStream = OpenTail(Path);

                if (!TryReadTail(tailStream, out var tail, out error))
                {
                    return false;
                }

                var timestamp = _clock.GetUtcNow()
                    .UtcDateTime
                    .ToString(TimestampFormat, CultureInfo.InvariantCulture);

                var sequence = tail.Sequence;
                var previous = tail.Previous;
                using var lines = new MemoryStream();

                foreach (var recordAt in recordsAt)
                {
                    sequence++;

                    if (Fitted(recordAt(sequence, previous), timestamp, sequence, previous, out var hash, out error) is not { } bytes)
                    {
                        return false;
                    }

                    lines.Write(bytes);
                    previous = hash;
                }

                // Seek first: FileStream writes at its own idea of the end, which the read above may not share. Then one
                // write and a flush, so the lines land whole.
                stream.Seek(0, SeekOrigin.End);

                // The one byte keypaste ever writes that is not part of a record. A crash between a
                // record and its newline leaves a fragment; without this the next record is glued
                // onto it and both are lost. It adds bytes at the end and modifies none.
                if (tail.NeedsNewline)
                {
                    stream.Write("\n"u8);
                }

                stream.Write(lines.GetBuffer().AsSpan(0, (int)lines.Length));
                stream.Flush();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = $"the audit log at '{Path}' could not be written: {ex.Message}";
                return false;
            }
            finally
            {
                writeLock?.Dispose();
            }

            error = string.Empty;
            return true;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }

    /// <summary>What the end of the file says the next record must link to.</summary>
    /// <param name="Previous">The last chained line's hash, or <see cref="AuditChain.Genesis"/>.</param>
    /// <param name="Sequence">That line's position in the chain, or <c>0</c> when the chain starts here.</param>
    /// <param name="NeedsNewline">Whether the file ends mid-line and a newline must be written first.</param>
    private readonly record struct Tail(string Previous, long Sequence, bool NeedsNewline);

    /// <summary>
    /// Reads the last complete line and says what a new record should link to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It links to the last <em>chained</em> line, stepping backwards over anything that is not one
    /// — an unfinished write, a line something else appended — which is the same linking rule the
    /// verifier applies forwards. The two have to agree or a healthy log would read as a broken one.
    /// Stepping back cannot be steered: the last chained line is the last chained line, and reaching
    /// an older one would mean deleting the newer ones, which is truncation and is T-5's residual
    /// rather than a new one.
    /// </para>
    /// <para>
    /// <b>The alternative was worse.</b> Refusing to append when the file does not end in a record
    /// makes one appended byte — a blank line from an editor, an <c>echo</c> — a permanent denial of
    /// every credential request. Under assumption 1 that hands an attacker a cheaper lever than the
    /// one it was meant to close, and the honest response to a line keypaste did not write is to
    /// record around it and let the verifier report it.
    /// </para>
    /// <para>
    /// <b>Only two things reach <see cref="AuditChain.Genesis"/>:</b> a file with no chained line in
    /// it at all, and only when the whole file was examined. Never a read that failed, and never a
    /// window that did not reach the start of the file — silently starting a fresh chain when the
    /// end of the file cannot be understood is precisely what somebody who has just truncated it
    /// wants to happen.
    /// </para>
    /// </remarks>
    private bool TryReadTail(FileStream stream, out Tail tail, out string error)
    {
        tail = new Tail(AuditChain.Genesis, 0, NeedsNewline: false);
        error = string.Empty;
        byte[]? buffer = null;

        try
        {
            // Never cached: FileStream caches a length only when the share mode excludes other
            // writers, and this one does not.
            var length = stream.Length;
            if (length == 0)
            {
                return true;
            }

            var window = (int)Math.Min(length, TailWindowBytes);
            // Pooled: a new array this size per append is a large-object-heap allocation every time.
            buffer = ArrayPool<byte>.Shared.Rent(window);

            stream.Seek(length - window, SeekOrigin.Begin);
            stream.ReadExactly(buffer, 0, window);

            var span = buffer.AsSpan(0, window);
            var needsNewline = span[^1] != (byte)'\n';

            // Only the last segment can be unterminated. It is examined like any other line, because
            // a record that verifies is a record whether or not a newline follows it.
            var rest = needsNewline
                ? span
                : span[..^1];

            while (!rest.IsEmpty)
            {
                var start = rest.LastIndexOf((byte)'\n') + 1;
                var line = rest[start..];
                rest = start == 0 ? default : rest[..(start - 1)];

                // One trailing carriage return, and only one: a log copied through a Windows tool
                // has grown a `\r` on every line, and the verifier forgives exactly that much.
                if (!line.IsEmpty && line[^1] == (byte)'\r')
                {
                    line = line[..^1];
                }

                var inspected = AuditChain.Inspect(line);

                if (inspected.Kind == AuditLineKind.Chained)
                {
                    tail = new Tail(inspected.Hash, inspected.Sequence, needsNewline);
                    return true;
                }

                // A schema this version cannot read is the one thing worth refusing over: appending
                // beneath it would fork the chain, and "upgrade keypaste" is something the operator
                // can actually act on.
                if (inspected.Kind == AuditLineKind.Newer)
                {
                    error = Unchainable("it was written by a newer version of keypaste");
                    return false;
                }

                if (start == 0 && window < length)
                {
                    error = Unchainable(
                        $"no record could be found in the last {TailWindowBytes} bytes of it");

                    return false;
                }
            }

            // Nothing in this file is a chained record, and the whole of it was examined, so the
            // chain starts here.
            tail = tail with { NeedsNewline = needsNewline };
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"the audit log at '{Path}' could not be read: {ex.Message}";
            return false;
        }
        finally
        {
            if (buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    private string Unchainable(string because) =>
        $"the audit log at '{Path}' cannot be appended to because {because}. "
        + "Run 'keypaste log verify' to see what is in it, or move the file aside to start a new "
        + "log — the old one stays readable and stays verifiable.";

    private static void CreateDirectory(string? directory)
    {
        if (string.IsNullOrEmpty(directory) || Directory.Exists(directory))
        {
            return;
        }

        HomeFiles.CreateOwnerOnlyFolder(directory);
    }

    /// <summary>
    /// Narrows an existing log's permissions if they are wider than owner-only.
    /// </summary>
    /// <remarks>
    /// <see cref="FileStreamOptions.UnixCreateMode"/> applies only when the file is created, so a
    /// log that already existed keeps whatever mode it had. <c>env export</c> solves the same
    /// problem by deleting and recreating, which is not available to an append-only file.
    /// </remarks>
    private static bool Tighten(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        var mode = File.GetUnixFileMode(path);
        if ((mode & ~_ownerOnlyFile) == 0)
        {
            return false;
        }

        HomeFiles.MakeOwnerOnly(path);
        return true;
    }

    /// <summary>One record composed as it will be written, its agent's reason, command head and caller dropped in that order until it fits.</summary>
    /// <returns>The line, with the hash the next one links to, or null with the reason when even the smallest form is over the limit.</returns>
    private static byte[]? Fitted(AuditRecord record, string timestamp, long sequence, string previous, out string hash, out string error)
    {
        error = string.Empty;
        var bytes = Compose(record, timestamp, sequence, previous, withReasonExcerpt: true, out hash);

        if (bytes.Length > MaximumRecordBytes)
        {
            bytes = Compose(record, timestamp, sequence, previous, withReasonExcerpt: false, out hash);
        }

        // A run's command head goes next: its argv is still committed to by command_sha256.
        if (bytes.Length > MaximumRecordBytes && record.Command is not null)
        {
            bytes = Compose(record with { Command = null }, timestamp, sequence, previous, withReasonExcerpt: false, out hash);
        }

        if (bytes.Length > MaximumRecordBytes && record.Caller is not null)
        {
            bytes = Compose(record with { Command = null, Caller = null }, timestamp, sequence, previous, withReasonExcerpt: false, out hash);
        }

        if (bytes.Length > MaximumRecordBytes)
        {
            error = $"the record is {bytes.Length} bytes, over the {MaximumRecordBytes}-byte limit";
            return null;
        }

        return bytes;
    }

    /// <summary>
    /// Writes one record and seals it. The key order is fixed and documented, so "the bytes of a
    /// line" is a well-defined thing for the hash chain to commit to.
    /// </summary>
    /// <remarks>
    /// The seal is applied here rather than by the caller so that the bytes hashed are always the
    /// bytes written. Composition happens twice when a record is too long — once with the agent's
    /// reason and once without — and hashing anything but the composition that is actually chosen
    /// would give every oversized record a hash that does not match itself.
    /// </remarks>
    internal static byte[] Compose(
        AuditRecord record,
        string timestamp,
        long sequence,
        string previous,
        bool withReasonExcerpt,
        out string hash)
    {
        using var buffer = new MemoryStream(1024);

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            writer.WriteNumber("v", AuditRecord.SchemaVersion);
            writer.WriteString("ts", timestamp);
            writer.WriteNumber("seq", sequence);
            writer.WriteNumber("pid", Environment.ProcessId);

            writer.WriteStartObject("client");
            WriteOptional(writer, "name", record.Client.Name);
            WriteOptional(writer, "version", record.Client.Version);
            WriteOptional(writer, "label", record.Client.Label);
            writer.WriteString("transport", Transport);
            writer.WriteEndObject();

            if (record.Caller is { } caller)
            {
                writer.WriteStartObject("caller");
                if (caller.Pid is { } pid)
                {
                    writer.WriteNumber("pid", pid);
                }

                WriteOptional(writer, "path", caller.Path);
                WriteOptional(writer, "signature", caller.Signature is { } signature ? Wire(signature) : null);
                WriteOptional(writer, "signer", caller.Signer);
                writer.WriteEndObject();
            }

            writer.WriteString("tool", record.Tool);

            writer.WriteStartObject("args");
            var args = record.Args;
            WriteOptional(writer, "entry", args.Entry);
            if (args.EntryKind is { } kind)
            {
                writer.WriteString("entry_kind", Wire(kind));
            }

            WriteOptional(writer, "item", args.Item);
            WriteOptional(writer, "field", args.Field);
            if (withReasonExcerpt)
            {
                WriteOptional(writer, "reason_excerpt", args.ReasonExcerpt);
            }

            if (args.ReasonLength is { } length)
            {
                writer.WriteNumber("reason_len", length);
            }

            WriteOptional(writer, "reason_sha256", args.ReasonSha256);
            if (args.Query is { } query)
            {
                writer.WriteStartObject("query");
                WriteOptional(writer, "text", query.Text);
                WriteOptional(writer, "host", query.Host);
                WriteOptional(writer, "kind", query.Kind);
                WriteOptional(writer, "related", query.Related);
                WriteOptional(writer, "project", query.Project);
                writer.WriteEndObject();
            }

            if (args.Set is { } set)
            {
                writer.WriteStartObject("set");
                WriteOptional(writer, "id", set.Id);
                if (set.Position is { } position)
                {
                    writer.WriteNumber("position", position);
                }

                writer.WriteNumber("of", set.Of);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();

            writer.WriteString("decision", Wire(record.Decision));
            writer.WriteString("method", Wire(record.Method));
            writer.WriteString("reason", record.Reason);

            writer.WriteStartArray("exposure");
            foreach (var glob in record.Exposure)
            {
                writer.WriteStringValue(glob);
            }

            writer.WriteEndArray();

            WriteOptional(writer, "session", record.Session);
            WriteOptional(writer, "vault", record.Vault);

            if (record.Entries is { } entries)
            {
                writer.WriteStartArray("entries");
                foreach (var entry in entries)
                {
                    writer.WriteStringValue(entry);
                }

                writer.WriteEndArray();
            }

            WriteOptional(writer, "command", record.Command);
            WriteOptional(writer, "command_sha256", record.CommandSha256);
            WriteSummary(writer, record.Summary);
            WriteRemoval(writer, record.Removal);
            WriteOptional(writer, "checkpoint", record.Checkpoint);

            // Next to the vault and the chain footer, where AuditCompaction reads it by position.
            WriteOptional(writer, "compaction", record.Compaction);

            // Last, so the bytes the hash covers include the link. The other order would let a
            // line's link be re-pointed without disturbing its hash.
            writer.WriteString("prev", previous);

            writer.WriteEndObject();
        }

        // Everything except the closing brace: the line exactly as it stands immediately before the
        // hash member is appended, which is what AuditChain defines the hash as covering.
        var committed = buffer.GetBuffer().AsSpan(0, (int)buffer.Length - 1);
        hash = AuditChain.HashOf(committed);

        buffer.SetLength(buffer.Length - 1);
        buffer.Write(",\"hash\":\""u8);
        buffer.Write(Encoding.ASCII.GetBytes(hash));
        buffer.Write("\"}\n"u8);

        return buffer.ToArray();
    }

    private static void WriteSummary(Utf8JsonWriter writer, AuditSummary? summary)
    {
        if (summary is null)
        {
            return;
        }

        writer.WriteStartObject("summary");
        writer.WriteString("day", summary.Day);
        writer.WriteString("first", summary.First);
        writer.WriteString("last", summary.Last);
        writer.WriteStartArray("counts");
        foreach (var count in summary.Counts)
        {
            writer.WriteStartObject();
            writer.WriteString("tool", count.Tool);
            writer.WriteString("decision", Wire(count.Decision));
            writer.WriteNumber("count", count.Count);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteString("sha256", summary.Sha256);
        writer.WriteEndObject();
    }

    private static void WriteRemoval(Utf8JsonWriter writer, AuditRemoval? removal)
    {
        if (removal is null)
        {
            return;
        }

        writer.WriteStartObject("removed");
        writer.WriteString("from", removal.From);
        writer.WriteString("to", removal.To);
        writer.WriteNumber("count", removal.Count);
        writer.WriteString("sha256", removal.Sha256);
        writer.WriteEndObject();
    }

    private static void WriteOptional(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }

    internal static string Wire(AuditDecision decision) => decision switch
    {
        AuditDecision.Granted => "granted",
        _ => "denied",
    };

    /// <remarks>
    /// Every member is named, and the fallback is <c>unknown</c> rather than any real method: a real
    /// one would record a member added without a case here as something that never happened — the
    /// quietest possible way to make the log law 3 requires say something untrue. It does not throw, because an audit write must not
    /// be the thing that takes the server down.
    /// </remarks>
    internal static string Wire(AuditMethod method) => method switch
    {
        AuditMethod.VaultLocked => "vault-locked",
        AuditMethod.NotImplemented => "not-implemented",
        AuditMethod.OutOfScope => "out-of-scope",
        AuditMethod.InvalidRequest => "invalid-request",
        AuditMethod.Exposure => "exposure",
        AuditMethod.Prompt => "prompt",
        AuditMethod.AlwaysAllowed => "always-allowed",
        AuditMethod.TimedOut => "timed-out",
        AuditMethod.Cancelled => "cancelled",
        AuditMethod.NoApprover => "no-approver",
        AuditMethod.Busy => "busy",
        AuditMethod.Cooldown => "cooldown",
        AuditMethod.Failed => "failed",
        AuditMethod.NotInitialized => "not-initialized",
        AuditMethod.Undeliverable => "undeliverable",
        AuditMethod.NoSession => "no-session",
        AuditMethod.VaultChanged => "vault-changed",
        AuditMethod.Token => "token",
        AuditMethod.ShareCreated => "share-created",
        AuditMethod.ShareRevoked => "share-revoked",
        AuditMethod.InjectOnly => "inject-only",
        AuditMethod.Automatic => "automatic",
        AuditMethod.RunOff => "run-off",
        AuditMethod.Narrowing => "narrowing",
        AuditMethod.Unchanged => "unchanged",
        AuditMethod.Unfillable => "unfillable",
        AuditMethod.Merged => "merged",
        AuditMethod.NotArmed => "not-armed",
        AuditMethod.NotPaired => "not-paired",
        AuditMethod.Removed => "removed",
        AuditMethod.AutomaticCommands => "automatic-commands",
        AuditMethod.Allowed => "allowed",
        AuditMethod.Blocked => "blocked",
        AuditMethod.RulesChanged => "rules-changed",
        AuditMethod.Checkpoint => AuditCheckpoint.Method,
        AuditMethod.Folded => "folded",
        AuditMethod.Expired => "expired",
        AuditMethod.Compaction => AuditCompaction.Method,
        AuditMethod.Picked => "picked",
        AuditMethod.Pressed => "pressed",
        AuditMethod.Continued => "continued",
        _ => "unknown",
    };

    private static string Wire(CallerSignature signature) => signature switch
    {
        CallerSignature.NotSigned => "unsigned",
        CallerSignature.AdHoc => "adhoc",
        CallerSignature.Invalid => "invalid",
        CallerSignature.Valid => "valid",
        _ => "unknown",
    };

    private static string Wire(EntryAddressKind kind) => kind switch
    {
        EntryAddressKind.Handle => "handle",
        EntryAddressKind.Path => "path",
        _ => "invalid",
    };
}
