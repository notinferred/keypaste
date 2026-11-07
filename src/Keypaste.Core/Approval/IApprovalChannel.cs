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
/// into the void. It must never return <see cref="ApprovalAnswer.ApprovedOnce"/> for anything except that explicit
/// choice, nor <see cref="ApprovalAnswer.Always"/> when the prompt offered no Always allow; a closed window, an ignored prompt, a stray keystroke and an unreadable answer are
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

    /// <summary>Shows a request more than one item answers to and waits for the person to pick one and answer.</summary>
    /// <param name="prompt">What to show, its <see cref="ApprovalPrompt.Candidates"/> the items. Already sanitized; render it as inert text.</param>
    /// <param name="cancellationToken">Cancelled when the answer is no longer wanted.</param>
    /// <returns>What the person said and which candidate they picked, or why they were not asked.</returns>
    /// <remarks>
    /// No candidate is picked until the person picks one, and no allow can be pressed before that. A channel that cannot
    /// show a picker has asked nobody, which is a no.
    /// </remarks>
    ValueTask<ItemPick> AskToPickAsync(ApprovalPrompt prompt, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ItemPick.NoChannel);

    /// <summary>Shows a request for a project's env set to a human and waits for their answer.</summary>
    /// <param name="prompt">What to show. Already sanitized; render it as inert text.</param>
    /// <param name="cancellationToken">Cancelled when the answer is no longer wanted.</param>
    /// <returns>What the human said, or why they were not asked.</returns>
    /// <remarks>A channel that cannot show this has asked nobody, which is a no.</remarks>
    ValueTask<ApprovalAnswer> AskAsync(EnvReleasePrompt prompt, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ApprovalAnswer.NoChannel);

    /// <summary>Shows an agent's request to run a command with secrets to a human and waits for their answer.</summary>
    /// <param name="prompt">What to show. Already checked and sanitized; render it as inert text.</param>
    /// <param name="cancellationToken">Cancelled when the answer is no longer wanted.</param>
    /// <returns>What the human said, or why they were not asked.</returns>
    /// <remarks>A channel that cannot show this has asked nobody, which is a no.</remarks>
    ValueTask<ApprovalAnswer> AskAsync(RunPrompt prompt, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ApprovalAnswer.NoChannel);

    /// <summary>Asks a human to confirm a change that gives an agent more, and waits for their answer.</summary>
    /// <param name="prompt">What to show. Already sanitized; render it as inert text.</param>
    /// <param name="cancellationToken">Cancelled when the answer is no longer wanted.</param>
    /// <returns>What the human said, or why they were not asked.</returns>
    /// <remarks>A channel that cannot show this has asked nobody, which is a no: nothing widens.</remarks>
    ValueTask<ApprovalAnswer> AskAsync(AccessChangePrompt prompt, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ApprovalAnswer.NoChannel);

    /// <summary>Asks a human to allow a terminal's edit to the vault, and waits for their answer.</summary>
    /// <param name="prompt">What to show. Already sanitized; render it as inert text.</param>
    /// <param name="cancellationToken">Cancelled when the answer is no longer wanted.</param>
    /// <returns>What the human said, or why they were not asked.</returns>
    /// <remarks>A channel that cannot show this has asked nobody, which is a no: nothing is written.</remarks>
    ValueTask<ApprovalAnswer> AskAsync(TerminalEditPrompt prompt, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ApprovalAnswer.NoChannel);

    /// <summary>Asks a human, who armed pairing, to pair a browser under a name they may change, and waits for their answer.</summary>
    /// <param name="prompt">What to show. Render it as inert text.</param>
    /// <param name="cancellationToken">Cancelled when the answer is no longer wanted.</param>
    /// <returns>What the human said, with the name they gave it, or why they were not asked.</returns>
    /// <remarks>A channel that cannot show this has asked nobody, which is a no: nothing is paired.</remarks>
    ValueTask<PairingAnswer> AskAsync(PairingPrompt prompt, CancellationToken cancellationToken) =>
        ValueTask.FromResult(PairingAnswer.NoChannel);

    /// <summary>Tells a human, where they chose Always allow, that the change it asked for did not take effect, and why.</summary>
    /// <param name="notice">What to say. Already sanitized; render it as inert text.</param>
    /// <remarks>It asks nothing and must not wait for anything. A channel that cannot show it says nothing.</remarks>
    void Tell(AlwaysNotice notice)
    {
    }

    /// <summary>Tells a human that a browser asked to pair while pairing was not armed, and how to pair one.</summary>
    /// <param name="notice">What asked.</param>
    /// <remarks>It asks nothing and must not wait for anything. A channel that cannot show it says nothing.</remarks>
    void Tell(PairingNotice notice)
    {
    }
}
