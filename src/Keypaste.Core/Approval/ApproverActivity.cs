namespace Keypaste.Core.Approval;

/// <summary>What an owner's gate and grant cache hold, as a person inspecting them sees it: never a value.</summary>
/// <param name="Waiting">The request in front of a person; at most one, because the gate asks one at a time.</param>
/// <param name="Grants">The grants in force, soonest to expire first.</param>
public sealed record ApproverActivity(IReadOnlyList<WaitingRequest> Waiting, IReadOnlyList<GrantInForce> Grants)
{
    /// <summary>Nothing waiting and nothing granted, which is what a session that is not live holds.</summary>
    public static ApproverActivity None { get; } = new([], []);

    /// <summary>The timed grants given to a repeated <c>keypaste run --session</c>, soonest to expire first.</summary>
    public IReadOnlyList<EnvGrantInForce> EnvGrants { get; init; } = [];
}

/// <summary>A timed grant a person gave a repeated <c>keypaste run --session</c>: names only, never a value.</summary>
/// <param name="Key">The request identity it answers, so it can be revoked.</param>
/// <param name="Project">The project whose set it releases.</param>
/// <param name="Profile">The profile it releases.</param>
/// <param name="Command">The command it was given to, as the prompt showed it.</param>
/// <param name="Remaining">How long it has left.</param>
public sealed record EnvGrantInForce(string Key, string Project, string Profile, string Command, TimeSpan Remaining);
