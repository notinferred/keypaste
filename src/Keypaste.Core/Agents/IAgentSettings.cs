using System.Diagnostics.CodeAnalysis;

namespace Keypaste.Core.Agents;

/// <summary>What saving an agent's settings came to.</summary>
public enum AgentSaveOutcome
{
    /// <summary>The vault's file holds the new record.</summary>
    Saved = 0,

    /// <summary>The record already said this, so nothing was saved.</summary>
    Unchanged = 1,

    /// <summary>The record was refused before anything changed; the reason says why.</summary>
    Refused = 2,

    /// <summary>No vault is open.</summary>
    Locked = 3,

    /// <summary>Another program saved the vault since it was opened, so nothing was written.</summary>
    ChangedOnDisk = 4,

    /// <summary>The save failed; the reason says why.</summary>
    Failed = 5,
}

/// <summary>What saving an agent's settings came to, and why when it did not.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Reason">keypaste's own words when nothing was saved, otherwise empty.</param>
public sealed record AgentSave(AgentSaveOutcome Outcome, string Reason)
{
    /// <summary>The file holds the new record.</summary>
    public static AgentSave Saved { get; } = new(AgentSaveOutcome.Saved, string.Empty);

    /// <summary>Nothing needed saving.</summary>
    public static AgentSave Unchanged { get; } = new(AgentSaveOutcome.Unchanged, string.Empty);

    /// <summary>Whether the vault's file now holds what was asked.</summary>
    public bool Holds => Outcome is AgentSaveOutcome.Saved or AgentSaveOutcome.Unchanged;
}

/// <summary>The records the vault holds, read as one list for <c>keypaste clients</c>.</summary>
/// <param name="State">Whether the open vault matches its file; the records are read from the open vault either way.</param>
/// <param name="Records">Every record, by name.</param>
public sealed record AgentRecordList(SavedRead State, IReadOnlyList<AgentRecord> Records)
{
    /// <summary>No records: no vault is open, or it holds none.</summary>
    public static AgentRecordList Empty { get; } = new(SavedRead.Current, []);
}

/// <summary>
/// Where a vault's owner reads each agent's settings and this computer's ceilings, and writes a
/// change it was asked for: the vault's records through the owner's own save, and <c>clients.toml</c>.
/// </summary>
public interface IAgentSettings
{
    /// <summary>Settings that hold no record and no ceiling, so every agent gets the defaults; nothing is written.</summary>
    static IAgentSettings Defaults { get; } = new NoSettings();

    /// <summary>What bears on an agent's requests now.</summary>
    /// <param name="label">The bridge's raw label, or null for a bridge started without one.</param>
    /// <param name="records">The vault's records for it; none while no vault is open.</param>
    /// <param name="caps">This computer's ceilings.</param>
    /// <param name="problem">Why they could not be read, otherwise empty.</param>
    /// <returns>False when <c>clients.toml</c> or the records cannot be read, which refuses every agent request.</returns>
    bool TryRead(string? label, [NotNullWhen(true)] out AgentRecords? records, [NotNullWhen(true)] out AccessCaps? caps, out string problem);

    /// <summary>Every record the open vault holds.</summary>
    /// <returns>The records, or <see cref="AgentRecordList.Empty"/> while no vault is open.</returns>
    AgentRecordList List();

    /// <summary>What an agent's record would read as once a change is written into it, writing nothing.</summary>
    /// <param name="name">The agent's name, or <see cref="AgentNames.Any"/>.</param>
    /// <param name="edit">The change.</param>
    /// <param name="record">
    /// What the record would read as: not <see cref="AcafRead.Understood"/> when it would still hold parts
    /// keypaste does not understand, so the agent gets no more than the defaults until they are fixed.
    /// </param>
    /// <param name="problem">Why the change cannot be written into the vault, otherwise empty.</param>
    /// <returns>Whether it can be.</returns>
    bool TryPreview(string name, AccessEdit edit, [NotNullWhen(true)] out AcafRead? record, out string problem);

    /// <summary>Writes a change to an agent's settings into the vault, rewriting only what it names, and saves it.</summary>
    /// <param name="name">The agent's name, or <see cref="AgentNames.Any"/>.</param>
    /// <param name="edit">The change.</param>
    /// <returns>What happened; unless the vault's file holds the change, the open vault does not hold it either.</returns>
    AgentSave Write(string name, AccessEdit edit);

    /// <summary>Changes <c>clients.toml</c> from what it holds at this moment, as <see cref="AccessCaps.TryChange"/> does.</summary>
    /// <param name="change">The new ceilings from those the file holds now.</param>
    /// <param name="problem">Why it was not changed, otherwise empty.</param>
    /// <returns>Whether the file holds the change, written or already so.</returns>
    /// <exception cref="ArgumentException">From <paramref name="change"/>: the file could not hold the new ceilings.</exception>
    bool TryChangeCaps(Func<AccessCaps, AccessCaps> change, out string problem);

    /// <summary>Whether <c>clients.toml</c> can be used, as <see cref="AccessCaps.Probe"/> says.</summary>
    /// <param name="problem">What is wrong with it, otherwise empty.</param>
    /// <returns>What it is.</returns>
    CapsFile ProbeCaps(out string problem);

    /// <summary>Sets a malformed <c>clients.toml</c> aside and starts an empty one, as <see cref="AccessCaps.TryReset"/> does.</summary>
    /// <param name="kept">Where it was set aside, once it was; otherwise empty.</param>
    /// <param name="problem">Why it was not reset, otherwise empty.</param>
    /// <returns>What happened.</returns>
    CapsChange TryResetCaps(out string kept, out string problem);

    private sealed class NoSettings : IAgentSettings
    {
        private const string _none = "this keypaste process keeps no agent settings";

        public bool TryRead(string? label, [NotNullWhen(true)] out AgentRecords? records, [NotNullWhen(true)] out AccessCaps? caps, out string problem)
        {
            records = AgentRecords.None;
            caps = AccessCaps.Empty;
            problem = string.Empty;
            return true;
        }

        public AgentRecordList List() => AgentRecordList.Empty;

        public bool TryPreview(string name, AccessEdit edit, [NotNullWhen(true)] out AcafRead? record, out string problem)
        {
            record = new AcafRead(edit.Apply(AgentAccess.Defaults), true, true);
            problem = string.Empty;
            return true;
        }

        public AgentSave Write(string name, AccessEdit edit) => new(AgentSaveOutcome.Refused, _none);

        public bool TryChangeCaps(Func<AccessCaps, AccessCaps> change, out string problem)
        {
            problem = _none;
            return false;
        }

        public CapsFile ProbeCaps(out string problem)
        {
            problem = string.Empty;
            return CapsFile.Usable;
        }

        public CapsChange TryResetCaps(out string kept, out string problem)
        {
            kept = string.Empty;
            problem = _none;
            return CapsChange.Unwritten;
        }
    }
}
