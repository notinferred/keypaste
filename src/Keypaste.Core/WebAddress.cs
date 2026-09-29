using System.Diagnostics.CodeAnalysis;

namespace Keypaste.Core;

/// <summary>Which web addresses an entry's URL field opens in a browser, and as what (D-0378).</summary>
/// <remarks>
/// Only <c>http</c> and <c>https</c> open. An address with no scheme, such as <c>github.com/login</c>,
/// opens as <c>https://</c>, as KeePassXC opens it; any other scheme, such as <c>javascript:</c>,
/// <c>file:</c>, <c>data:</c> or KeePassXC's <c>cmd://</c>, never does. Neither does text that
/// <see cref="DisplayTextSanitizer"/> would draw differently, so the link a person reads is the
/// address that opens.
/// </remarks>
public static class WebAddress
{
    /// <summary>The address to open for an entry's URL field.</summary>
    /// <param name="url">The field as stored, never as drawn.</param>
    /// <param name="address">The absolute <c>http</c> or <c>https</c> address, or null.</param>
    /// <returns>Whether keypaste opens it.</returns>
    public static bool TryOpenable(string? url, [NotNullWhen(true)] out Uri? address)
    {
        address = null;
        var text = url?.Trim() ?? string.Empty;

        if (text.Length == 0 || DisplayTextSanitizer.Sanitize(text).WasAltered || text.Any(char.IsWhiteSpace))
        {
            return false;
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            if (NamesAScheme(text))
            {
                return false;
            }

            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || uri.Host.Length == 0)
        {
            return false;
        }

        address = uri;
        return true;
    }

    /// <summary>Whether text with no <c>://</c> starts with a scheme such as <c>mailto:</c>, rather than a host and port.</summary>
    private static bool NamesAScheme(string text)
    {
        var end = text.AsSpan().IndexOfAny('/', '?', '#');
        var authority = end < 0 ? text : text[..end];
        var colon = authority.IndexOf(':', StringComparison.Ordinal);

        return colon >= 0 && !authority[(colon + 1)..].All(char.IsAsciiDigit);
    }
}
