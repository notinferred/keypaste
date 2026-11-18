using Keypaste.Core.Ipc;

namespace Keypaste.Core.Browser;

/// <summary>
/// What decides a browser's pairing, pairing check, list, release and show once the vault's owner has admitted them on a
/// browser's attachment to its live session; an owner given none refuses each.
/// </summary>
public interface IBrowserRequests
{
    /// <summary>The database hash the extension files this computer's pairing under, which a browser's attachment is told, or null while locked.</summary>
    /// <returns>The hash in lowercase hex, or null.</returns>
    string? DatabaseHash();

    /// <summary>Decides a pairing.</summary>
    /// <param name="request">The extension's identification key.</param>
    /// <param name="browser">The build the connection attached as.</param>
    /// <param name="connectionId">The connection it came on.</param>
    /// <param name="cancellationToken">Cancelled when the connection goes, or a lock or reload withdraws the request.</param>
    /// <returns>The pairing, or why there is none.</returns>
    ValueTask<BrowserReply> PairAsync(BrowserPairRequest request, BrowserIdentity browser, string connectionId, CancellationToken cancellationToken);

    /// <summary>Decides whether a pairing the extension holds is this browser's.</summary>
    /// <param name="request">The pairing and its key.</param>
    /// <param name="browser">The build the connection attached as.</param>
    /// <param name="connectionId">The connection it came on.</param>
    /// <param name="cancellationToken">Cancelled when the connection goes, or a lock or reload withdraws the request.</param>
    /// <returns>The pairing, or why it is not this browser's.</returns>
    ValueTask<BrowserReply> CheckAsync(BrowserCheckRequest request, BrowserIdentity browser, string connectionId, CancellationToken cancellationToken);

    /// <summary>Lists the names of a page's logins in every vault open here, never a password.</summary>
    /// <param name="request">The pairing, its key and the page.</param>
    /// <param name="browser">The build the connection attached as.</param>
    /// <param name="connectionId">The connection it came on.</param>
    /// <param name="cancellationToken">Cancelled when the connection goes, or a lock or reload withdraws the request.</param>
    /// <returns>The names, or why none.</returns>
    ValueTask<BrowserListReply> ListAsync(BrowserListRequest request, BrowserIdentity browser, string connectionId, CancellationToken cancellationToken);

    /// <summary>Decides whether the one login the person picked in a page's list is filled.</summary>
    /// <param name="request">The pairing, its key, the page and the item.</param>
    /// <param name="browser">The build the connection attached as.</param>
    /// <param name="connectionId">The connection it came on.</param>
    /// <param name="cancellationToken">Cancelled when the connection goes, or a lock or reload withdraws the request.</param>
    /// <returns>The login, or why none.</returns>
    ValueTask<BrowserFillReply> ReleaseAsync(BrowserReleaseRequest request, BrowserIdentity browser, string connectionId, CancellationToken cancellationToken);

    /// <summary>Brings keypaste forward for what the person pressed in a page's list, releasing nothing.</summary>
    /// <param name="request">What to show.</param>
    /// <param name="browser">The build the connection attached as.</param>
    /// <param name="connectionId">The connection it came on.</param>
    /// <param name="cancellationToken">Cancelled when the connection goes.</param>
    /// <returns>Whether keypaste was brought forward.</returns>
    ValueTask<BrowserReply> ShowAsync(BrowserShowRequest request, BrowserIdentity browser, string connectionId, CancellationToken cancellationToken);
}
