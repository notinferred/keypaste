using System.Diagnostics.CodeAnalysis;

namespace Keypaste.Core.Browser;

/// <summary>A page's request for logins, as KeePassXC-Browser names it, and which items' web addresses it matches (D-0617).</summary>
/// <remarks>
/// <para>
/// An item matches a page on its own host, or a subdomain of it at a label boundary, within one registrable domain, and on the
/// same scheme and port: <c>gist.github.com</c> matches an item for <c>https://github.com</c>, while <c>ab.example.com</c> does not
/// match one for <c>b.example.com</c>, nor <c>example.com</c> one for <c>login.example.com</c>. An IP address or <c>localhost</c>
/// matches only itself, and a public suffix, which has no registrable domain, matches nothing. An item's address with no scheme
/// is <c>https</c>, and one with no port has its scheme's default.
/// </para>
/// <para>
/// An item's address in double quotes matches only the page's URL exactly, and one holding <c>*</c> matches nothing, since
/// keypaste honours no wildcard.
/// </para>
/// </remarks>
public sealed class LoginLookup
{
    private const string _moreAddresses = "KP2A_URL";

    private readonly string _url;

    private LoginLookup(string url, WebOrigin page, WebOrigin? otherSiteForm)
    {
        _url = url;
        Page = page;
        OtherSiteForm = otherSiteForm;
    }

    /// <summary>The page's origin.</summary>
    public WebOrigin Page { get; }

    /// <summary>Where the page's form posts when that is another registrable domain, which the person is told; otherwise null.</summary>
    public WebOrigin? OtherSiteForm { get; }

    /// <summary>
    /// Whether a fill here is asked about every time and never remembered: the page is <c>http</c>, or its form posts to another
    /// registrable domain.
    /// </summary>
    public bool AsksEveryTime => Page.Scheme == Uri.UriSchemeHttp || OtherSiteForm is not null;

    /// <summary>Reads the extension's request.</summary>
    /// <param name="url">The page's URL, which a browser writes with its host in A-labels.</param>
    /// <param name="submitUrl">Where the page's form posts, or null or empty when it names nowhere else.</param>
    /// <param name="httpAuthentication">Whether the request is for HTTP authentication rather than a page's form.</param>
    /// <param name="lookup">The request, or null when it gets no logins.</param>
    /// <param name="refusal">Why it gets none, or <see cref="LookupRefusal.None"/>.</param>
    /// <returns>Whether any item can match it.</returns>
    public static bool TryRead(
        string? url,
        string? submitUrl,
        bool httpAuthentication,
        [NotNullWhen(true)] out LoginLookup? lookup,
        out LookupRefusal refusal)
    {
        lookup = null;

        if (httpAuthentication)
        {
            refusal = LookupRefusal.HttpAuthentication;
            return false;
        }

        if (!WebOrigin.TryRead(url, out var page, out refusal))
        {
            return false;
        }

        if (page.Site is null)
        {
            refusal = LookupRefusal.PublicSuffix;
            return false;
        }

        WebOrigin? form = null;

        if (!string.IsNullOrEmpty(submitUrl) && !WebOrigin.TryRead(submitUrl, out form, out refusal))
        {
            return false;
        }

        lookup = new LoginLookup(url, page, form is null || form.Site == page.Site ? null : form);
        refusal = LookupRefusal.None;
        return true;
    }

    /// <summary>Whether one of an item's web addresses matches the page.</summary>
    /// <param name="address">The item's URL, or the value of one of its <c>KP2A_URL</c> fields.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public bool Matches(string? address)
    {
        var text = address?.Trim() ?? string.Empty;

        if (text is ['"', .. var exact, '"'])
        {
            return string.Equals(exact, _url, StringComparison.Ordinal);
        }

        if (text.Contains('*', StringComparison.Ordinal)
            || !WebOrigin.TryReadItem(text, out var item)
            || item.Scheme != Page.Scheme
            || item.Port != Page.Port)
        {
            return false;
        }

        if (item.IsExactHost || Page.IsExactHost)
        {
            return item.Host == Page.Host;
        }

        return item.Site is not null
            && item.Site == Page.Site
            && (item.Host == Page.Host || Page.Host.EndsWith("." + item.Host, StringComparison.Ordinal));
    }

    /// <summary>Whether an item matches the page by its URL or by a field whose name starts <c>KP2A_URL</c>, as KeePassXC reads more addresses.</summary>
    /// <param name="url">The item's URL.</param>
    /// <param name="fields">The item's custom fields, by name and value.</param>
    /// <returns><see langword="true"/> when one of them does.</returns>
    public bool MatchesItem(string? url, IEnumerable<KeyValuePair<string, string>> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        return Matches(url)
            || fields.Any(field => field.Key.StartsWith(_moreAddresses, StringComparison.Ordinal) && Matches(field.Value));
    }
}
