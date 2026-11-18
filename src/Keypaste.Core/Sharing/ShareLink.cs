namespace Keypaste.Core.Sharing;

/// <summary>A share link: <c>https://share.keypaste.com/#&lt;id&gt;.&lt;key&gt;</c>.</summary>
/// <remarks>
/// The id travels in the fragment beside the key, so the page request names nothing, access logs
/// and <c>Referer</c> hold no id, and the viewer is one static file. A browser never sends a
/// fragment to the server.
/// </remarks>
public static class ShareLink
{
    /// <summary>The bytes of a server-made share id.</summary>
    public const int IdBytes = 16;

    // Also matches a link made before the services left keypaste.com, whose viewer was at /s/.
    private const string _marker = "/#";

    /// <summary>The link for a share made on <paramref name="endpoint"/>, on the page <see cref="ShareEndpoint.PageFor"/> names.</summary>
    public static string Format(Uri endpoint, string id, string key)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(key);

        return ShareEndpoint.PageFor(endpoint).GetLeftPart(UriPartial.Authority) + _marker + id + "." + key;
    }

    /// <summary>Reads the id and key out of a link.</summary>
    public static bool TryParse(string link, out string id, out string key)
    {
        ArgumentNullException.ThrowIfNull(link);

        id = string.Empty;
        key = string.Empty;

        var at = link.IndexOf(_marker, StringComparison.Ordinal);
        if (at < 0)
        {
            return false;
        }

        var fragment = link.AsSpan(at + _marker.Length);
        var dot = fragment.IndexOf('.');
        if (dot < 0
            || !ShareCrypto.IsBase64Url(fragment[..dot], IdBytes)
            || !ShareCrypto.IsBase64Url(fragment[(dot + 1)..], ShareCrypto.KeyBytes))
        {
            return false;
        }

        id = fragment[..dot].ToString();
        key = fragment[(dot + 1)..].ToString();
        return true;
    }

    /// <summary>Whether text has the shape of a server-made share id.</summary>
    public static bool IsId(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return ShareCrypto.IsBase64Url(text, IdBytes);
    }
}
