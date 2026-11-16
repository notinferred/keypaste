using Keypaste.Core.Browser;

namespace Keypaste.Core.Recommendations;

/// <summary>What a reminder's link opens.</summary>
public enum NewCredentialPageKind
{
    /// <summary>The provider's own page for making a key or token, from <see cref="NewCredentialPages"/>' table.</summary>
    Provider,

    /// <summary>A login's site's <c>/.well-known/change-password</c>, which W3C's Well-Known URL for Changing Passwords defines.</summary>
    ChangePassword,

    /// <summary>The item's own URL.</summary>
    ItemUrl,
}

/// <summary>Where a reminder sends the person to make a new credential.</summary>
/// <param name="Address">An absolute <c>http</c> or <c>https</c> address.</param>
/// <param name="Kind">What it is.</param>
public sealed record NewCredentialPage(Uri Address, NewCredentialPageKind Kind);

/// <summary>Where to make a new credential for an item, worked out from its URL and kind alone, with no network request.</summary>
/// <remarks>
/// A login goes to its site's change-password address. Any other item whose host is one of the table's, or below one within the
/// same registrable domain, goes to that provider's page, and otherwise to its own URL. The table is small and conservative: a
/// provider whose page is uncertain is left out rather than guessed, and a host such as <c>amazon.com</c> that also serves
/// another product is matched only below the part that is the provider's.
/// </remarks>
public static class NewCredentialPages
{
    private static readonly (string Host, Uri Page)[] _providers =
    [
        ("github.com", new("https://github.com/settings/tokens")),
        ("gitlab.com", new("https://gitlab.com/-/user_settings/personal_access_tokens")),
        ("aws.amazon.com", new("https://console.aws.amazon.com/iam/home#/security_credentials")),
        ("platform.openai.com", new("https://platform.openai.com/api-keys")),
        ("api.openai.com", new("https://platform.openai.com/api-keys")),
        ("console.anthropic.com", new("https://console.anthropic.com/settings/keys")),
        ("api.anthropic.com", new("https://console.anthropic.com/settings/keys")),
        ("stripe.com", new("https://dashboard.stripe.com/apikeys")),
        ("cloudflare.com", new("https://dash.cloudflare.com/profile/api-tokens")),
    ];

    /// <summary>The page for an item.</summary>
    /// <param name="url">The item's URL field as stored.</param>
    /// <param name="kind">The item's kind.</param>
    /// <returns>The page, or null when the URL is not one keypaste opens.</returns>
    public static NewCredentialPage? For(string? url, ItemKind kind)
    {
        if (!WebAddress.TryOpenable(url, out var address))
        {
            return null;
        }

        if (!WebOrigin.TryReadItem(address.OriginalString, out var origin))
        {
            return new NewCredentialPage(address, NewCredentialPageKind.ItemUrl);
        }

        if (kind == ItemKind.Login)
        {
            return new NewCredentialPage(new UriBuilder(origin.Scheme, origin.Host.Trim('[', ']'), origin.Port, "/.well-known/change-password").Uri, NewCredentialPageKind.ChangePassword);
        }

        foreach (var (host, page) in _providers)
        {
            if (origin.Site is { } site
                && (origin.Host == host || origin.Host.EndsWith("." + host, StringComparison.Ordinal))
                && site == PublicSuffixList.RegistrableDomain(host))
            {
                return new NewCredentialPage(page, NewCredentialPageKind.Provider);
            }
        }

        return new NewCredentialPage(address, NewCredentialPageKind.ItemUrl);
    }
}
