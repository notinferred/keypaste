namespace Keypaste.Core.Approval;

/// <summary>
/// One prompt in front of the person at a time, and the agents' requests that arrive meanwhile waiting their turn in the
/// order they came, at most <see cref="ApprovalLimits.MaximumWaiting"/> for each agent (F.80).
/// </summary>
/// <remarks>
/// A request that does not name an agent never waits: it takes the turn now or is refused as busy, as every request was
/// before agents could queue. A waiting request leaves the queue the moment it is withdrawn, so a lock, which withdraws
/// them all, empties it. A looping agent can hold no more than its bound, and the refusal cooldown still answers its retries
/// of a request a person refused (THREATS.md T-11).
/// </remarks>
internal sealed class PromptQueue
{
    private readonly int _perAsker;
    private readonly object _lock = new();
    private readonly LinkedList<Waiter> _waiting = new();
    private bool _taken;

    /// <param name="perAsker">How many of one agent's requests may wait behind the prompt on screen.</param>
    internal PromptQueue(int perAsker) => _perAsker = perAsker;

    /// <summary>How many requests are waiting now, behind the one on screen.</summary>
    internal int Waiting
    {
        get
        {
            lock (_lock)
            {
                return _waiting.Count;
            }
        }
    }

    /// <summary>Takes the turn now, or waits for it behind the requests that came first.</summary>
    /// <param name="asker">The agent asking, or null for a request that never waits.</param>
    /// <param name="cancellationToken">Withdraws the request, which then leaves the queue.</param>
    /// <returns>True with the turn, which <see cref="Release"/> gives back; false when it was refused as busy.</returns>
    /// <exception cref="OperationCanceledException">The request was withdrawn while it waited.</exception>
    internal async ValueTask<bool> TakeAsync(string? asker, CancellationToken cancellationToken)
    {
        LinkedListNode<Waiter> node;

        lock (_lock)
        {
            if (!_taken)
            {
                _taken = true;
                return true;
            }

            if (asker is null || _waiting.Count(waiter => string.Equals(waiter.Asker, asker, StringComparison.Ordinal)) >= _perAsker)
            {
                return false;
            }

            node = _waiting.AddLast(new Waiter(asker, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)));
        }

        using (cancellationToken.Register(() => node.Value.Turn.TrySetCanceled(cancellationToken)))
        {
            try
            {
                await node.Value.Turn.Task.ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                lock (_lock)
                {
                    if (node.List is not null)
                    {
                        _waiting.Remove(node);
                    }
                }

                throw;
            }
        }
    }

    /// <summary>Gives the turn to the request that has waited longest and is still waiting, or frees it.</summary>
    internal void Release()
    {
        lock (_lock)
        {
            while (_waiting.First is { } first)
            {
                _waiting.RemoveFirst();

                // One withdrawn as it was handed the turn has given it up already.
                if (first.Value.Turn.TrySetResult())
                {
                    return;
                }
            }

            _taken = false;
        }
    }

    private sealed record Waiter(string Asker, TaskCompletionSource Turn);
}
