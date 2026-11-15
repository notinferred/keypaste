using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Infrastructure;

namespace Keypaste.Core.Ipc;

// An agent's linked set, and its reply: every field's value in one frame, or none of them.
public static partial class ApproverProtocol
{
    internal const string SetKind = "set";

    /// <summary>Encodes an agent's linked set.</summary>
    /// <param name="request">What the agent asked for, and the attachment it is made under.</param>
    /// <returns>The frame's bytes, without a delimiter.</returns>
    public static byte[] Encode(SetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return JsonSerializer.SerializeToUtf8Bytes(new SetRequestFrame(request), Wire.SetRequestFrame);
    }

    /// <summary>Encodes a set's reply whole, or, when its values will not fit one frame, a refusal carrying none of them.</summary>
    /// <param name="reply">The answer.</param>
    /// <returns>The frame's bytes, without a delimiter. Never over <see cref="MessageFramer.MaximumPayloadBytes"/>.</returns>
    /// <remarks>Never a set cut short: an agent given some of what a person approved together would act on a set nobody approved.</remarks>
    public static byte[] Encode(SetReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        var frame = WriteSet(reply);

        if (frame.Length <= MessageFramer.MaximumPayloadBytes)
        {
            return frame;
        }

        CryptographicOperations.ZeroMemory(frame);

        return WriteSet(new SetReply
        {
            Decision = AuditDecision.Denied,
            Method = reply.Decision == AuditDecision.Granted ? AuditMethod.Undeliverable : reply.Method,
            Reason = reply.Decision == AuditDecision.Granted ? UndeliverableReason(reply.Method) : Oversized,
            Session = reply.Session,
            Recorded = reply.Recorded,
        });
    }

    /// <summary>Whether a set's reply would reach the peer whole, measured through the same writer as <see cref="Encode(SetReply)"/>.</summary>
    /// <param name="reply">The reply as it will be sent, its session and <c>recorded</c> included.</param>
    /// <returns><see langword="true"/> when it fits one frame.</returns>
    internal static bool Fits(SetReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        return Measured(WriteSet(reply)) <= MessageFramer.MaximumPayloadBytes;
    }

    private static byte[] WriteSet(SetReply reply) =>
        JsonSerializer.SerializeToUtf8Bytes(new SetReplyFrame(reply), Wire.SetReplyFrame);

    /// <summary>Decodes an agent's linked set, bounded before anything is resolved.</summary>
    /// <param name="frame">The frame's bytes.</param>
    /// <param name="request">The decoded request.</param>
    /// <returns><see langword="true"/> when the frame was a well-formed set of <see cref="SetRequestRules.MinimumFields"/> to <see cref="SetRequestRules.MaximumFields"/> fields within every bound.</returns>
    public static bool TryDecode(ReadOnlySpan<byte> frame, [NotNullWhen(true)] out SetRequest? request)
    {
        request = TryAttached(frame, SetKind, out var vault, out var session, out var root)
            && TrySetItems(root, out var items)
            && TryString(root, "reason", out var reason)
            && reason.Length <= CredentialRequestRules.MaximumReasonLength
            && TryStrings(root, "exposure", out var exposure)
            && exposure.Count <= MaximumExposureGlobs
                ? new SetRequest
                {
                    Items = items,
                    Reason = reason,
                    Exposure = exposure,
                    ClientName = Optional(root, "client"),
                    ClientVersion = Optional(root, "client_version"),
                    ClientLabel = Optional(root, "client_label"),
                    Vault = vault,
                    Session = session,
                }
                : null;

        return request is not null;
    }

    /// <summary>Decodes a set's reply.</summary>
    /// <param name="frame">The frame's bytes.</param>
    /// <param name="reply">The decoded reply.</param>
    /// <returns>
    /// <see langword="true"/> when the frame was a well-formed reply: a grant carrying <see cref="SetRequestRules.MinimumFields"/> to
    /// <see cref="SetRequestRules.MaximumFields"/> fields, each with a value, or a refusal carrying none.
    /// </returns>
    public static bool TryDecode(ReadOnlySpan<byte> frame, [NotNullWhen(true)] out SetReply? reply)
    {
        reply = null;

        if (!TryParse(frame, out var document))
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;

            if (!IsKind(root, SetKind)
                || !TryString(root, "decision", out var decision)
                || !TryInteger(root, "method", out var method)
                || !TryString(root, "reason", out var reason)
                || !root.TryGetProperty("fields", out var rows)
                || rows.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var granted = string.Equals(decision, "granted", StringComparison.Ordinal);
            var count = rows.GetArrayLength();

            if (granted ? count is < SetRequestRules.MinimumFields or > SetRequestRules.MaximumFields : count != 0)
            {
                return false;
            }

            var fields = new List<SetField>(count);

            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object
                    || !TryString(row, "entry", out var entry)
                    || !TryString(row, "field", out var field)
                    || !TryString(row, "value", out var value)
                    || value.Length == 0
                    || !TryInteger(row, "code_seconds", out var codeSeconds))
                {
                    return false;
                }

                fields.Add(new SetField { Entry = entry, Field = field, Value = value, CodeSeconds = codeSeconds });
            }

            reply = new SetReply
            {
                Decision = granted ? AuditDecision.Granted : AuditDecision.Denied,
                Method = Enum.IsDefined((AuditMethod)method) ? (AuditMethod)method : AuditMethod.Failed,
                Reason = reason,
                Fields = fields,
                Session = Optional(root, "session"),
                Recorded = TrueOnly(root, "recorded"),
            };

            return true;
        }
    }

    private static bool TrySetItems(JsonElement root, [NotNullWhen(true)] out IReadOnlyList<SetItem>? items)
    {
        items = null;

        if (!root.TryGetProperty("items", out var rows)
            || rows.ValueKind != JsonValueKind.Array
            || rows.GetArrayLength() is < SetRequestRules.MinimumFields or > SetRequestRules.MaximumFields)
        {
            return false;
        }

        var decoded = new List<SetItem>(rows.GetArrayLength());

        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object
                || !TryString(row, "entry", out var entry)
                || entry.Length > CredentialRequestRules.MaximumEntryLength
                || !TryString(row, "field", out var field)
                || field.Length > CredentialFields.MaximumFieldLength)
            {
                return false;
            }

            decoded.Add(new SetItem(entry, field));
        }

        items = decoded;
        return true;
    }
}
