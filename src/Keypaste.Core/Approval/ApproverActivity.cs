namespace Keypaste.Core.Approval;

/// <summary>What an owner's gate holds, as a person inspecting it sees it: never a value.</summary>
/// <param name="Waiting">The request in front of a person; at most one, because the gate asks one at a time.</param>
public sealed record ApproverActivity(IReadOnlyList<WaitingRequest> Waiting)
{
    /// <summary>Nothing waiting, which is what a session that is not live holds.</summary>
    public static ApproverActivity None { get; } = new([]);

    /// <summary>The agent's run in front of a person; at most one.</summary>
    public IReadOnlyList<WaitingRun> WaitingRuns { get; init; } = [];

    /// <summary>The <c>keypaste run --session</c> request in front of a person; at most one.</summary>
    public IReadOnlyList<WaitingEnv> WaitingEnvs { get; init; } = [];

    /// <summary>The confirmation of a change that gives an agent more in front of a person; at most one.</summary>
    public IReadOnlyList<WaitingChange> WaitingChanges { get; init; } = [];

    /// <summary>The terminal's edit in front of a person; at most one.</summary>
    public IReadOnlyList<WaitingEdit> WaitingEdits { get; init; } = [];
}
