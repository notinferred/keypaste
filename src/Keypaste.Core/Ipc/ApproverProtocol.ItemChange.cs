using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Keypaste.Core.Audit;
using Keypaste.Infrastructure;

namespace Keypaste.Core.Ipc;

// An agent's request that the person allow a change to an item, and whether it was made: no frame of either carries a value.
public static partial class ApproverProtocol
{
    internal const string ItemChangeKind = "item_change";

    /// <summary>Encodes an agent's request to change an item.</summary>
    /// <param name="request">The item, the change and why, and the attachment it is made under.</param>
    /// <returns>The frame's bytes, without a delimiter.</returns>
    public static byte[] Encode(ItemChangeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return JsonSerializer.SerializeToUtf8Bytes(new ItemChangeRequestFrame(request), Wire.ItemChangeRequestFrame);
    }

    /// <summary>Encodes whether an item was changed.</summary>
    /// <param name="reply">The answer.</param>
    /// <returns>The frame's bytes, without a delimiter.</returns>
    public static byte[] Encode(ItemChangeReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        var frame = JsonSerializer.SerializeToUtf8Bytes(new ItemChangeReplyFrame(reply), Wire.ItemChangeReplyFrame);

        return frame.Length <= MessageFramer.MaximumPayloadBytes
            ? frame
            : JsonSerializer.SerializeToUtf8Bytes(new ItemChangeReplyFrame(reply with { Reason = Oversized, Item = null }), Wire.ItemChangeReplyFrame);
    }

    /// <summary>Decodes an agent's request to change an item.</summary>
    /// <param name="frame">The frame's bytes.</param>
    /// <param name="request">The decoded request.</param>
    /// <returns><see langword="true"/> when the frame was a well-formed item change request.</returns>
    public static bool TryDecode(ReadOnlySpan<byte> frame, [NotNullWhen(true)] out ItemChangeRequest? request)
    {
        request = TryAttached(frame, ItemChangeKind, out var vault, out var session, out var root)
            && TryString(root, "entry", out var entry)
            && TryString(root, "change", out var change)
            && TryString(root, "expires", out var expires)
            && TryString(root, "reason", out var reason)
            && TryStrings(root, "exposure", out var exposure)
                ? new ItemChangeRequest
                {
                    Entry = entry,
                    Change = change,
                    Expires = expires,
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

    /// <summary>Decodes whether an item was changed.</summary>
    /// <param name="frame">The frame's bytes.</param>
    /// <param name="reply">The decoded reply.</param>
    /// <returns><see langword="true"/> when the frame was a well-formed item change reply.</returns>
    public static bool TryDecode(ReadOnlySpan<byte> frame, [NotNullWhen(true)] out ItemChangeReply? reply)
    {
        reply = null;

        if (!TryParse(frame, out var document))
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;

            if (!IsKind(root, ItemChangeKind)
                || !TryString(root, "decision", out var decision)
                || !TryInteger(root, "method", out var method)
                || !TryString(root, "reason", out var reason))
            {
                return false;
            }

            reply = new ItemChangeReply(
                string.Equals(decision, "granted", StringComparison.Ordinal) ? AuditDecision.Granted : AuditDecision.Denied,
                Enum.IsDefined((AuditMethod)method) ? (AuditMethod)method : AuditMethod.Failed,
                reason)
            {
                Recorded = TrueOnly(root, "recorded"),
                Item = Optional(root, "item"),
                Session = Optional(root, "session"),
            };

            return true;
        }
    }
}
