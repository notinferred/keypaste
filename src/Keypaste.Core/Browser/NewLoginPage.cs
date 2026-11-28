namespace Keypaste.Core.Browser;

/// <summary>What a new login for a page starts with, read from the address the browser gave for its tab and never from the page.</summary>
/// <param name="Website">The page's origin, with no path, query or fragment, or empty for no web page.</param>
/// <param name="Title">
/// The site the origin belongs to, such as <c>github.com</c> for <c>https://www.github.com</c>: its registrable domain, or the host
/// itself for an IP address, <c>localhost</c>, a single label or a public suffix; empty for no web page.
/// </param>
public sealed record NewLoginPage(string Website, string Title)
{
    /// <summary>No web page, so the new login starts empty.</summary>
    public static NewLoginPage None { get; } = new(string.Empty, string.Empty);

    /// <summary>Reads the page's address as the browser named it.</summary>
    /// <param name="url">The tab's address.</param>
    /// <returns>Its origin and site, or <see cref="None"/> for no web page.</returns>
    public static NewLoginPage Of(string? url)
    {
        if (!WebOrigin.TryRead(url, out var origin, out _))
        {
            return None;
        }

        var defaultPort = string.Equals(origin.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ? 443 : 80;
        var website = origin.Port == defaultPort ? $"{origin.Scheme}://{origin.Host}" : origin.ToString();
        return new NewLoginPage(website, origin.Site ?? origin.Host);
    }
}
