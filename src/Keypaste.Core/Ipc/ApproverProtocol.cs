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
/// parse failure must cost that connection and nothing more (CORE.md law 3.7).
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
    public const int Version = 1;

    internal const string NamesKind = "names";
    internal const string CredentialKind = "credential";

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
        });
    }

    /// <summary>Encodes a reply carrying entry names.</summary>
    /// <param name="reply">The names, or the reason there are none.</param>
    /// <returns>The frame's bytes, without a delimiter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reply"/> is null.</exception>
    public static byte[] Encode(NamesReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        return Write(writer =>
        {
            writer.WriteNumber("v", Version);
            writer.WriteString("kind", NamesKind);
            writer.WriteBoolean("unlocked", reply.VaultUnlocked);
            writer.WriteString("reason", reply.Reason);
            writer.WriteStartArray("names");

            foreach (var name in reply.Names)
            {
                writer.WriteStartObject();
                writer.WriteString("group", name.GroupPath);
                writer.WriteString("title", name.Title);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        });
    }

    /// <summary>Encodes a reply to a credential request.</summary>
    /// <param name="reply">The decision, and the value on the one path that has one.</param>
    /// <returns>The frame's bytes, without a delimiter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reply"/> is null.</exception>
    public static byte[] Encode(CredentialReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        return Write(writer =>
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
    }

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

            reply = new NamesReply(unlocked.GetBoolean(), decoded, Optional(root, "reason") ?? string.Empty);
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

    private static string? Optional(JsonElement root, string name) =>
        TryString(root, name, out var value) ? value : null;
}
