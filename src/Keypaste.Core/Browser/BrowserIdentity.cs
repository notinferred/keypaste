namespace Keypaste.Core.Browser;

/// <summary>
/// A KeePassXC-Browser build keypaste answers, named as its browser names the caller when it starts keypaste: the Chrome Web
/// Store's and Microsoft Edge Add-ons' extension origins and the Firefox add-on's ID, and no other (D-0548, D-0624).
/// </summary>
/// <param name="Caller">What the browser passes keypaste: a Chrome-family extension origin, or a Firefox add-on ID.</param>
/// <param name="Store">Where that build is published, as a person reads it.</param>
/// <remarks>
/// The browser starts the host only for an extension its manifest allows, but any local process can start keypaste naming one
/// of these, so an identity guards against a misconfigured manifest and is not proof of the caller (THREATS.md T-48).
/// </remarks>
public sealed record BrowserIdentity(string Caller, string Store)
{
    /// <summary>What keypaste says to a caller that is none of the three.</summary>
    public const string Only = "keypaste answers KeePassXC-Browser from the Chrome Web Store, Microsoft Edge Add-ons and Firefox Add-ons only";

    private const string _chromeOrigin = "chrome-extension://";

    /// <summary>KeePassXC-Browser from the Chrome Web Store.</summary>
    public static BrowserIdentity ChromeWebStore { get; } = new("chrome-extension://oboonakemofpalcgghocfoadofidjkkk/", "the Chrome Web Store");

    /// <summary>KeePassXC-Browser from Microsoft Edge Add-ons.</summary>
    public static BrowserIdentity EdgeAddOns { get; } = new("chrome-extension://pdffhmdngciaglkoonimfcmckehcpafo/", "Microsoft Edge Add-ons");

    /// <summary>KeePassXC-Browser from Firefox Add-ons.</summary>
    public static BrowserIdentity FirefoxAddOns { get; } = new("keepassxc-browser@keepassxc.org", "Firefox Add-ons");

    /// <summary>The three, in that order.</summary>
    public static IReadOnlyList<BrowserIdentity> All { get; } = [ChromeWebStore, EdgeAddOns, FirefoxAddOns];

    /// <summary>The extension's own ID: a Chrome-family origin's host, or the Firefox add-on ID.</summary>
    public string Id => Caller.StartsWith(_chromeOrigin, StringComparison.Ordinal) ? Caller[_chromeOrigin.Length..].TrimEnd('/') : Caller;

    /// <summary>How a confirmation names the build: <c>KeePassXC-Browser from the Chrome Web Store (oboonakemofpalcgghocfoadofidjkkk)</c>.</summary>
    public string Described => $"KeePassXC-Browser from {Store} ({Id})";

    /// <summary>The browser a person most likely runs this build in, which a pairing's suggested name starts with.</summary>
    public string Browser => ReferenceEquals(this, EdgeAddOns) ? "Edge" : ReferenceEquals(this, FirefoxAddOns) ? "Firefox" : "Chrome";

    /// <summary>The build a browser's caller names, compared exactly, or null for any other.</summary>
    /// <param name="caller">What the browser passed.</param>
    /// <returns>The build, or null.</returns>
    public static BrowserIdentity? Of(string? caller) =>
        All.FirstOrDefault(identity => string.Equals(identity.Caller, caller, StringComparison.Ordinal));
}
