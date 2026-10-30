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

/// <summary>Asks the vault's owner to pair the browser attached on this connection (D-0627).</summary>
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

/// <summary>Asks the vault's owner whether a pairing the extension holds is this browser's (D-0627).</summary>
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

/// <summary>Asks the vault's owner for the logins of the page the extension names, under the one pairing the relay checked (D-0627).</summary>
/// <param name="Pairing">The pairing's id.</param>
/// <param name="IdentificationKey">The pairing's identification key, in base64.</param>
/// <param name="Url">The page's URL as the extension reports it.</param>
public sealed record BrowserFillRequest(string Pairing, string IdentificationKey, string Url)
{
    /// <summary>The URL the page's form posts to, as the extension reports it, or null when it named none.</summary>
    public string? SubmitUrl { get; init; }

    /// <summary>The vault this connection attached to.</summary>
    public string Vault { get; init; } = string.Empty;

    /// <summary>The session this connection attached to.</summary>
    public string Session { get; init; } = string.Empty;

    /// <summary>A description with the key left out.</summary>
    /// <returns>The pairing and the page.</returns>
    public override string ToString() => $"BrowserFillRequest {{ Pairing = {Pairing}, Url = {Url}, SubmitUrl = {SubmitUrl} }}";
}

/// <summary>Asks the vault's owner to lock now, as <c>keypaste lock</c> does, for the browser attached on this connection (D-0627).</summary>
public sealed record BrowserLockRequest
{
    /// <summary>The vault this connection attached to.</summary>
    public string Vault { get; init; } = string.Empty;

    /// <summary>The session this connection attached to.</summary>
    public string Session { get; init; } = string.Empty;
}

/// <summary>The owner's answer to a browser's pairing, pairing check or lock.</summary>
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

/// <summary>The owner's answer to a browser's fill: the logins released, or why none were.</summary>
/// <param name="Outcome">What became of the request.</param>
/// <param name="Reason">keypaste's own words for a refusal, or empty.</param>
public sealed record BrowserFillReply(BrowserOutcome Outcome, string Reason)
{
    /// <summary>The logins released; none unless <see cref="Outcome"/> is <see cref="BrowserOutcome.Done"/>.</summary>
    public IReadOnlyList<BrowserLogin> Logins { get; init; } = [];

    /// <summary>A description with the logins left out.</summary>
    /// <returns>The outcome and how many logins it carries.</returns>
    public override string ToString() => $"BrowserFillReply {{ Outcome = {Outcome}, Logins = {Logins.Count} }}";
}
