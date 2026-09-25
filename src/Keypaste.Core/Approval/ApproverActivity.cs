namespace Keypaste.Core.Approval;

/// <summary>What an owner's gate and grant cache hold, as a person inspecting them sees it: never a value.</summary>
/// <param name="Waiting">The request in front of a person; at most one, because the gate asks one at a time.</param>
/// <param name="Grants">The grants in force, soonest to expire first.</param>
public sealed record ApproverActivity(IReadOnlyList<WaitingRequest> Waiting, IReadOnlyList<GrantInForce> Grants)
{
    /// <summary>Nothing waiting and nothing granted, which is what a session that is not live holds.</summary>
    public static ApproverActivity None { get; } = new([], []);

    /// <summary>The timed grants given to a repeated <c>keypaste run --session</c> or an agent's run, soonest to expire first.</summary>
    public IReadOnlyList<EnvGrantInForce> EnvGrants { get; init; } = [];

    /// <summary>The agent's run in front of a person; at most one.</summary>
    public IReadOnlyList<WaitingRun> WaitingRuns { get; init; } = [];

    /// <summary>The <c>keypaste run --session</c> request in front of a person; at most one.</summary>
    public IReadOnlyList<WaitingEnv> WaitingEnvs { get; init; } = [];
}

/// <summary>A timed grant a person gave a repeated <c>keypaste run --session</c> or an agent's run: names only, never a value.</summary>
/// <param name="Key">The request identity it answers, so it can be revoked.</param>
/// <param name="Project">The project whose set it releases.</param>
/// <param name="Profile">The profile it releases.</param>
/// <param name="Command">The command it was given to, as the prompt showed it.</param>
/// <param name="Remaining">How long it has left.</param>
public sealed record EnvGrantInForce(string Key, string Project, string Profile, string Command, TimeSpan Remaining)
{
    /// <summary>The agent client it was given to, as the prompt showed it, or null for <c>keypaste run --session</c>.</summary>
    public string? Client { get; init; }

    /// <summary>The bridge's raw <c>--client-label</c>, so a client's policy change can end it; null when none.</summary>
    public string? Label { get; init; }

    /// <summary>Each entry it releases, as <see cref="ApprovalPrompt.Shown"/> writes it.</summary>
    public IReadOnlyList<string> Entries { get; init; } = [];

    /// <summary>Grants are equal when every member is, the entries in order.</summary>
    /// <param name="other">The other grant.</param>
    /// <returns>Whether they are equal.</returns>
    public bool Equals(EnvGrantInForce? other) =>
        other is not null
        && Key == other.Key
        && Project == other.Project
        && Profile == other.Profile
        && Command == other.Command
        && Remaining == other.Remaining
        && Client == other.Client
        && Label == other.Label
        && Entries.SequenceEqual(other.Entries, StringComparer.Ordinal);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Key, Project, Profile, Command, Remaining, Client, Label, Entries.Count);
}
