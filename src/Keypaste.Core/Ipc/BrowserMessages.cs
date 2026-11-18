namespace Keypaste.Core.Ipc;

/// <summary>What became of a browser's request at the vault's owner.</summary>
public enum BrowserOutcome
{
    /// <summary>Done as asked.</summary>
    Done = 0,

    /// <summary>Nothing holds the vault unlocked for this connection: it is locked, or the request belongs to an unlock that has ended.</summary>
    Locked = 1,

    /// <summary>Refused, by the owner or by the person it asked.</summary>
    Refused = 2,

    /// <summary>The pairing named is not one this browser holds.</summary>
    NotPaired = 3,
}

/// <summary>Asks the vault's owner to pair the browser attached on this connection.</summary>
/// <param name="IdentificationKey">The public identification key the extension made for the pairing, in base64.</param>
/// <remarks>The extension presents that key again to prove the pairing, so it is a bearer key and is not printed.</remarks>
public sealed record BrowserPairRequest(string IdentificationKey)
{
    /// <summary>The vault this connection attached to.</summary>
    public string Vault { get; init; } = string.Empty;

    /// <summary>The session this connection attached to.</summary>
    public string Session { get; init; } = string.Empty;

    /// <summary>A description with the key left out.</summary>
    /// <returns>The vault and session.</returns>
    public override string ToString() => $"BrowserPairRequest {{ Vault = {Vault}, Session = {Session} }}";
}

/// <summary>Asks the vault's owner whether a pairing the extension holds is this browser's.</summary>
/// <param name="Pairing">The pairing's id, as the extension keeps it.</param>
/// <param name="IdentificationKey">The pairing's identification key, in base64.</param>
public sealed record BrowserCheckRequest(string Pairing, string IdentificationKey)
{
    /// <summary>The vault this connection attached to.</summary>
    public string Vault { get; init; } = string.Empty;

    /// <summary>The session this connection attached to.</summary>
    public string Session { get; init; } = string.Empty;

    /// <summary>A description with the key left out.</summary>
    /// <returns>The pairing, vault and session.</returns>
    public override string ToString() => $"BrowserCheckRequest {{ Pairing = {Pairing}, Vault = {Vault}, Session = {Session} }}";
}

/// <summary>
/// Asks the vault's owner for the names of a page's logins in every open vault, under the one pairing the relay checked: never a
/// password, which only a <see cref="BrowserReleaseRequest"/> for one picked login can release.
/// </summary>
/// <param name="Pairing">The pairing's id.</param>
/// <param name="IdentificationKey">The pairing's identification key, in base64.</param>
/// <param name="Url">The tab's URL as the browser reports it to the extension.</param>
public sealed record BrowserListRequest(string Pairing, string IdentificationKey, string Url)
{
    /// <summary>The URL the page's form posts to, as the extension reports it, or null when it named none.</summary>
    public string? SubmitUrl { get; init; }

    /// <summary>The vault this connection attached to.</summary>
    public string Vault { get; init; } = string.Empty;

    /// <summary>The session this connection attached to.</summary>
    public string Session { get; init; } = string.Empty;

    /// <summary>A description with the key left out.</summary>
    /// <returns>The pairing and the page.</returns>
    public override string ToString() => $"BrowserListRequest {{ Pairing = {Pairing}, Url = {Url}, SubmitUrl = {SubmitUrl} }}";
}

/// <summary>One login a page's list shows: its names, and nothing that fills.</summary>
/// <param name="Uuid">The item's UUID, which a pick names.</param>
/// <param name="Title">Its title, sanitized.</param>
/// <param name="Username">Its username as stored, sanitized, or empty.</param>
public sealed record BrowserLoginName(string Uuid, string Title, string Username)
{
    /// <summary>The vault holding it, named while several vaults are open; otherwise empty.</summary>
    public string Vault { get; init; } = string.Empty;
}

/// <summary>The owner's answer to a list: the matching logins' names in every unlocked vault, and the open vaults that are locked.</summary>
/// <param name="Outcome">What became of the request.</param>
/// <param name="Reason">keypaste's own words for a refusal, or empty.</param>
public sealed record BrowserListReply(BrowserOutcome Outcome, string Reason)
{
    /// <summary>The page's site, which the list names, or empty when its URL gets no logins.</summary>
    public string Site { get; init; } = string.Empty;

    /// <summary>The matching logins, each vault's in its order; none unless <see cref="Outcome"/> is <see cref="BrowserOutcome.Done"/>.</summary>
    public IReadOnlyList<BrowserLoginName> Names { get; init; } = [];

    /// <summary>The names of the vaults open here and locked, whose logins are not listed.</summary>
    public IReadOnlyList<string> LockedVaults { get; init; } = [];

    /// <summary>Why a fill here is asked in keypaste every time, in words the list shows, or empty.</summary>
    public string Warning { get; init; } = string.Empty;
}

/// <summary>Asks the vault's owner to fill the one login the person picked in a page's list, under the one pairing the relay checked.</summary>
/// <param name="Pairing">The pairing's id.</param>
/// <param name="IdentificationKey">The pairing's identification key, in base64.</param>
/// <param name="Url">The tab's URL as the browser reports it to the extension.</param>
/// <param name="Uuid">The picked item's UUID.</param>
public sealed record BrowserReleaseRequest(string Pairing, string IdentificationKey, string Url, string Uuid)
{
    /// <summary>The URL the page's form posts to, as the extension reports it, or null when it named none.</summary>
    public string? SubmitUrl { get; init; }

    /// <summary>The vault this connection attached to.</summary>
    public string Vault { get; init; } = string.Empty;

    /// <summary>The session this connection attached to.</summary>
    public string Session { get; init; } = string.Empty;

    /// <summary>A description with the key left out.</summary>
    /// <returns>The pairing, the page and the item.</returns>
    public override string ToString() => $"BrowserReleaseRequest {{ Pairing = {Pairing}, Url = {Url}, SubmitUrl = {SubmitUrl}, Uuid = {Uuid} }}";
}

/// <summary>What a person asked keypaste to show from a page's list.</summary>
public enum BrowserShow
{
    /// <summary>keypaste's window.</summary>
    Open = 0,

    /// <summary>A new login.</summary>
    NewLogin = 1,

    /// <summary>A locked vault's unlock.</summary>
    Unlock = 2,
}

/// <summary>Asks the vault's owner to bring keypaste forward for what the person pressed in a page's list; nothing is released.</summary>
/// <param name="What">What they pressed.</param>
public sealed record BrowserShowRequest(BrowserShow What)
{
    /// <summary>The locked vault to unlock, or the vault of the logins the list showed for a new login, as the list named it, or empty.</summary>
    public string VaultName { get; init; } = string.Empty;

    /// <summary>The page a new login is for, as the browser named it, or empty.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>The vault this connection attached to.</summary>
    public string Vault { get; init; } = string.Empty;

    /// <summary>The session this connection attached to.</summary>
    public string Session { get; init; } = string.Empty;
}

/// <summary>Asks the vault's owner to lock now, as <c>keypaste lock</c> does, for the browser attached on this connection.</summary>
public sealed record BrowserLockRequest
{
    /// <summary>The vault this connection attached to.</summary>
    public string Vault { get; init; } = string.Empty;

    /// <summary>The session this connection attached to.</summary>
    public string Session { get; init; } = string.Empty;
}

/// <summary>The owner's answer to a browser's pairing, pairing check, lock or show.</summary>
/// <param name="Outcome">What became of the request.</param>
/// <param name="Reason">keypaste's own words for a refusal, or empty.</param>
public sealed record BrowserReply(BrowserOutcome Outcome, string Reason)
{
    /// <summary>The pairing a pairing or a check answered for, or null.</summary>
    public string? Pairing { get; init; }
}

/// <summary>One login released to a browser: an item's username and password, its title and its UUID, and nothing else of it.</summary>
/// <param name="Login">The item's username.</param>
/// <param name="Password">The item's password.</param>
/// <param name="Name">The item's title.</param>
/// <param name="Uuid">The item's UUID, as KeePassXC writes one.</param>
/// <remarks>A record prints every member, so the password and username are left out of its description.</remarks>
public sealed record BrowserLogin(string Login, string Password, string Name, string Uuid)
{
    /// <summary>A description with the username and password left out.</summary>
    /// <returns>The item's UUID.</returns>
    public override string ToString() => $"BrowserLogin {{ Uuid = {Uuid} }}";
}

/// <summary>The owner's answer to a browser's release: the one login released, or why none was.</summary>
/// <param name="Outcome">What became of the request.</param>
/// <param name="Reason">keypaste's own words for a refusal, or empty.</param>
public sealed record BrowserFillReply(BrowserOutcome Outcome, string Reason)
{
    /// <summary>The login released, at most one; none unless <see cref="Outcome"/> is <see cref="BrowserOutcome.Done"/>.</summary>
    public IReadOnlyList<BrowserLogin> Logins { get; init; } = [];

    /// <summary>A description with the logins left out.</summary>
    /// <returns>The outcome and how many logins it carries.</returns>
    public override string ToString() => $"BrowserFillReply {{ Outcome = {Outcome}, Logins = {Logins.Count} }}";
}
