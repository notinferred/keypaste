namespace Keypaste.Core.Approval;

/// <summary>
/// Somewhere a human can be shown a request and answer it.
/// </summary>
/// <remarks>
/// <para>
/// One method, because a channel has one job. A terminal prompt, a native OS dialog, and — from
/// Stage 4.3 — the desktop app's Agent Activity screen are all implementations of this, which is
/// what stops the approval flow being rebuilt when the GUI arrives.
/// </para>
/// <para>
/// <b>The contract a channel has to keep.</b> It must honour its cancellation token by
/// withdrawing whatever it put in front of the human — a
/// dialog left on screen for a request nobody is waiting for is how somebody approves something
/// into the void. It must never return <see cref="ApprovalAnswer.Approved"/> for anything except an
/// explicit yes; a closed window, an ignored prompt, a stray keystroke and an unreadable answer are
/// all denials. And it must not enforce its own deadline as though it were the deadline:
/// <see cref="ApprovalGate"/> owns the window, so that a channel which forgets still cannot leave a
/// request open forever.
/// </para>
/// </remarks>
public interface IApprovalChannel
{
    /// <summary>Shows a request to a human and waits for their answer.</summary>
    /// <param name="prompt">What to show. Already sanitized and capped; render it as inert text.</param>
    /// <param name="cancellationToken">Cancelled when the answer is no longer wanted.</param>
    /// <returns>What the human said, or why they were not asked.</returns>
    ValueTask<ApprovalAnswer> AskAsync(ApprovalPrompt prompt, CancellationToken cancellationToken);
}
