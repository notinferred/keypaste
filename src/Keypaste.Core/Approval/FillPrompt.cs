using Keypaste.Core.Browser;

namespace Keypaste.Core.Approval;

/// <summary>
/// What a person is shown before keypaste fills a login they picked in a page's list, on a site it was not filled on before or
/// where every fill is asked: never its password.
/// </summary>
/// <remarks>
/// The browser and the page are what the extension claims (THREATS.md T-48, T-58, T-59), so the prompt names the build and the
/// pairing and never says the person's browser asked.
/// </remarks>
public sealed record FillPrompt
{
    /// <summary>The build the browser named.</summary>
    public required BrowserIdentity Identity { get; init; }

    /// <summary>The pairing's name.</summary>
    public required string Pairing { get; init; }

    /// <summary>The page's origin, as <c>scheme://host:port</c>.</summary>
    public required string Origin { get; init; }

    /// <summary>The page's host, which the prompt emphasises.</summary>
    public required string Site { get; init; }

    /// <summary>Where the page's form posts when that is another site, or null.</summary>
    public string? FormOrigin { get; init; }

    /// <summary>Why this site is asked every time, such as its connection not being private, or empty.</summary>
    public string Warning { get; init; } = string.Empty;

    /// <summary>The item's title, sanitized.</summary>
    public required string Title { get; init; }

    /// <summary>The item's username, sanitized, or empty.</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>The vault holding the item, named while several vaults are open; otherwise empty.</summary>
    public string Vault { get; init; } = string.Empty;

    /// <summary>What the prompt says asked: <c>keypaste's extension from the Chrome Web Store (…)</c>.</summary>
    public string IdentityText => Identity.Described;

    /// <summary>The question: <c>Fill GitHub on github.com?</c>.</summary>
    public string Question => $"Fill {Title} on {Site}?";
}
