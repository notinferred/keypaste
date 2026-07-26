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
/// <b>What "append-only" claims.</b> The file is opened with <see cref="FileMode.Append"/> and
/// written one whole record at a time, and there is no code path anywhere in keypaste that seeks,
/// truncates, rewrites, or deletes it. That is a statement about keypaste's own behaviour and
/// nothing more: the file belongs to the user's account, so anything running as that user can
/// rewrite it. Append-only by construction within keypaste; tamper-<em>evident</em> from Stage 2.4,
/// when the per-line hash chain arrives; never tamper-<em>proof</em>.
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

    private static readonly UnixFileMode _ownerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private static readonly UnixFileMode _ownerOnlyDirectory =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly Lock _gate = new();
    private readonly FileStream _stream;
    private readonly TimeProvider _clock;
    private long _sequence;
    private bool _disposed;

    private AuditLog(string path, FileStream stream, TimeProvider clock, bool tightened)
    {
        Path = path;
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

            try
            {
                // One write, then a flush: the record reaches the file whole or not at all, which
                // is what lets a second keypaste-mcp append to the same log without interleaving.
                _stream.Write(bytes);
                _stream.Flush();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = $"the audit log at '{Path}' could not be written: {ex.Message}";
                return false;
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

    private static string Wire(AuditMethod method) => method switch
    {
        AuditMethod.NotImplemented => "not-implemented",
        AuditMethod.OutOfScope => "out-of-scope",
        AuditMethod.InvalidRequest => "invalid-request",
        _ => "vault-locked",
    };

    private static string Wire(EntryAddressKind kind) => kind switch
    {
        EntryAddressKind.Handle => "handle",
        EntryAddressKind.Path => "path",
        _ => "invalid",
    };
}
