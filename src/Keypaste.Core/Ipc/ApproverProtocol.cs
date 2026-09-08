using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Keypaste.Core.Audit;

namespace Keypaste.Core.Ipc;

/// <summary>
/// Turns approver messages into bytes and back, and refuses anything it does not recognise.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Utf8JsonWriter"/> to write and <see cref="JsonDocument"/> to read, never
/// <see cref="JsonSerializer"/>. That is not a style preference: reflection-based serialization
/// trips IL2026 and IL3050 under the trim and AOT analyzers this repository builds with, which was
/// demonstrated with a negative control rather than assumed (DECISIONS.md D-0019).
/// </para>
/// <para>
/// <b>Every read is a <c>Try</c>.</b> A malformed frame is a refusal, never an exception: the peer
/// on the other end of this pipe is a process, and the approver holds the unlocked vault, so a
/// parse failure must cost that connection and nothing more (docs/PRODUCT.md law 3.7).
/// </para>
/// <para>
/// <b>One transport is not one seam.</b> DECISIONS.md D-0022 forbids fusing the listing path and
/// the credential path. That still holds: they are different message kinds with different handlers,
/// and only <see cref="CredentialReply"/> has anywhere to put a secret. Sharing a pipe does not
/// fuse them; sharing an interface would have.
/// </para>
/// </remarks>
public static class ApproverProtocol
{
    /// <summary>The wire version, so a later change to the shape is unambiguous.</summary>
    /// <remarks>
    /// Stage 2.3 added an optional <c>client_label</c> to a credential request and deliberately did
    /// <b>not</b> bump this. The version guards how a frame is interpreted, and a bump would make
    /// every mixed-version pair fail at the framing layer — no reply, no audit line beyond
    /// <see cref="AuditMethod.NoApprover"/> — over one optional field. Both mismatched pairs degrade
    /// the same way instead: the label is absent, so no rule matches, so every request is shown to a
    /// person. That is the same state a malformed policy file produces, and it is the state this
    /// whole stage is built to fall back to.
    /// </remarks>
    public const int Version = 1;

    internal const string NamesKind = "names";
    internal const string CredentialKind = "credential";

    /// <summary>Stands in for a reply whose own envelope will not fit a frame.</summary>
    /// <remarks>
    /// Only reachable through a <see cref="NamesReply.Reason"/> longer than a frame, which nothing
    /// in this repository writes. It exists so that case has an answer that is a message rather than
    /// an exception.
    /// </remarks>
    internal const string Undersized = "the reply did not fit one message";

    /// <summary>Stands in for a refusal whose own explanation will not fit a frame.</summary>
    /// <remarks>
    /// Reachable through <c>ApproverHandler</c>'s glob failure, which interpolates an
    /// operator-supplied parse error into its reason with no cap of its own. The refusal keeps its
    /// method and loses only the detail: nothing was authorized on that path, so it must not borrow
    /// <see cref="UndeliverableReason"/>'s sentence.
    /// </remarks>
    internal const string Oversized = "the reason for this refusal did not fit one message";

    /// <summary>What the log and the bridge are told when an authorized release will not fit.</summary>
    /// <param name="authority">The method the release was granted under.</param>
    /// <returns>keypaste's own words, naming which authority allowed it.</returns>
    /// <remarks>
    /// Selected by the authority rather than written once, because a release a standing rule allowed
    /// must never be recorded as a human act (THREATS.md T-16). This is the only path in keypaste
    /// where a denial follows an authorization, so it is the only place where naming the authority
    /// on a denied line is meaningful at all.
    /// </remarks>
    internal static string UndeliverableReason(AuditMethod authority) => authority switch
    {
        AuditMethod.Prompt => "a person approved this release and it was too large to send in one reply",
        AuditMethod.GrantCache => "a person had already approved this release and it was too large to send in one reply",
        AuditMethod.Policy => "a standing rule authorized this release and it was too large to send in one reply",
        _ => "this release was authorized and it was too large to send in one reply",
    };

    /// <summary>Encodes a request for entry names.</summary>
    /// <param name="request">What to ask for.</param>
    /// <returns>The frame's bytes, without a delimiter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public static byte[] Encode(NamesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Write(writer =>
        {
            writer.WriteNumber("v", Version);
            writer.WriteString("kind", NamesKind);
            WriteStrings(writer, "exposure", request.Exposure);
        });
    }

    /// <summary>Encodes a credential request.</summary>
    /// <param name="request">What to ask for.</param>
    /// <returns>The frame's bytes, without a delimiter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public static byte[] Encode(CredentialRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Write(writer =>
        {
            writer.WriteNumber("v", Version);
            writer.WriteString("kind", CredentialKind);
            writer.WriteString("entry", request.Entry);
            writer.WriteString("field", request.Field);
            writer.WriteString("reason", request.Reason);
            writer.WriteNumber("ttl_seconds", request.TtlSeconds);
            WriteStrings(writer, "exposure", request.Exposure);
            WriteOptional(writer, "client", request.ClientName);
            WriteOptional(writer, "client_version", request.ClientVersion);
            WriteOptional(writer, "client_label", request.ClientLabel);
        });
    }

    /// <summary>Encodes a reply carrying entry names, bounded to what one frame can carry.</summary>
    /// <param name="reply">The names, or the reason there are none.</param>
    /// <returns>The frame's bytes, without a delimiter. Never over <see cref="MessageFramer.MaximumPayloadBytes"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reply"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// <b>This method is total: no reply, however large, can produce a frame the framer will
    /// refuse.</b> That refusal is correct where it is — a truncated frame is a message that says
    /// something other than what was sent — but it reaches
    /// <see cref="ApproverListener"/>'s outermost <c>catch</c>, which ends the connection and
    /// revokes the grants scoped to it. So an ordinary thousand-entry vault could cost a person the
    /// approval they had already given, and the agent was told only that something went wrong.
    /// </para>
    /// <para>
    /// <b>The bound is bytes, and it is measured here because only here knows what a name costs.</b>
    /// <see cref="Utf8JsonWriter"/>'s default encoder escapes every non-ASCII UTF-16 code unit to
    /// <c>\uXXXX</c>, so one astral rune is twelve bytes on the wire and a hundred short names can
    /// overflow where a thousand long ones would not. Any count outside this writer reimplements
    /// that escaping policy, and the day the two disagree by one byte the connection dies again.
    /// </para>
    /// <para>
    /// Names are dropped from the end and dropped whole. Shortening one would hand the bridge a
    /// string that is not the vault's, and could collapse two entries into one row;
    /// <see cref="Approval.IEntryNameLister"/> leaves altering names to whoever renders them.
    /// </para>
    /// </remarks>
    public static byte[] Encode(NamesReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        var kept = Fit(reply);
        var frame = WriteNames(reply, kept, reply.Complete && kept == reply.Names.Count);

        // Belt and braces. Everything above says this cannot happen; this is what stops an error in
        // that reasoning costing a connection and its grants rather than a listing (law 3.7).
        return frame.Length <= MessageFramer.MaximumPayloadBytes
            ? frame
            : WriteNames(new NamesReply(reply.VaultUnlocked, [], Undersized, false), 0, false);
    }

    /// <summary>How many of a reply's names fit one frame.</summary>
    /// <remarks>
    /// Counted before anything is written, because <see cref="Utf8JsonWriter"/> is forward-only and
    /// cannot take an element back. The envelope is measured by encoding it with no names and the
    /// <c>false</c> spelling of <c>complete</c> — the longer of the two, and the only one a
    /// truncated reply carries — so a reply that turns out to be complete is a byte under its own
    /// budget rather than a byte over. Each element is then measured on its own, which is exact
    /// because default encoding is context-free: an object encodes to identical bytes standalone and
    /// nested in an array.
    /// </remarks>
    private static int Fit(NamesReply reply)
    {
        var budget = MessageFramer.MaximumPayloadBytes - WriteNames(reply, 0, false).Length;

        if (budget < 0)
        {
            return 0;
        }

        var buffer = new ArrayBufferWriter<byte>(256);
        using var writer = new Utf8JsonWriter(buffer);

        var used = 0;
        var kept = 0;

        foreach (var name in reply.Names)
        {
            buffer.Clear();
            writer.Reset(buffer);
            WriteName(writer, name);
            writer.Flush();

            var cost = buffer.WrittenCount + (kept == 0 ? 0 : 1);

            if (used + cost > budget)
            {
                break;
            }

            used += cost;
            kept++;
        }

        return kept;
    }

    private static byte[] WriteNames(NamesReply reply, int count, bool complete) =>
        Write(writer =>
        {
            writer.WriteNumber("v", Version);
            writer.WriteString("kind", NamesKind);
            writer.WriteBoolean("unlocked", reply.VaultUnlocked);
            writer.WriteString("reason", reply.Reason);
            writer.WriteBoolean("complete", complete);
            writer.WriteStartArray("names");

            for (var i = 0; i < count; i++)
            {
                WriteName(writer, reply.Names[i]);
            }

            writer.WriteEndArray();
        });

    private static void WriteName(Utf8JsonWriter writer, EntryName name)
    {
        writer.WriteStartObject();
        writer.WriteString("group", name.GroupPath);
        writer.WriteString("title", name.Title);
        writer.WriteEndObject();
    }

    /// <summary>Encodes a reply to a credential request, bounded to what one frame can carry.</summary>
    /// <param name="reply">The decision, and the value on the one path that has one.</param>
    /// <returns>The frame's bytes, without a delimiter. Never over <see cref="MessageFramer.MaximumPayloadBytes"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reply"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// <b>This method is total</b>, for the reason <see cref="Encode(NamesReply)"/> gives and one
    /// more: <c>notes</c> is a releasable field and a KDBX note has no length limit, so a
    /// <em>granted</em> reply could exceed a frame. The write then threw,
    /// <see cref="ApproverListener"/>'s outermost <c>catch</c> ended the connection, and the
    /// <c>finally</c> revoked the grants scoped to it — after a person had approved the release,
    /// which the agent was then told had gone wrong.
    /// </para>
    /// <para>
    /// <b>A credential is refused whole, never trimmed.</b> A shortened secret is not a shorter
    /// secret, it is a different one, and a prefix of a live credential on the wire is most of what
    /// this bound exists to prevent. The substitute is built from scratch and has no value member at
    /// all, so no part of one can survive into it.
    /// </para>
    /// <para>
    /// <b>The substitution reads the original decision.</b> A refusal that was already a refusal —
    /// the reachable case is an uncapped glob error — keeps its own method and loses only its
    /// detail, because nothing was authorized there and borrowing
    /// <see cref="UndeliverableReason"/>'s sentence would put one untrue record in place of another.
    /// </para>
    /// </remarks>
    public static byte[] Encode(CredentialReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        var frame = WriteCredential(reply);

        if (frame.Length <= MessageFramer.MaximumPayloadBytes)
        {
            return frame;
        }

        var refusal = WriteCredential(Bounded(reply, reply.Entry));

        // Belt and braces, as on the listing path: the entry is capped by EntryNameSanitizer long
        // before it gets here, and this is what stops an error in that reasoning costing a
        // connection and its grants rather than one release (docs/PRODUCT.md law 3.7).
        return refusal.Length <= MessageFramer.MaximumPayloadBytes
            ? refusal
            : WriteCredential(Bounded(reply, entry: null));
    }

    /// <summary>Whether a reply would reach the peer as itself, rather than as a refusal.</summary>
    /// <param name="reply">The reply the handler is about to return.</param>
    /// <returns><see langword="true"/> when it fits one frame.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reply"/> is null.</exception>
    /// <remarks>
    /// The same question <see cref="Encode(CredentialReply)"/> asks, through the same writer and the
    /// same budget, so the two cannot answer it differently. The encoder needs it because the method
    /// must be total for any input; <c>ApproverHandler</c> needs it because it narrates a release to
    /// the operator's terminal before the reply has left the process, and that line is the only
    /// record the approver produces itself.
    /// </remarks>
    internal static bool Fits(CredentialReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        return WriteCredential(reply).Length <= MessageFramer.MaximumPayloadBytes;
    }

    private static CredentialReply Bounded(CredentialReply reply, string? entry) =>
        reply.Decision == AuditDecision.Granted
            ? new CredentialReply
            {
                Decision = AuditDecision.Denied,
                Method = AuditMethod.Undeliverable,
                Reason = UndeliverableReason(reply.Method),
                Entry = entry,
            }
            : new CredentialReply
            {
                Decision = AuditDecision.Denied,
                Method = reply.Method,
                Reason = Oversized,
                Entry = entry,
            };

    private static byte[] WriteCredential(CredentialReply reply) =>
        Write(writer =>
        {
            writer.WriteNumber("v", Version);
            writer.WriteString("kind", CredentialKind);
            writer.WriteString("decision", reply.Decision == AuditDecision.Granted ? "granted" : "denied");
            writer.WriteNumber("method", (int)reply.Method);
            writer.WriteString("reason", reply.Reason);
            writer.WriteNumber("ttl_seconds", reply.TtlSeconds);
            WriteOptional(writer, "entry", reply.Entry);
            WriteOptional(writer, "value", reply.Value);
        });

    /// <summary>Which kind of message a frame is, without committing to parsing it.</summary>
    /// <param name="frame">The frame's bytes.</param>
    /// <returns>The kind, or <see cref="ApproverMessageKind.Unknown"/> for anything unrecognised.</returns>
    public static ApproverMessageKind KindOf(ReadOnlySpan<byte> frame)
    {
        if (!TryParse(frame, out var document))
        {
            return ApproverMessageKind.Unknown;
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String)
            {
                return ApproverMessageKind.Unknown;
            }

            return kind.GetString() switch
            {
                NamesKind => ApproverMessageKind.Names,
                CredentialKind => ApproverMessageKind.Credential,
                _ => ApproverMessageKind.Unknown,
            };
        }
    }

    /// <summary>Decodes a request for entry names.</summary>
    /// <param name="frame">The frame's bytes.</param>
    /// <param name="request">The decoded request.</param>
    /// <returns><see langword="true"/> when the frame was a well-formed names request.</returns>
    public static bool TryDecode(ReadOnlySpan<byte> frame, [NotNullWhen(true)] out NamesRequest? request)
    {
        request = null;

        if (!TryParse(frame, out var document))
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;

            if (!IsKind(root, NamesKind) || !TryStrings(root, "exposure", out var exposure))
            {
                return false;
            }

            request = new NamesRequest(exposure);
            return true;
        }
    }

    /// <summary>Decodes a credential request.</summary>
    /// <param name="frame">The frame's bytes.</param>
    /// <param name="request">The decoded request.</param>
    /// <returns><see langword="true"/> when the frame was a well-formed credential request.</returns>
    public static bool TryDecode(ReadOnlySpan<byte> frame, [NotNullWhen(true)] out CredentialRequest? request)
    {
        request = null;

        if (!TryParse(frame, out var document))
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;

            if (!IsKind(root, CredentialKind)
                || !TryString(root, "entry", out var entry)
                || !TryString(root, "field", out var field)
                || !TryString(root, "reason", out var reason)
                || !TryInteger(root, "ttl_seconds", out var ttl)
                || !TryStrings(root, "exposure", out var exposure))
            {
                return false;
            }

            request = new CredentialRequest
            {
                Entry = entry,
                Field = field,
                Reason = reason,
                TtlSeconds = ttl,
                Exposure = exposure,
                ClientName = Optional(root, "client"),
                ClientVersion = Optional(root, "client_version"),
                ClientLabel = Optional(root, "client_label"),
            };

            return true;
        }
    }

    /// <summary>Decodes a reply carrying entry names.</summary>
    /// <param name="frame">The frame's bytes.</param>
    /// <param name="reply">The decoded reply.</param>
    /// <returns><see langword="true"/> when the frame was a well-formed names reply.</returns>
    public static bool TryDecode(ReadOnlySpan<byte> frame, [NotNullWhen(true)] out NamesReply? reply)
    {
        reply = null;

        if (!TryParse(frame, out var document))
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;

            if (!IsKind(root, NamesKind)
                || !root.TryGetProperty("unlocked", out var unlocked)
                || unlocked.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !root.TryGetProperty("names", out var names)
                || names.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var decoded = new List<EntryName>(names.GetArrayLength());

            foreach (var element in names.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object
                    || !TryString(element, "group", out var group)
                    || !TryString(element, "title", out var title))
                {
                    return false;
                }

                decoded.Add(new EntryName(group, title));
            }

            reply = new NamesReply(
                unlocked.GetBoolean(),
                decoded,
                Optional(root, "reason") ?? string.Empty,
                TrueOnly(root, "complete"));

            return true;
        }
    }

    /// <summary>Decodes a reply to a credential request.</summary>
    /// <param name="frame">The frame's bytes.</param>
    /// <param name="reply">The decoded reply.</param>
    /// <returns><see langword="true"/> when the frame was a well-formed credential reply.</returns>
    /// <remarks>
    /// An unrecognised method number decodes to <see cref="AuditMethod.Failed"/> rather than being
    /// refused outright, so a newer approver talking to an older bridge still produces a denial with
    /// a line in the log — the fail-closed direction.
    /// </remarks>
    public static bool TryDecode(ReadOnlySpan<byte> frame, [NotNullWhen(true)] out CredentialReply? reply)
    {
        reply = null;

        if (!TryParse(frame, out var document))
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;

            if (!IsKind(root, CredentialKind)
                || !TryString(root, "decision", out var decision)
                || !TryInteger(root, "method", out var method)
                || !TryString(root, "reason", out var reason)
                || !TryInteger(root, "ttl_seconds", out var ttl))
            {
                return false;
            }

            var granted = string.Equals(decision, "granted", StringComparison.Ordinal);
            var value = Optional(root, "value");

            // A granted reply with nothing in it is not a grant. Refusing here rather than handing
            // an agent an empty credential keeps the one path that releases a secret honest.
            if (granted && value is not { Length: > 0 })
            {
                return false;
            }

            reply = new CredentialReply
            {
                Decision = granted ? AuditDecision.Granted : AuditDecision.Denied,
                Method = Enum.IsDefined((AuditMethod)method) ? (AuditMethod)method : AuditMethod.Failed,
                Reason = reason,
                TtlSeconds = ttl,
                Entry = Optional(root, "entry"),
                Value = granted ? value : null,
            };

            return true;
        }
    }

    private static byte[] Write(Action<Utf8JsonWriter> body)
    {
        using var buffer = new MemoryStream(512);

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
    {
        writer.WriteStartArray(name);

        for (var i = 0; i < values.Count; i++)
        {
            writer.WriteStringValue(values[i]);
        }

        writer.WriteEndArray();
    }

    private static void WriteOptional(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }

    private static bool TryParse(ReadOnlySpan<byte> frame, [NotNullWhen(true)] out JsonDocument? document)
    {
        document = null;

        try
        {
            var parsed = JsonDocument.Parse(frame.ToArray());

            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                parsed.Dispose();
                return false;
            }

            document = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsKind(JsonElement root, string kind) =>
        root.TryGetProperty("v", out var version)
        && version.ValueKind == JsonValueKind.Number
        && version.TryGetInt32(out var number)
        && number == Version
        && TryString(root, "kind", out var actual)
        && string.Equals(actual, kind, StringComparison.Ordinal);

    private static bool TryString(JsonElement root, string name, [NotNullWhen(true)] out string? value)
    {
        value = null;

        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString();
        return value is not null;
    }

    private static bool TryInteger(JsonElement root, string name, out int value)
    {
        value = 0;

        return root.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out value);
    }

    private static bool TryStrings(JsonElement root, string name, [NotNullWhen(true)] out IReadOnlyList<string>? values)
    {
        values = null;

        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var decoded = new List<string>(element.GetArrayLength());

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            decoded.Add(item.GetString()!);
        }

        values = decoded;
        return true;
    }

    /// <summary>Reads a flag that only an explicit <c>true</c> may set.</summary>
    /// <remarks>
    /// <b>Absence is the negative, and the field is named for the answer that has to be earned.</b>
    /// <c>complete</c> is optional so the version need not bump (see <see cref="Version"/>), which
    /// means an approver from before this field existed sends a names reply without it — and that
    /// approver capped its listing at a thousand entries and said nothing. Reading absence as
    /// "complete" would turn its silence into a claim it never made. Read as "not complete" it is
    /// merely true. A field named <c>truncated</c> would have needed absence to mean <c>true</c>,
    /// which every later reader would have to relearn.
    /// </remarks>
    private static bool TrueOnly(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.True;

    private static string? Optional(JsonElement root, string name) =>
        TryString(root, name, out var value) ? value : null;
}
