namespace Keypaste.Core.Ownership;

/// <summary>One release an owner committed: which entry, to whom, how and when. Never a value.</summary>
/// <param name="Entry">The entry, as <see cref="Approval.ApprovalPrompt.Shown"/> writes it.</param>
/// <param name="Client">Who received it, as a screen names them.</param>
/// <param name="How">How it left: an audit method's word, or <c>run</c>, <c>run --session</c> or <c>token</c>.</param>
/// <param name="At">When it was committed.</param>
public sealed record ReleaseSeen(string Entry, string Client, string How, DateTimeOffset At)
{
    /// <summary>The UUID, as hex, of the one item released, or null where the release names none.</summary>
    public string? Item { get; init; }
}

/// <summary>
/// The releases one unlocked lifetime committed, newest first, held in memory only (D-0361).
/// </summary>
/// <remarks>
/// What a screen lists of this lifetime's releases without reading the audit log back. It ends with
/// the lifetime.
/// </remarks>
public sealed class ReleaseLedger
{
    /// <summary>The most rows kept; the oldest is dropped first.</summary>
    public const int MaximumRows = 4096;

    private readonly Lock _gate = new();
    private readonly LinkedList<ReleaseSeen> _rows = new();

    /// <summary>Records one release of each entry.</summary>
    /// <param name="entries">The entries released.</param>
    /// <param name="client">Who received them.</param>
    /// <param name="how">How they left.</param>
    /// <param name="at">When.</param>
    /// <param name="item">The UUID of the one item released, which tells it from another of its name, or null.</param>
    public void Record(IEnumerable<string> entries, string client, string how, DateTimeOffset at, string? item = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(how);

        lock (_gate)
        {
            foreach (var entry in entries)
            {
                _rows.AddFirst(new ReleaseSeen(entry, client, how, at) { Item = item });

                if (_rows.Count > MaximumRows)
                {
                    _rows.RemoveLast();
                }
            }
        }
    }

    /// <summary>Every release kept, newest first.</summary>
    /// <returns>A copy.</returns>
    public IReadOnlyList<ReleaseSeen> Recent()
    {
        lock (_gate)
        {
            return [.. _rows];
        }
    }
}
