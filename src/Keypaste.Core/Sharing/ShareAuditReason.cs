using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Keypaste.Core.Sharing;

/// <summary>How a share-created audit line words its reason, and how a reader gets the recipient and expiry back.</summary>
/// <remarks>One format, written and read here, so a screen naming "maya@acme.dev · Link · 24h" cannot drift from the line.</remarks>
public static class ShareAuditReason
{
    private const string _prefix = "share ";
    private const string _expires = ", expires ";
    private const string _to = ", to ";
    private const string _passphrase = ", passphrase";
    private const string _iso = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    /// <summary>What a link with no recipient label is recorded as being for.</summary>
    public const string Anyone = "anyone with the link";

    /// <summary>The reason a share-created line carries.</summary>
    /// <param name="info">The share as stored.</param>
    /// <returns><c>share &lt;id&gt;: &lt;views&gt;, expires &lt;ISO&gt;, to &lt;recipient&gt;[, passphrase]</c>.</returns>
    public static string Created(ShareInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        var views = info.Views == 1 ? "1 view" : $"{info.Views} views";
        var expires = info.Expires.UtcDateTime.ToString(_iso, CultureInfo.InvariantCulture);
        var passphrase = info.Passphrase ? _passphrase : string.Empty;

        return string.Create(CultureInfo.InvariantCulture, $"{_prefix}{info.Id}: {views}{_expires}{expires}{_to}{info.Recipient ?? Anyone}{passphrase}");
    }

    /// <summary>The recipient and expiry in a reason <see cref="Created"/> wrote.</summary>
    /// <param name="reason">A line's reason.</param>
    /// <param name="recipient">Who the link was for, or <see cref="Anyone"/>.</param>
    /// <param name="expires">When the link stops opening.</param>
    /// <returns>Whether the reason had both.</returns>
    public static bool TryRead(string reason, [NotNullWhen(true)] out string? recipient, out DateTimeOffset expires)
    {
        ArgumentNullException.ThrowIfNull(reason);

        recipient = null;
        expires = default;

        if (!reason.StartsWith(_prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var at = reason.IndexOf(_expires, StringComparison.Ordinal);
        var to = at < 0 ? -1 : reason.IndexOf(_to, at, StringComparison.Ordinal);

        if (to < 0
            || !DateTimeOffset.TryParseExact(
                reason[(at + _expires.Length)..to],
                _iso,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out expires))
        {
            return false;
        }

        var rest = reason[(to + _to.Length)..];
        recipient = rest.EndsWith(_passphrase, StringComparison.Ordinal) ? rest[..^_passphrase.Length] : rest;

        return recipient.Length > 0;
    }
}
