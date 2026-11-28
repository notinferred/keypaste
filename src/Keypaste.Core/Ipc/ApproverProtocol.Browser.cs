using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Keypaste.Infrastructure;

namespace Keypaste.Core.Ipc;

// The requests a browser's attachment may make, and their replies. Typed as every other request is: the relay sends the owner
// only what each extension message asks, never the extension's JSON.
public static partial class ApproverProtocol
{
    internal const string BrowserPairKind = "browser-pair";
    internal const string BrowserCheckKind = "browser-check";
    internal const string BrowserListKind = "browser-list";
    internal const string BrowserReleaseKind = "browser-release";
    internal const string BrowserShowKind = "browser-show";
    internal const string BrowserLockKind = "browser-lock";

    /// <summary>The kind of the reply to a pairing, a pairing check, a lock and a show, which carry nothing of an item.</summary>
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

    /// <summary>Encodes a browser's request for the names of a page's logins.</summary>
    /// <param name="request">The pairing, its key and the page, and the attachment it is made under.</param>
    /// <returns>The frame's bytes, without a delimiter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public static byte[] Encode(BrowserListRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return JsonSerializer.SerializeToUtf8Bytes(new BrowserListRequestFrame(request), Wire.BrowserListRequestFrame);
    }

    /// <summary>Encodes a browser's request to fill the login the person picked.</summary>
    /// <param name="request">The pairing, its key, the page and the item, and the attachment it is made under.</param>
    /// <returns>The frame's bytes, without a delimiter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public static byte[] Encode(BrowserReleaseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return JsonSerializer.SerializeToUtf8Bytes(new BrowserReleaseRequestFrame(request), Wire.BrowserReleaseRequestFrame);
    }

    /// <summary>Encodes a browser's request to bring keypaste forward.</summary>
    /// <param name="request">What to show, and the attachment it is made under.</param>
    /// <returns>The frame's bytes, without a delimiter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public static byte[] Encode(BrowserShowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return JsonSerializer.SerializeToUtf8Bytes(new BrowserShowRequestFrame(request), Wire.BrowserShowRequestFrame);
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

    /// <summary>Encodes the answer to a pairing, a pairing check, a lock or a show.</summary>
    /// <param name="reply">The answer.</param>
    /// <returns>The frame's bytes, without a delimiter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reply"/> is null.</exception>
    public static byte[] Encode(BrowserReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        return JsonSerializer.SerializeToUtf8Bytes(new BrowserReplyFrame(reply), Wire.BrowserReplyFrame);
    }

    /// <summary>Encodes the answer to a list.</summary>
    /// <param name="reply">The logins' names, or why none.</param>
    /// <returns>The frame's bytes, without a delimiter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reply"/> is null.</exception>
    public static byte[] Encode(BrowserListReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        return JsonSerializer.SerializeToUtf8Bytes(new BrowserListReplyFrame(reply), Wire.BrowserListReplyFrame);
    }

    /// <summary>Encodes the answer to a release.</summary>
    /// <param name="reply">The login released, or why none was.</param>
    /// <returns>The frame's bytes, without a delimiter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reply"/> is null.</exception>
    public static byte[] Encode(BrowserFillReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        return JsonSerializer.SerializeToUtf8Bytes(new BrowserFillReplyFrame(reply), Wire.BrowserFillReplyFrame);
    }

    /// <summary>Whether a release's answer fits one frame, so a release that would not is refused before anything leaves.</summary>
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

    /// <summary>Decodes a browser's request for the names of a page's logins.</summary>
    /// <param name="frame">The frame's bytes.</param>
    /// <param name="request">The decoded request.</param>
    /// <returns><see langword="true"/> when the frame was a well-formed list request.</returns>
    public static bool TryDecode(ReadOnlySpan<byte> frame, [NotNullWhen(true)] out BrowserListRequest? request)
    {
        request = null;

        if (!TryAttached(frame, BrowserListKind, out var vault, out var session, out var root)
            || !TryBrowserField(root, "pairing", out var pairing)
            || !TryBrowserField(root, "identification_key", out var key)
            || !TryString(root, "url", out var url)
            || !TrySubmitUrl(root)
            || !TryTopUrl(root)
            || !TryAsksTrust(root, out var asksTrust))
        {
            return false;
        }

        request = new BrowserListRequest(pairing, key, url)
        {
            SubmitUrl = Optional(root, "submit_url"),
            TopUrl = Optional(root, "top_url"),
            AsksTrust = asksTrust,
            Vault = vault,
            Session = session,
        };
        return true;
    }

    /// <summary>Decodes a browser's request to fill the login the person picked.</summary>
    /// <param name="frame">The frame's bytes.</param>
    /// <param name="request">The decoded request.</param>
    /// <returns><see langword="true"/> when the frame was a well-formed release request.</returns>
    public static bool TryDecode(ReadOnlySpan<byte> frame, [NotNullWhen(true)] out BrowserReleaseRequest? request)
    {
        request = null;

        if (!TryAttached(frame, BrowserReleaseKind, out var vault, out var session, out var root)
            || !TryBrowserField(root, "pairing", out var pairing)
            || !TryBrowserField(root, "identification_key", out var key)
            || !TryString(root, "url", out var url)
            || !TryBrowserField(root, "uuid", out var uuid)
            || !TrySubmitUrl(root)
            || !TryTopUrl(root)
            || !TryFill(root, out var fill)
            || !TryContinued(root, out var continued))
        {
            return false;
        }

        request = new BrowserReleaseRequest(pairing, key, url, uuid)
        {
            SubmitUrl = Optional(root, "submit_url"),
            TopUrl = Optional(root, "top_url"),
            Fill = fill,
            Continued = continued,
            Vault = vault,
            Session = session,
        };
        return true;
    }

    /// <summary>Decodes a browser's request to bring keypaste forward.</summary>
    /// <param name="frame">The frame's bytes.</param>
    /// <param name="request">The decoded request.</param>
    /// <returns><see langword="true"/> when the frame was a well-formed show request naming something this version shows.</returns>
    public static bool TryDecode(ReadOnlySpan<byte> frame, [NotNullWhen(true)] out BrowserShowRequest? request)
    {
        request = null;

        if (!TryAttached(frame, BrowserShowKind, out var vault, out var session, out var root)
            || !TryInteger(root, "what", out var what)
            || !Enum.IsDefined((BrowserShow)what)
            || !TryString(root, "vault_name", out var vaultName)
            || vaultName.Length > AttachClient.MaximumLength
            || !TryString(root, "url", out var url))
        {
            return false;
        }

        request = new BrowserShowRequest((BrowserShow)what) { VaultName = vaultName, Url = url, Vault = vault, Session = session };
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

    /// <summary>Decodes the answer to a pairing, a pairing check, a lock or a show.</summary>
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

    /// <summary>Decodes the answer to a list.</summary>
    /// <param name="frame">The frame's bytes.</param>
    /// <param name="reply">The decoded reply.</param>
    /// <returns><see langword="true"/> when the frame was a well-formed list reply, carrying names only when it says it was done.</returns>
    public static bool TryDecode(ReadOnlySpan<byte> frame, [NotNullWhen(true)] out BrowserListReply? reply)
    {
        reply = null;

        if (!TryParse(frame, out var document))
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;

            if (!IsKind(root, BrowserListKind)
                || !TryOutcome(root, out var outcome)
                || !TryString(root, "reason", out var reason)
                || !TryString(root, "site", out var site)
                || !TryString(root, "warning", out var warning)
                || !TryStrings(root, "locked_vaults", out var locked)
                || !root.TryGetProperty("names", out var rows)
                || rows.ValueKind != JsonValueKind.Array
                || (outcome != BrowserOutcome.Done && rows.GetArrayLength() > 0))
            {
                return false;
            }

            var names = new List<BrowserLoginName>(rows.GetArrayLength());

            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object
                    || !TryString(row, "uuid", out var uuid)
                    || !TryString(row, "title", out var title)
                    || !TryString(row, "username", out var username)
                    || !TryString(row, "vault", out var vaultName))
                {
                    return false;
                }

                names.Add(new BrowserLoginName(uuid, title, username) { Vault = vaultName });
            }

            reply = new BrowserListReply(outcome, reason) { Site = site, Warning = warning, LockedVaults = locked, Names = names };
            return true;
        }
    }

    /// <summary>Decodes the answer to a release.</summary>
    /// <param name="frame">The frame's bytes.</param>
    /// <param name="reply">The decoded reply.</param>
    /// <returns><see langword="true"/> when the frame was a well-formed release reply, carrying at most one login and only when it says it was done.</returns>
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

            if (!IsKind(root, BrowserReleaseKind)
                || !TryOutcome(root, out var outcome)
                || !TryString(root, "reason", out var reason)
                || !root.TryGetProperty("logins", out var rows)
                || rows.ValueKind != JsonValueKind.Array
                || rows.GetArrayLength() > 1
                || (outcome != BrowserOutcome.Done && rows.GetArrayLength() > 0)
                || !TryContinuation(root, out var continueSite, out var continueSeconds))
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
                    || !TryString(row, "uuid", out var uuid)
                    || (row.TryGetProperty("totp", out _) && !TryString(row, "totp", out _)))
                {
                    return false;
                }

                logins.Add(new BrowserLogin(login, password, name, uuid) { Totp = Optional(row, "totp") ?? string.Empty });
            }

            reply = new BrowserFillReply(outcome, reason) { Logins = logins, ContinueSite = continueSite, ContinueSeconds = continueSeconds };
            return true;
        }
    }

    /// <summary>A tab's URL beside a frame's, which a request may leave out or send as null, as an owner before frames never read it.</summary>
    private static bool TryTopUrl(JsonElement root) =>
        !root.TryGetProperty("top_url", out var top) || top.ValueKind == JsonValueKind.Null || TryString(root, "top_url", out _);

    /// <summary>The parts a fill is for, both when a relay from before them named none.</summary>
    private static bool TryFill(JsonElement root, out BrowserFill fill)
    {
        fill = BrowserFill.Both;

        if (!root.TryGetProperty("fill", out _))
        {
            return true;
        }

        if (!TryInteger(root, "fill", out var number) || !Enum.IsDefined((BrowserFill)number))
        {
            return false;
        }

        fill = (BrowserFill)number;
        return true;
    }

    /// <summary>Whether a fill continues the last one; only an explicit <c>true</c> says it does.</summary>
    /// <summary>Whether a frame's trust may be asked: yes unless the list says otherwise.</summary>
    private static bool TryAsksTrust(JsonElement root, out bool asks)
    {
        asks = true;

        return !root.TryGetProperty("asks_trust", out _) || TryBoolean(root, "asks_trust", out asks);
    }

    private static bool TryContinued(JsonElement root, out bool continued)
    {
        continued = false;

        return !root.TryGetProperty("continued", out _) || TryBoolean(root, "continued", out continued);
    }

    /// <summary>Where and for how long a fill may be continued: neither from an owner before continuations, and a site, a host name of at most 253 characters, only with a time.</summary>
    private static bool TryContinuation(JsonElement root, out string site, out int seconds)
    {
        site = string.Empty;
        seconds = 0;

        if (!root.TryGetProperty("continue_site", out _) && !root.TryGetProperty("continue_seconds", out _))
        {
            return true;
        }

        if (!TryString(root, "continue_site", out var named) || !TryInteger(root, "continue_seconds", out seconds)
            || named.Length > 253 || seconds < 0 || (named.Length == 0) != (seconds == 0))
        {
            return false;
        }

        site = named;
        return true;
    }

    /// <summary>A form's URL, which a request may leave out or send as null.</summary>
    private static bool TrySubmitUrl(JsonElement root) =>
        !root.TryGetProperty("submit_url", out var submit) || submit.ValueKind == JsonValueKind.Null || TryString(root, "submit_url", out _);

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
