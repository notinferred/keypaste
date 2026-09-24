namespace Keypaste.Core.Approval;

/// <summary>What an owner's gate and grant cache hold, as a person inspecting them sees it: never a value.</summary>
/// <param name="Waiting">The request in front of a person; at most one, because the gate asks one at a time.</param>
/// <param name="Grants">The grants in force, soonest to expire first.</param>
public sealed record ApproverActivity(IReadOnlyList<WaitingRequest> Waiting, IReadOnlyList<GrantInForce> Grants)
{
    /// <summary>Nothing waiting and nothing granted, which is what a session that is not live holds.</summary>
    public static ApproverActivity None { get; } = new([], []);
}
