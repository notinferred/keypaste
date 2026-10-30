using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Keypaste.Core.Infrastructure;

namespace Keypaste.Core.Ipc;

// The four requests a browser's attachment may make, and their replies (D-0627). Typed as every other request is: the relay
// sends the owner only what each extension message asks, never the extension's JSON.
public static partial class ApproverProtocol
{
    internal const string BrowserPairKind = "browser-pair";
    internal const string BrowserCheckKind = "browser-check";
    internal const string BrowserFillKind = "browser-fill";
    internal const string BrowserLockKind = "browser-lock";

    /// <summary>The kind of the reply to a pairing, a pairing check and a lock, which carry nothing of an item.</summary>
    internal const string BrowserReplyKind = "browser";

    /// <summary>Encodes a browser's request to pair.</summary>
    /// <param name="request">The identification key, and the attachment it is made under.</param>
    /// <returns>The frame's bytes, without a delimiter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public static byte[] Encode(BrowserPairRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return JsonSerializer.SerializeToUtf8Bytes(new BrowserPairRequestFrame(request), Wire.BrowserPairRequestFrame);
    }

    /// <summary>Encodes a browser's check of a pairing.</summary>
    /// <param name="request">The pairing and its key, and the attachment it is made under.</param>
    /// <returns>The frame's bytes, without a delimiter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public static byte[] Encode(BrowserCheckRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return JsonSerializer.SerializeToUtf8Bytes(new BrowserCheckRequestFrame(request), Wire.BrowserCheckRequestFrame);
    }

    /// <summary>Encodes a browser's request for a page's logins.</summary>
    /// <param name="request">The pairing, its key and the page, and the attachment it is made under.</param>
    /// <returns>The frame's bytes, without a delimiter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public static byte[] Encode(BrowserFillRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return JsonSerializer.SerializeToUtf8Bytes(new BrowserFillRequestFrame(request), Wire.BrowserFillRequestFrame);
    }

    /// <summary>Encodes a browser's request to lock now.</summary>
    /// <param name="request">The attachment it is made under.</param>
    /// <returns>The frame's bytes, without a delimiter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public static byte[] Encode(BrowserLockRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return WriteAttached(BrowserLockKind, request.Vault, request.Session);
    }

    /// <summary>Encodes the answer to a pairing, a pairing check or a lock.</summary>
    /// <param name="reply">The answer.</param>
    /// <returns>The frame's bytes, without a delimiter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reply"/> is null.</exception>
    public static byte[] Encode(BrowserReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        return JsonSerializer.SerializeToUtf8Bytes(new BrowserReplyFrame(reply), Wire.BrowserReplyFrame);
    }

    /// <summary>Encodes the answer to a fill.</summary>
    /// <param name="reply">The logins released, or why none were.</param>
    /// <returns>The frame's bytes, without a delimiter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reply"/> is null.</exception>
    public static byte[] Encode(BrowserFillReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        return JsonSerializer.SerializeToUtf8Bytes(new BrowserFillReplyFrame(reply), Wire.BrowserFillReplyFrame);
    }

    /// <summary>Whether a fill's answer fits one frame, so a release that would not is refused before anything leaves.</summary>
    /// <param name="reply">The answer.</param>
    /// <returns>True when it fits.</returns>
    internal static bool Fits(BrowserFillReply reply) => Encode(reply).Length <= MessageFramer.MaximumPayloadBytes;

    /// <summary>Decodes a browser's request to pair.</summary>
    /// <param name="frame">The frame's bytes.</param>
    /// <param name="request">The decoded request.</param>
    /// <returns><see langword="true"/> when the frame was a well-formed pairing request.</returns>
    public static bool TryDecode(ReadOnlySpan<byte> frame, [NotNullWhen(true)] out BrowserPairRequest? request)
    {
        request = TryAttached(frame, BrowserPairKind, out var vault, out var session, out var root)
            && TryBrowserField(root, "identification_key", out var key)
                ? new BrowserPairRequest(key) { Vault = vault, Session = session }
                : null;

        return request is not null;
    }

    /// <summary>Decodes a browser's check of a pairing.</summary>
    /// <param name="frame">The frame's bytes.</param>
    /// <param name="request">The decoded request.</param>
    /// <returns><see langword="true"/> when the frame was a well-formed pairing check.</returns>
    public static bool TryDecode(ReadOnlySpan<byte> frame, [NotNullWhen(true)] out BrowserCheckRequest? request)
    {
        request = TryAttached(frame, BrowserCheckKind, out var vault, out var session, out var root)
            && TryBrowserField(root, "pairing", out var pairing)
            && TryBrowserField(root, "identification_key", out var key)
                ? new BrowserCheckRequest(pairing, key) { Vault = vault, Session = session }
                : null;

        return request is not null;
    }

    /// <summary>Decodes a browser's request for a page's logins.</summary>
    /// <param name="frame">The frame's bytes.</param>
    /// <param name="request">The decoded request.</param>
    /// <returns><see langword="true"/> when the frame was a well-formed fill request.</returns>
    public static bool TryDecode(ReadOnlySpan<byte> frame, [NotNullWhen(true)] out BrowserFillRequest? request)
    {
        request = null;

        if (!TryAttached(frame, BrowserFillKind, out var vault, out var session, out var root)
            || !TryBrowserField(root, "pairing", out var pairing)
            || !TryBrowserField(root, "identification_key", out var key)
            || !TryString(root, "url", out var url)
            || (root.TryGetProperty("submit_url", out _) && !TryString(root, "submit_url", out _)))
        {
            return false;
        }

        request = new BrowserFillRequest(pairing, key, url) { SubmitUrl = Optional(root, "submit_url"), Vault = vault, Session = session };
        return true;
    }

    /// <summary>Decodes a browser's request to lock now.</summary>
    /// <param name="frame">The frame's bytes.</param>
    /// <param name="request">The decoded request.</param>
    /// <returns><see langword="true"/> when the frame was a well-formed lock request.</returns>
    public static bool TryDecode(ReadOnlySpan<byte> frame, [NotNullWhen(true)] out BrowserLockRequest? request)
    {
        request = TryAttached(frame, BrowserLockKind, out var vault, out var session, out _)
            ? new BrowserLockRequest { Vault = vault, Session = session }
            : null;

        return request is not null;
    }

    /// <summary>Decodes the answer to a pairing, a pairing check or a lock.</summary>
    /// <param name="frame">The frame's bytes.</param>
    /// <param name="reply">The decoded reply.</param>
    /// <returns><see langword="true"/> when the frame was a well-formed reply naming an outcome this version knows.</returns>
    public static bool TryDecode(ReadOnlySpan<byte> frame, [NotNullWhen(true)] out BrowserReply? reply)
    {
        reply = null;

        if (!TryParse(frame, out var document))
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;

            if (!IsKind(root, BrowserReplyKind)
                || !TryOutcome(root, out var outcome)
                || !TryString(root, "reason", out var reason)
                || (root.TryGetProperty("pairing", out _) && !TryBrowserField(root, "pairing", out _)))
            {
                return false;
            }

            reply = new BrowserReply(outcome, reason) { Pairing = Optional(root, "pairing") };
            return true;
        }
    }

    /// <summary>Decodes the answer to a fill.</summary>
    /// <param name="frame">The frame's bytes.</param>
    /// <param name="reply">The decoded reply.</param>
    /// <returns><see langword="true"/> when the frame was a well-formed fill reply, carrying logins only when it says it was done.</returns>
    public static bool TryDecode(ReadOnlySpan<byte> frame, [NotNullWhen(true)] out BrowserFillReply? reply)
    {
        reply = null;

        if (!TryParse(frame, out var document))
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;

            if (!IsKind(root, BrowserFillKind)
                || !TryOutcome(root, out var outcome)
                || !TryString(root, "reason", out var reason)
                || !root.TryGetProperty("logins", out var rows)
                || rows.ValueKind != JsonValueKind.Array
                || (outcome != BrowserOutcome.Done && rows.GetArrayLength() > 0))
            {
                return false;
            }

            var logins = new List<BrowserLogin>(rows.GetArrayLength());

            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object
                    || !TryString(row, "login", out var login)
                    || !TryString(row, "password", out var password)
                    || !TryString(row, "name", out var name)
                    || !TryString(row, "uuid", out var uuid))
                {
                    return false;
                }

                logins.Add(new BrowserLogin(login, password, name, uuid));
            }

            reply = new BrowserFillReply(outcome, reason) { Logins = logins };
            return true;
        }
    }

    /// <summary>A pairing's id or a key: a string of one to <see cref="AttachClient.MaximumLength"/> characters.</summary>
    private static bool TryBrowserField(JsonElement root, string name, [NotNullWhen(true)] out string? value) =>
        TryString(root, name, out value) && value.Length is > 0 and <= AttachClient.MaximumLength;

    private static bool TryOutcome(JsonElement root, out BrowserOutcome outcome)
    {
        outcome = default;

        if (!TryInteger(root, "outcome", out var number) || !Enum.IsDefined((BrowserOutcome)number))
        {
            return false;
        }

        outcome = (BrowserOutcome)number;
        return true;
    }
}
