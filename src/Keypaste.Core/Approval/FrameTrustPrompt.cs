using Keypaste.Core.Browser;

namespace Keypaste.Core.Approval;

/// <summary>
/// What a person is shown before keypaste offers a paired browser's logins in a sign-in frame of another site: the frame's host,
/// the host of the page it is in, and the build and pairing that asked.
/// </summary>
/// <remarks>
/// The hosts are the browser's, as the extension reports them, never the page's own words; trusting the frame offers it only
/// its own site's logins, never the page's (THREATS.md T-62). It has no member for a value.
/// </remarks>
public sealed record FrameTrustPrompt
{
    /// <summary>The build the browser named.</summary>
    public required BrowserIdentity Identity { get; init; }

    /// <summary>The pairing that asked.</summary>
    public required string Pairing { get; init; }

    /// <summary>The host of the tab's page, as <c>www.icloud.com</c>.</summary>
    public required string Top { get; init; }

    /// <summary>The frame's host, as <c>idmsa.apple.com</c>.</summary>
    public required string Frame { get; init; }

    /// <summary>The question, naming both hosts.</summary>
    public string Question => $"Trust sign-in from {Frame} on {Top}?";

    /// <summary>What trusting it does, in keypaste's words.</summary>
    public string Explanation =>
        $"{Top} shows a sign-in form from {Frame}. If you trust it, keypaste offers {Frame}'s logins there, never {Top}'s, in this browser until you remove it in Settings › Browser fill.";
}
