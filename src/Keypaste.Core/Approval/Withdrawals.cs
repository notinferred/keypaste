namespace Keypaste.Core.Approval;

/// <summary>
/// The tokens whose cancellation withdraws the request the calling flow is answering, kept for
/// whatever is about to put that request in front of a person (D-0396).
/// </summary>
/// <remarks>
/// A token is marked the moment its withdrawal is requested, while a token linked below it is
/// cancelled only as its callbacks run: on the thread pool after <c>CancelAsync</c>, or on the
/// cancelling thread after <c>Cancel</c>. A bridge's hang-up and an idle lock both cancel off the UI
/// thread, so each layer that links a request's token from one of theirs adds that one here, and a
/// prompt not yet drawn reads them all.
/// </remarks>
public static class Withdrawals
{
    private static readonly AsyncLocal<CancellationToken[]?> _current = new();

    /// <summary>The calling flow's tokens, in the order they were added.</summary>
    public static IReadOnlyList<CancellationToken> Current => _current.Value ?? [];

    /// <summary>Adds a token to the calling flow's, which every flow it goes on to start inherits.</summary>
    /// <param name="token">A token the request's own is linked below.</param>
    public static void Add(CancellationToken token) => _current.Value = [.. Current, token];
}
