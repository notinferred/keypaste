using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Keypaste.Core.Audit;
using Keypaste.Infrastructure;

namespace Keypaste.Core.Ipc;

// An agent's request that the person save a value, and whether it was saved: no frame of either carries a value.
public static partial class ApproverProtocol
{
    internal const string SaveKind = "save";

    /// <summary>Encodes an agent's request to save a value.</summary>
    /// <param name="request">Where the value goes and why, and the attachment it is made under.</param>
    /// <returns>The frame's bytes, without a delimiter.</returns>
    public static byte[] Encode(SaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return JsonSerializer.SerializeToUtf8Bytes(new SaveRequestFrame(request), Wire.SaveRequestFrame);
    }

    /// <summary>Encodes whether a value was saved.</summary>
    /// <param name="reply">The answer.</param>
    /// <returns>The frame's bytes, without a delimiter.</returns>
    public static byte[] Encode(SaveReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        var frame = JsonSerializer.SerializeToUtf8Bytes(new SaveReplyFrame(reply), Wire.SaveReplyFrame);

        return frame.Length <= MessageFramer.MaximumPayloadBytes
            ? frame
            : JsonSerializer.SerializeToUtf8Bytes(new SaveReplyFrame(reply with { Reason = Oversized, Where = null }), Wire.SaveReplyFrame);
    }

    /// <summary>Decodes an agent's request to save a value.</summary>
    /// <param name="frame">The frame's bytes.</param>
    /// <param name="request">The decoded request.</param>
    /// <returns><see langword="true"/> when the frame was a well-formed save request.</returns>
    public static bool TryDecode(ReadOnlySpan<byte> frame, [NotNullWhen(true)] out SaveRequest? request)
    {
        request = TryAttached(frame, SaveKind, out var vault, out var session, out var root)
            && TryString(root, "item", out var item)
            && TryString(root, "field", out var field)
            && TryString(root, "project", out var project)
            && TryString(root, "environment", out var environment)
            && TryString(root, "key", out var key)
            && TryString(root, "reason", out var reason)
            && TryStrings(root, "exposure", out var exposure)
                ? new SaveRequest
                {
                    Item = item,
                    Field = field,
                    Project = project,
                    Environment = environment,
                    Key = key,
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

    /// <summary>Decodes whether a value was saved.</summary>
    /// <param name="frame">The frame's bytes.</param>
    /// <param name="reply">The decoded reply.</param>
    /// <returns><see langword="true"/> when the frame was a well-formed save reply.</returns>
    public static bool TryDecode(ReadOnlySpan<byte> frame, [NotNullWhen(true)] out SaveReply? reply)
    {
        reply = null;

        if (!TryParse(frame, out var document))
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;

            if (!IsKind(root, SaveKind)
                || !TryString(root, "decision", out var decision)
                || !TryInteger(root, "method", out var method)
                || !TryString(root, "reason", out var reason))
            {
                return false;
            }

            reply = new SaveReply(
                string.Equals(decision, "granted", StringComparison.Ordinal) ? AuditDecision.Granted : AuditDecision.Denied,
                Enum.IsDefined((AuditMethod)method) ? (AuditMethod)method : AuditMethod.Failed,
                reason)
            {
                Recorded = TrueOnly(root, "recorded"),
                Where = Optional(root, "where"),
                Session = Optional(root, "session"),
            };

            return true;
        }
    }
}
