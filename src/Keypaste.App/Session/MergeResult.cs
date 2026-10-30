using Keypaste.Core;

namespace Keypaste.App.Session;

/// <summary>What became of a merge of another program's save with the open vault, for the notice (D-0542).</summary>
/// <param name="Outcome">What became of it.</param>
/// <param name="Reason">Why there is no merged copy, as Core says it; empty when there is one.</param>
/// <param name="Report">What the merge did, by name and never by value, once it merged.</param>
/// <param name="Carried">The items the open vault had changed that no save had written, which a saved merge wrote; names only.</param>
/// <param name="Recorded">Whether the owner's audit line of a saved merge was written.</param>
/// <param name="At">When it finished.</param>
internal sealed record MergeResult(
    VaultMergeSaveOutcome Outcome,
    string Reason,
    VaultMergeReport? Report,
    IReadOnlyList<EntryName> Carried,
    bool Recorded,
    DateTimeOffset At)
{
    /// <summary>Whether the merged copy is in place, so agents are served from it.</summary>
    internal bool Merged => Outcome is VaultMergeSaveOutcome.Saved or VaultMergeSaveOutcome.Updated;

    /// <summary>Whether the merge was refused, leaving the open vault and its file as they were and agents refused until a reload.</summary>
    internal bool Refused => Outcome is not (VaultMergeSaveOutcome.Saved or VaultMergeSaveOutcome.Updated or VaultMergeSaveOutcome.Unchanged or VaultMergeSaveOutcome.Locked);
}
