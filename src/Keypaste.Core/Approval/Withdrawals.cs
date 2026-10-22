namespace Keypaste.Core.Approval;

/// <summary>
/// The tokens whose cancellation withdraws the request the calling flow is answering, kept for
/// whatever is about to put that request in front of a person (D-0396).
/// </summary>
/// <remarks>
/// <para>
/// A token is marked the moment its withdrawal is requested, while a token linked below it is
/// cancelled only as its callbacks run: on the thread pool after <c>CancelAsync</c>, or on the
/// cancelling thread after <c>Cancel</c>. A bridge's hang-up and an idle lock both cancel off the UI
/// thread, so each layer that links a request's token from one of theirs adds that one here, and a
/// prompt not yet drawn reads them all.
/// </para>
/// <para>
/// The owner's own, which a lock or a reload cancels, are also kept apart, because a request the owner
/// withdrew went unanswered through nobody's choice and holds nothing back (THREATS.md T-11).
/// </para>
/// </remarks>
public static class Withdrawals
{
    private static readonly AsyncLocal<CancellationToken[]?> _current = new();
    private static readonly AsyncLocal<CancellationToken[]?> _owners = new();

    /// <summary>The calling flow's tokens, in the order they were added.</summary>
    public static IReadOnlyList<CancellationToken> Current => _current.Value ?? [];

    /// <summary>Whether the owner, by a lock or a reload, has withdrawn the request the calling flow is answering.</summary>
    public static bool ByOwner => _owners.Value is { } owners && Array.Exists(owners, token => token.IsCancellationRequested);

    /// <summary>Adds a token to the calling flow's, which every flow it goes on to start inherits.</summary>
    /// <param name="token">A token the request's own is linked below.</param>
    public static void Add(CancellationToken token) => _current.Value = [.. Current, token];

    /// <summary>Adds the owner's own token, as <see cref="Add"/> does, so its cancellation reads as the owner's withdrawal.</summary>
    /// <param name="token">The lifetime's wait, which a lock or a reload cancels.</param>
    public static void AddOwner(CancellationToken token)
    {
        Add(token);
        _owners.Value = _owners.Value is { } owners ? [.. owners, token] : [token];
    }
}
