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
/// CORE.md law 3.3 requires every access to be logged and law 3.7 requires every error path to
/// deny; together they mean no record, no disclosure. Callers are expected to honour that, and
/// THREATS.md T-6 states it as a property of the product.
/// </para>
/// <para>
/// <b>What "append-only" claims.</b> Records are only ever added, one whole record at a time, at the
/// end of the file; no code path in keypaste truncates, rewrites, or deletes it, and the only seek
/// is to the end immediately before a write. That is a statement about keypaste's own behaviour and
/// nothing more: the file belongs to the user's account, so anything running as that user can
/// rewrite it. Append-only by construction within keypaste; tamper-<em>evident</em> from Stage 2.4,
/// when the per-line hash chain arrives; never tamper-<em>proof</em>.
/// </para>
/// <para>
/// <b>Two servers really do share one file</b> — Claude Desktop and Claude Code each spawn their
/// own. <see cref="FileMode.Append"/> is not enough to make that safe: .NET's
/// <see cref="FileStream"/> keeps its own idea of the file's length and writes at that offset, so
/// two streams opened on one path will happily overwrite each other's records. Appends therefore
/// take a sidecar lock file for the moment of the write and seek to the real end first. A lock file
/// left behind by a crash is harmless, because it is the open handle that excludes — not the file's
/// existence — and the operating system releases that when the process dies.
/// </para>
/// <para>
/// <b>Reading it while a server is running.</b> The file is opened <see cref="FileShare.ReadWrite"/>
/// so it can be read live, but a reader has to grant the same courtesy back:
/// <see cref="File.ReadAllLines(string)"/> and friends ask for <see cref="FileShare.Read"/>, which
/// denies other <em>writers</em> and therefore fails outright on Windows while any keypaste-mcp
/// holds the log. Stage 2.4's <c>keypaste log</c> must open with
/// <see cref="FileShare.ReadWrite"/> or it will not work on the platform where people are most
/// likely to have two clients running at once.
/// </para>
/// <para>
/// <b>Every line is one line.</b> Records are written with the default JSON encoder, which escapes
/// every non-ASCII character and every control character, so a newline inside a value can never
/// split a record across two physical lines — which is what keeps the file readable by
/// <c>jq</c>, by <c>keypaste log</c>, and by Stage 2.4's chain verifier.
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
    /// </remarks>
    public const int MaximumRecordBytes = 4096;

    internal const string Transport = "stdio";

    /// <summary>The suffix of the sidecar file that serialises writers across processes.</summary>
    internal const string LockSuffix = ".lock";

    /// <summary>How many times to retry taking the lock before giving up and denying the call.</summary>
    internal const int LockAttempts = 50;

    /// <summary>How long to wait between attempts, in milliseconds.</summary>
    internal const int LockWaitMilliseconds = 20;

    private static readonly UnixFileMode _ownerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private static readonly UnixFileMode _ownerOnlyDirectory =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly Lock _gate = new();
    private readonly FileStream _stream;
    private readonly TimeProvider _clock;
    private readonly string _lockPath;
    private long _sequence;
    private bool _disposed;

    private AuditLog(string path, FileStream stream, TimeProvider clock, bool tightened)
    {
        Path = path;
        _lockPath = path + LockSuffix;
        _stream = stream;
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
    /// <returns><see langword="true"/> if the log is open and writable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> or <paramref name="clock"/> is null.</exception>
    /// <remarks>
    /// Opened once and held for the process's lifetime, which makes "the log is writable" a startup
    /// invariant rather than a surprise on the first call, and keeps a file open off the latency
    /// budget of every tool invocation.
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
            var tightened = Tighten(full);

            log = new AuditLog(full, stream, clock, tightened);
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or NotSupportedException or ArgumentException)
        {
            stream?.Dispose();
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
            var timestamp = _clock.GetUtcNow()
                .UtcDateTime
                .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

            var sequence = _sequence + 1;
            var bytes = Compose(record, timestamp, sequence, withReasonExcerpt: true);

            if (bytes.Length > MaximumRecordBytes)
            {
                bytes = Compose(record, timestamp, sequence, withReasonExcerpt: false);
            }

            if (bytes.Length > MaximumRecordBytes)
            {
                error = $"the record is {bytes.Length} bytes, over the {MaximumRecordBytes}-byte limit";
                return false;
            }

            // Declared before the try and released unconditionally in the finally: the shape CA2000
            // insists on, which is also the shape that survives an exception mid-write.
            FileStream? writeLock = null;

            try
            {
                writeLock = AcquireWriteLock();
                if (writeLock is null)
                {
                    error = $"the audit log at '{Path}' is locked by another keypaste process";
                    return false;
                }

                // Seek first: another process may have appended since this stream last wrote, and
                // FileStream would otherwise write at its own stale idea of the end and destroy
                // that record. Then one write and a flush, so the line lands whole.
                _stream.Seek(0, SeekOrigin.End);
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

            _sequence = sequence;
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
    }

    /// <summary>
    /// Takes the sidecar lock, or returns null when another process will not let go.
    /// </summary>
    /// <remarks>
    /// Exclusion comes from holding the handle open, not from the file existing, so a lock file left
    /// behind by a killed process blocks nothing: the operating system closes its handles. That is
    /// what keeps this free of the stale-lock problem every "delete the lock file on exit" scheme
    /// eventually has.
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
    /// Writes one record. The key order is fixed and documented, so "the bytes of a line" is
    /// already a well-defined thing for Stage 2.4's hash chain to commit to.
    /// </summary>
    private static byte[] Compose(AuditRecord record, string timestamp, long sequence, bool withReasonExcerpt)
    {
        using var buffer = new MemoryStream(512);

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

            writer.WriteEndObject();
        }

        buffer.WriteByte((byte)'\n');
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
        _ => "unknown",
    };

    private static string Wire(EntryAddressKind kind) => kind switch
    {
        EntryAddressKind.Handle => "handle",
        EntryAddressKind.Path => "path",
        _ => "invalid",
    };
}
