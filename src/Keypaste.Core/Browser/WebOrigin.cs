using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Keypaste.Core.Browser;

/// <summary>A page's, a form's or an item's origin as browser fill compares it: its scheme, its host and its port (D-0617, D-0618).</summary>
/// <remarks>
/// Only an absolute <c>http</c> or <c>https</c> URL has one. Its host is ASCII, in lowercase: a domain name in A-labels, an IPv4
/// address in dotted decimal, a bracketed IPv6 address or <c>localhost</c>. A URL naming a user before its host, a host that is
/// not ASCII or a port outside 1 to 65535 has none, and neither does a domain name with an empty label or ending in a number.
/// </remarks>
public sealed record WebOrigin
{
    private static readonly SearchValues<char> _authorityEnds = SearchValues.Create("/?#\\");

    private WebOrigin(string scheme, string host, int port, bool isExactHost)
    {
        Scheme = scheme;
        Host = host;
        Port = port;
        IsExactHost = isExactHost;
        Site = isExactHost ? host : PublicSuffixList.RegistrableDomain(host);
    }

    /// <summary><c>http</c> or <c>https</c>.</summary>
    public string Scheme { get; }

    /// <summary>The host, in lowercase ASCII.</summary>
    public string Host { get; }

    /// <summary>The port, the scheme's default when the URL names none.</summary>
    public int Port { get; }

    /// <summary>
    /// The site the origin belongs to: a domain name's registrable domain, or an IP address or <c>localhost</c> itself; null for
    /// a public suffix, which has none.
    /// </summary>
    public string? Site { get; }

    /// <summary>Whether the host is an IP address or <c>localhost</c>, which matches only itself.</summary>
    internal bool IsExactHost { get; }

    /// <summary>The origin as <c>scheme://host:port</c>, with no path, query or fragment.</summary>
    /// <returns>The origin.</returns>
    public override string ToString() => $"{Scheme}://{Host}:{Port.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Reads a URL the browser sent, which names its scheme.</summary>
    internal static bool TryRead([NotNullWhen(true)] string? url, [NotNullWhen(true)] out WebOrigin? origin, out LookupRefusal refusal)
    {
        origin = null;
        refusal = LookupRefusal.NotAWebPage;

        if (string.IsNullOrEmpty(url) || url.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            return false;
        }

        var separator = url.IndexOf("://", StringComparison.Ordinal);

        if (separator < 0)
        {
            return false;
        }

        var scheme = url[..separator];
        int defaultPort;

        if (scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            (scheme, defaultPort) = (Uri.UriSchemeHttps, 443);
        }
        else if (scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            (scheme, defaultPort) = (Uri.UriSchemeHttp, 80);
        }
        else
        {
            return false;
        }

        var rest = url[(separator + 3)..];
        var end = rest.AsSpan().IndexOfAny(_authorityEnds);
        var authority = end < 0 ? rest : rest[..end];

        if (authority.Contains('@', StringComparison.Ordinal))
        {
            refusal = LookupRefusal.UserInfo;
            return false;
        }

        if (!Ascii.IsValid(authority))
        {
            refusal = LookupRefusal.NotAscii;
            return false;
        }

        if (!TrySplit(authority, out var host, out var isExactHost, out var portText) || !TryReadPort(portText, defaultPort, out var port))
        {
            return false;
        }

        origin = new WebOrigin(scheme, host, port, isExactHost);
        refusal = LookupRefusal.None;
        return true;
    }

    /// <summary>Reads an item's web address, in which a missing scheme means <c>https</c>, as the item pane opens it (D-0378).</summary>
    internal static bool TryReadItem(string address, [NotNullWhen(true)] out WebOrigin? origin)
    {
        origin = null;

        if (!address.Contains("://", StringComparison.Ordinal))
        {
            if (NamesAScheme(address))
            {
                return false;
            }

            address = "https://" + address;
        }

        return TryRead(address, out origin, out _);
    }

    /// <summary>Whether text with no <c>://</c> starts with a scheme such as <c>mailto:</c>, rather than a host and port.</summary>
    private static bool NamesAScheme(string text)
    {
        var end = text.AsSpan().IndexOfAny('/', '?', '#');
        var authority = end < 0 ? text : text[..end];
        var colon = authority.IndexOf(':', StringComparison.Ordinal);

        return !authority.StartsWith('[') && colon >= 0 && !authority[(colon + 1)..].All(char.IsAsciiDigit);
    }

    private static bool TrySplit(string authority, out string host, out bool isExactHost, out string? portText)
    {
        isExactHost = false;
        portText = null;

        if (authority.StartsWith('['))
        {
            var close = authority.IndexOf(']', StringComparison.Ordinal);
            var after = close < 0 ? string.Empty : authority[(close + 1)..];
            host = close < 0 ? string.Empty : authority[..(close + 1)];

            if (after.Length > 0)
            {
                if (after[0] != ':')
                {
                    return false;
                }

                portText = after[1..];
            }

            isExactHost = true;
            return TryCanonicalIPv6(host, out host);
        }

        var colon = authority.IndexOf(':', StringComparison.Ordinal);
        host = (colon < 0 ? authority : authority[..colon]).ToLowerInvariant();
        portText = colon < 0 ? null : authority[(colon + 1)..];

        var labels = host.Split('.');

        if (Array.Exists(labels, label => label.Length == 0)
            || !host.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '-' or '_' or '.'))
        {
            return false;
        }

        if (host == "localhost")
        {
            isExactHost = true;
            return true;
        }

        if (labels[^1].All(char.IsAsciiDigit))
        {
            isExactHost = true;
            return IsDottedDecimal(labels);
        }

        return true;
    }

    /// <summary>An IPv4 address as a browser writes one: four numbers up to 255, none with a leading zero.</summary>
    private static bool IsDottedDecimal(string[] labels) =>
        labels.Length == 4
        && labels.All(label => label.Length <= 3
            && label.All(char.IsAsciiDigit)
            && (label.Length == 1 || label[0] != '0')
            && int.Parse(label, CultureInfo.InvariantCulture) <= 255);

    /// <summary>A bracketed IPv6 address in the form <see cref="Uri"/> writes it, the longest run of zeros compressed.</summary>
    private static bool TryCanonicalIPv6(string bracketed, out string canonical)
    {
        canonical = string.Empty;

        if (bracketed.Length < 3
            || bracketed.Contains('%', StringComparison.Ordinal)
            || !Uri.TryCreate($"http://{bracketed}/", UriKind.Absolute, out var uri)
            || uri.HostNameType != UriHostNameType.IPv6)
        {
            return false;
        }

        canonical = uri.Host;
        return true;
    }

    private static bool TryReadPort(string? portText, int defaultPort, out int port)
    {
        port = defaultPort;

        if (portText is null)
        {
            return true;
        }

        return portText.Length is > 0 and <= 5
            && portText.All(char.IsAsciiDigit)
            && int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port)
            && port is > 0 and <= 65535;
    }
}
