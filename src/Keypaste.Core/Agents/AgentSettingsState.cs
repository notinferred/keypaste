namespace Keypaste.Core.Agents;

/// <summary>One agent's settings as the person's own screen shows them: what the vault says, and what this computer holds back.</summary>
/// <param name="Agent">The agent's name, or <see cref="AgentNames.Any"/>.</param>
/// <param name="Vault">
/// The settings a change starts from: the agent's own record, the one in its own entry where several
/// are titled with its name, else the <see cref="AgentNames.Any"/> record it is held beneath, else the
/// defaults.
/// </param>
/// <param name="HasRecord">Whether the vault holds a record under the agent's own name.</param>
/// <param name="BeneathDefaults">
/// Whether its records apply only beneath the defaults: the vault holds a change not saved yet, another
/// program changed its file, or a record is not wholly understood.
/// </param>
/// <param name="HeldBack">What this computer's ceilings keep from the agent that <paramref name="Vault"/> gives it.</param>
public sealed record AgentSettingsState(
    string Agent,
    AgentAccess Vault,
    bool HasRecord,
    bool BeneathDefaults,
    IReadOnlyList<AccessGain> HeldBack)
{
    /// <summary>Reads one agent's settings from its records and this computer's ceilings.</summary>
    /// <param name="agent">The agent's name, or <see cref="AgentNames.Any"/>.</param>
    /// <param name="records">The records that bear on it.</param>
    /// <param name="caps">This computer's ceilings.</param>
    /// <returns>Its settings.</returns>
    public static AgentSettingsState Read(string agent, AgentRecords records, AccessCaps caps)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(caps);

        var own = AgentNames.IsNamed(agent) ? records.Own : records.Any;
        var vault = own.Count > 0 ? own[0].Access
            : records.Any.Count > 0 ? records.Any[0].Access
            : AgentAccess.Defaults;

        // What the ceilings take, apart from what applying beneath the defaults already takes.
        var capped = AccessChange.Classify(EffectiveAccess.Create(agent, records, caps), vault).Gains;
        var uncapped = AccessChange.Classify(EffectiveAccess.Create(agent, records, AccessCaps.Empty), vault).Gains;

        return new AgentSettingsState(
            agent,
            vault,
            own.Count > 0,
            records.State != SavedRead.Current || own.Any(record => !record.Understood),
            [.. capped.Except(uncapped)]);
    }
}
