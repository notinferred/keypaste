using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace Keypaste.Core.Audit;

/// <summary>
/// The append-only local record of everything an agent asked keypaste for.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a precondition, not observability.</b> If a line cannot be written the call it
/// describes must be refused — otherwise breaking the logger becomes the way to obtain access that
/// leaves no trace, and an attacker's first move is to fill the disk or remove write permission.
/// docs/PRODUCT.md law 3.3 requires every access to be logged and law 3.7 requires every error path to
/// deny; together they mean no record, no disclosure. Callers are expected to honour that, and
/// THREATS.md T-6 states it as a property of the product.
/// </para>
/// <para>
/// <b>What "append-only" claims.</b> Records are only ever added, one whole record at a time, at the
/// end of the file; no code path in keypaste truncates, rewrites, or deletes it, and the only seek
/// is to the end immediately before a write. That is a statement about keypaste's own behaviour and
/// nothing more: the file belongs to the user's account, so anything running as that user can
/// rewrite it. Since Stage 2.4 every record also carries a <see cref="AuditChain"/> link to the one
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
/// <b>Why the log is opened twice.</b> Deriving a record's <c>prev</c> means reading the end of the
/// file, and <see cref="FileMode.Append"/> forbids <see cref="FileAccess.Read"/>. Rather than give
/// that up, the log keeps a second, read-only handle on the same file. Keeping the writer in append
/// mode is worth a handle: .NET itself throws on a seek before the append start, so "no code path in
/// keypaste rewrites the log" is a runtime invariant rather than a claim about the code — and it is
/// the sentence THREATS.md T-5's mitigation rests on.
/// </para>
/// <para>
/// <b>Reading it while a server is running.</b> The file is opened <see cref="FileShare.ReadWrite"/>
/// so it can be read live, but a reader has to grant the same courtesy back:
/// <see cref="File.ReadAllLines(string)"/> and friends ask for <see cref="FileShare.Read"/>, which
/// denies other <em>writers</em> and therefore fails outright on Windows while any keypaste-mcp
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
    /// A single small write is atomic against other appenders on a local filesystem, which is what
    /// lets two servers share one log. That guarantee thins out as records grow, so every field is
    /// capped and the total is checked; a record that somehow exceeds this is rewritten without the
    /// agent's reason, and if it still does not fit it is refused rather than written torn.
    /// Unchanged by the chain, which spends <see cref="AuditChain.ChainOverheadBytes"/> of it: the
    /// atomicity argument is about total bytes written, so raising the cap to absorb the chain would
    /// be a number chosen to hide a change rather than to state one.
    /// </remarks>
    public const int MaximumRecordBytes = 4096;

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

    /// <summary>
    /// The timestamp format. Its width is fixed, which is what lets
    /// <see cref="AuditChain"/> find <c>seq</c> by position instead of by parsing.
    /// </summary>
    internal const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    private static readonly UnixFileMode _ownerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private static readonly UnixFileMode _ownerOnlyDirectory =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly Lock _gate = new();
    private readonly FileStream _stream;
    private readonly FileStream _tail;
    private readonly TimeProvider _clock;
    private readonly string _lockPath;
    private bool _disposed;

    private AuditLog(string path, FileStream stream, FileStream tail, TimeProvider clock, bool tightened)
    {
        Path = path;
        _lockPath = path + LockSuffix;
        _stream = stream;
        _tail = tail;
        _clock = clock;
        TightenedPermissions = tightened;
    }

    /// <summary>The file being appended to.</summary>
    public string Path { get; }

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
    /// Opened once and held for the process's lifetime, which makes "the log is writable" a startup
    /// invariant rather than a surprise on the first call, and keeps a file open off the latency
    /// budget of every tool invocation. Since 2.4 the end of the file is inspected here too, for the
    /// same reason: the rare log that cannot be chained onto at all — one written by a newer
    /// keypaste — should stop the server at startup rather than surface as a mysterious denial an
    /// hour later. Everything else the end of the file might hold is recorded around rather than
    /// refused; <see cref="TryReadTail"/> says why.
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
        FileStream? stream = null;
        FileStream? tail = null;

        try
        {
            var full = System.IO.Path.GetFullPath(path);
            CreateDirectory(System.IO.Path.GetDirectoryName(full));

            var options = new FileStreamOptions
            {
                Mode = FileMode.Append,
                Access = FileAccess.Write,

                // Readable while we hold it, so `keypaste log` works on a live file — including on
                // Windows, where the default share mode would lock other readers out.
                Share = FileShare.ReadWrite,
            };

            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = _ownerOnlyFile;
            }

            stream = new FileStream(full, options);

            // The same file, for reading the line a new record links to. FileShare.ReadWrite is not
            // optional: a share mode is checked against every existing handle including this
            // process's own writer, so asking for less collides with ourselves on Windows.
            // Unbuffered, so there is no second copy of the end of the file to reason about.
            tail = new FileStream(full, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.ReadWrite,
                BufferSize = 0,
            });

            var tightened = Tighten(full);
            var opened = new AuditLog(full, stream, tail, clock, tightened);

            if (!opened.TryReadTail(out _, out error))
            {
                opened.Dispose();
                return false;
            }

            log = opened;
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or NotSupportedException or ArgumentException)
        {
            stream?.Dispose();
            tail?.Dispose();
            error = $"the audit log at '{path}' could not be opened: {ex.Message}";
            return false;
        }
    }

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

        lock (_gate)
        {
            // Declared before the try and released unconditionally in the finally: the shape CA2000
            // insists on, which is also the shape that survives an exception mid-write. Taken before
            // anything else now, because what a record links to is read from the file and must not
            // change between the read and the write.
            FileStream? writeLock = null;

            try
            {
                writeLock = AcquireWriteLock();
                if (writeLock is null)
                {
                    error = $"the audit log at '{Path}' is locked by another keypaste process";
                    return false;
                }

                if (!TryReadTail(out var tail, out error))
                {
                    return false;
                }

                var timestamp = _clock.GetUtcNow()
                    .UtcDateTime
                    .ToString(TimestampFormat, CultureInfo.InvariantCulture);

                var sequence = tail.Sequence + 1;
                var bytes = Compose(record, timestamp, sequence, tail.Previous, withReasonExcerpt: true);

                if (bytes.Length > MaximumRecordBytes)
                {
                    bytes = Compose(record, timestamp, sequence, tail.Previous, withReasonExcerpt: false);
                }

                if (bytes.Length > MaximumRecordBytes)
                {
                    error = $"the record is {bytes.Length} bytes, over the {MaximumRecordBytes}-byte limit";
                    return false;
                }

                // Seek first: another process may have appended since this stream last wrote, and
                // FileStream would otherwise write at its own stale idea of the end and destroy
                // that record. Then one write and a flush, so the line lands whole.
                _stream.Seek(0, SeekOrigin.End);

                // The one byte keypaste ever writes that is not part of a record. A crash between a
                // record and its newline leaves a fragment; without this the next record is glued
                // onto it and both are lost. It adds bytes at the end and modifies none.
                if (tail.NeedsNewline)
                {
                    _stream.Write("\n"u8);
                }

                _stream.Write(bytes);
                _stream.Flush();
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
        _stream.Dispose();
        _tail.Dispose();
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
    private bool TryReadTail(out Tail tail, out string error)
    {
        tail = new Tail(AuditChain.Genesis, 0, NeedsNewline: false);
        error = string.Empty;

        try
        {
            // Never cached: FileStream caches a length only when the share mode excludes other
            // writers, and this one does not.
            var length = _tail.Length;
            if (length == 0)
            {
                return true;
            }

            var window = (int)Math.Min(length, TailWindowBytes);
            var buffer = new byte[window];

            _tail.Seek(length - window, SeekOrigin.Begin);
            _tail.ReadExactly(buffer, 0, window);

            var span = buffer.AsSpan();
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
    }

    private string Unchainable(string because) =>
        $"the audit log at '{Path}' cannot be appended to because {because}. "
        + "Run 'keypaste log verify' to see what is in it, or move the file aside to start a new "
        + "log — the old one stays readable and stays verifiable.";

    /// <summary>
    /// Takes the sidecar lock, or returns null when another process will not let go.
    /// </summary>
    /// <remarks>
    /// Exclusion comes from holding the handle open, not from the file existing, so a lock file left
    /// behind by a killed process blocks nothing: the operating system closes its handles. That is
    /// what keeps this free of the stale-lock problem every "delete the lock file on exit" scheme
    /// eventually has. It excludes other keypaste processes and nothing else — a text editor or a
    /// `sed -i` writing at the same moment is a case the chain reports rather than prevents.
    /// </remarks>
    private FileStream? AcquireWriteLock()
    {
        for (var attempt = 0; attempt < LockAttempts; attempt++)
        {
            try
            {
                return new FileStream(
                    _lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.Write,
                    FileShare.None);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(LockWaitMilliseconds);
            }
        }

        return null;
    }

    private static void CreateDirectory(string? directory)
    {
        if (string.IsNullOrEmpty(directory) || Directory.Exists(directory))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
            return;
        }

        Directory.CreateDirectory(directory, _ownerOnlyDirectory);
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

        File.SetUnixFileMode(path, _ownerOnlyFile);
        return true;
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
    private static byte[] Compose(
        AuditRecord record,
        string timestamp,
        long sequence,
        string previous,
        bool withReasonExcerpt)
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

            writer.WriteString("tool", record.Tool);

            writer.WriteStartObject("args");
            var args = record.Args;
            WriteOptional(writer, "entry", args.Entry);
            if (args.EntryKind is { } kind)
            {
                writer.WriteString("entry_kind", Wire(kind));
            }

            WriteOptional(writer, "field", args.Field);
            if (args.TtlSeconds is { } ttl)
            {
                writer.WriteNumber("ttl_seconds", ttl);
            }

            if (withReasonExcerpt)
            {
                WriteOptional(writer, "reason_excerpt", args.ReasonExcerpt);
            }

            if (args.ReasonLength is { } length)
            {
                writer.WriteNumber("reason_len", length);
            }

            WriteOptional(writer, "reason_sha256", args.ReasonSha256);
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

            // Last, so the bytes the hash covers include the link. The other order would let a
            // line's link be re-pointed without disturbing its hash.
            writer.WriteString("prev", previous);

            writer.WriteEndObject();
        }

        // Everything except the closing brace: the line exactly as it stands immediately before the
        // hash member is appended, which is what AuditChain defines the hash as covering.
        var committed = buffer.GetBuffer().AsSpan(0, (int)buffer.Length - 1);
        var hash = Encoding.ASCII.GetBytes(AuditChain.HashOf(committed));

        buffer.SetLength(buffer.Length - 1);
        buffer.Write(",\"hash\":\""u8);
        buffer.Write(hash);
        buffer.Write("\"}\n"u8);

        return buffer.ToArray();
    }

    private static void WriteOptional(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }

    private static string Wire(AuditDecision decision) => decision switch
    {
        AuditDecision.Granted => "granted",
        _ => "denied",
    };

    /// <remarks>
    /// Every member is named, and the fallback is <c>unknown</c> rather than any real method. It
    /// used to be <c>vault-locked</c>, which meant a member added without a case here would have
    /// been recorded as a denial that never happened — the quietest possible way to make the log
    /// law 3.3 requires say something untrue. It does not throw, because an audit write must not
    /// be the thing that takes the server down.
    /// </remarks>
    private static string Wire(AuditMethod method) => method switch
    {
        AuditMethod.VaultLocked => "vault-locked",
        AuditMethod.NotImplemented => "not-implemented",
        AuditMethod.OutOfScope => "out-of-scope",
        AuditMethod.InvalidRequest => "invalid-request",
        AuditMethod.Exposure => "exposure",
        AuditMethod.Prompt => "prompt",
        AuditMethod.GrantCache => "grant-cache",
        AuditMethod.TimedOut => "timed-out",
        AuditMethod.Cancelled => "cancelled",
        AuditMethod.NoApprover => "no-approver",
        AuditMethod.Busy => "busy",
        AuditMethod.Cooldown => "cooldown",
        AuditMethod.Failed => "failed",
        AuditMethod.Policy => "policy",
        AuditMethod.PolicyLimit => "policy-limit",
        AuditMethod.NotInitialized => "not-initialized",
        _ => "unknown",
    };

    private static string Wire(EntryAddressKind kind) => kind switch
    {
        EntryAddressKind.Handle => "handle",
        EntryAddressKind.Path => "path",
        _ => "invalid",
    };
}
