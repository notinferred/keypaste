using Keypaste.Core.Ipc;

namespace Keypaste.Core.Browser;

/// <summary>
/// What decides a browser's pairing, pairing check and fill once the vault's owner has admitted them on a browser's attachment
/// to its live session; an owner given none refuses each (D-0627).
/// </summary>
public interface IBrowserRequests
{
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

    /// <summary>Decides which logins of a page are released.</summary>
    /// <param name="request">The pairing, its key and the page.</param>
    /// <param name="browser">The build the connection attached as.</param>
    /// <param name="connectionId">The connection it came on.</param>
    /// <param name="cancellationToken">Cancelled when the connection goes, or a lock or reload withdraws the request.</param>
    /// <returns>The logins, or why none.</returns>
    ValueTask<BrowserFillReply> FillAsync(BrowserFillRequest request, BrowserIdentity browser, string connectionId, CancellationToken cancellationToken);
}
