namespace Keypaste.Core.Agents;

/// <summary>One entry of the agents group: the name it is titled with and what its record says.</summary>
/// <param name="Name">The agent's name, or <see cref="AgentNames.Any"/>.</param>
/// <param name="Record">What its record says.</param>
public sealed record AgentRecord(string Name, AcafRead Record);

/// <summary>The records that bear on one agent's requests.</summary>
/// <param name="State">
/// Whether they were read as the vault's file holds them. Anything but <see cref="SavedRead.Current"/>
/// means they were read from the open vault instead and apply only beneath the defaults.
/// </param>
/// <param name="Own">
/// Every record titled with the agent's name, the one in the entry with its <see cref="AcafIds.Agent"/>
/// first: one, or several where a person or another program made another entry under the name.
/// </param>
/// <param name="Any">Every record titled <see cref="AgentNames.Any"/>, in the same order.</param>
public sealed record AgentRecords(SavedRead State, IReadOnlyList<AcafRead> Own, IReadOnlyList<AcafRead> Any)
{
    /// <summary>No record at all, as a vault without the group holds.</summary>
    public static AgentRecords None { get; } = new(SavedRead.Current, [], []);
}

/// <summary>What writing an agent's settings came to.</summary>
public enum AgentAccessWrite
{
    /// <summary>The open vault holds the new record, for the caller to save.</summary>
    Written = 0,

    /// <summary>The record already said exactly this, and nothing changed.</summary>
    Unchanged = 1,

    /// <summary>The name is not one settings may be kept under.</summary>
    InvalidName = 2,

    /// <summary>A record for the name is of a version keypaste does not know, which it never rewrites.</summary>
    UnknownVersion = 3,

    /// <summary>The settings for <see cref="AgentNames.Any"/> would give more than the defaults, which that name never may.</summary>
    WiderThanDefaults = 4,

    /// <summary>The change is to a project row, and the record's <c>projects</c> is not a list keypaste can place one in without losing it.</summary>
    ProjectsUnreadable = 5,

    /// <summary>The record would hold more than <see cref="AgentAccess.MaximumProjects"/> project rows.</summary>
    TooManyProjects = 6,

    /// <summary>
    /// The record already holds more than <see cref="AgentAccess.MaximumProjects"/> project rows, which
    /// keypaste reads as none, so a change to one of them could not be judged.
    /// </summary>
    ProjectsOverLimit = 7,
}

/// <summary>
/// Agents' settings kept in a vault as ACAF records: one entry per agent in
/// <see cref="ReservedGroups.Agents"/>, titled with its name, its UUID the <see cref="AcafIds.Agent"/>
/// of the name, the record in its <c>acaf</c> field.
/// </summary>
/// <remarks>
/// <para>
/// A record authorizes only as the vault's file holds it, as a token is checked (D-0317): a vault
/// holding an unsaved change, or one whose file another program has written, has its records read
/// from memory and applied only beneath the defaults, so they can narrow and never widen.
/// </para>
/// <para>
/// Writing changes the open vault and does not save it. A change rewrites only what it names and keeps
/// every other member and row as the record holds them, so what another writer or a person in KeePassXC
/// put there survives it; while any of it is not understood, the record still applies only beneath the
/// defaults. Two copies that each create an agent's record create the same entry in the same group,
/// since their UUIDs follow their names, so a merge, KeePass's or KeePassXC's, keeps the newer record
/// and the older in its history. A reset writes a record of the defaults rather than deleting, so a
/// merge carries it by time like any other change. Another entry under the name, which a person or
/// another program made with another UUID, applies with it as the least of them, and a write gives it
/// the same record, which a merge carries by time where a removal would be undone.
/// </para>
/// </remarks>
/// <param name="vault">The open vault.</param>
public sealed class AgentAccessStore(Vault vault)
{
    /// <summary>The field an entry keeps its record in.</summary>
    public const string Field = "acaf";

    private readonly Vault _vault = vault ?? throw new ArgumentNullException(nameof(vault));

    /// <summary>The records that bear on an agent's requests now.</summary>
    /// <param name="label">The bridge's raw label, or null for a bridge started without one.</param>
    /// <returns>Its own records when the label names an agent, and every <see cref="AgentNames.Any"/> record.</returns>
    public AgentRecords Read(string? label)
    {
        var state = _vault.ReadSavedRecords(ReservedGroups.Agents, Field, out var saved);
        var records = saved ?? _vault.ReadRecords(ReservedGroups.Agents, Field);

        return new AgentRecords(
            state,
            AgentNames.IsNamed(label) ? Records(records, label) : [],
            Records(records, AgentNames.Any));
    }

    /// <summary>Every record the open vault holds, by name; a name holding two records is listed twice.</summary>
    /// <returns>The records, ordinally by name and each name's own entry first; an entry titled with no valid name is left out.</returns>
    public IReadOnlyList<AgentRecord> List() =>
        [.. _vault.ReadRecords(ReservedGroups.Agents, Field)
            .Where(record => AgentNames.IsValid(record.Title, out _))
            .OrderBy(record => record.Title, StringComparer.Ordinal)
            .ThenBy(record => record.Id != AcafIds.Agent(record.Title))
            .Select(record => new AgentRecord(record.Title, AcafRecord.Read(record.Value ?? string.Empty)))];

    /// <summary>Writes a change to an agent's settings into the open vault, which the caller saves.</summary>
    /// <param name="name">The agent's name, or <see cref="AgentNames.Any"/>.</param>
    /// <param name="edit">The change.</param>
    /// <returns>What happened.</returns>
    /// <remarks>
    /// The change is made to the record in the agent's own entry, rewriting only what it names, and
    /// every entry under the name is given the result. A name with no record starts from the first
    /// <see cref="AgentNames.Any"/> record, else the defaults, as what applies to it does. A name with a
    /// record of a version keypaste does not know is refused, and so is a record for
    /// <see cref="AgentNames.Any"/> that would read as giving more than the defaults in any way.
    /// </remarks>
    public AgentAccessWrite Write(string name, AccessEdit edit) => Write(name, edit, out _);

    /// <summary>Writes a change as <see cref="Write(string, AccessEdit)"/> does, and says how to take it back.</summary>
    /// <param name="name">The agent's name, or <see cref="AgentNames.Any"/>.</param>
    /// <param name="edit">The change.</param>
    /// <param name="undo">When it was written, what puts the open vault back as it was, for a save that then fails; otherwise null.</param>
    /// <returns>What happened.</returns>
    internal AgentAccessWrite Write(string name, AccessEdit edit, out Action? undo)
    {
        undo = null;
        var outcome = Rewritten(name, edit, out var text);

        if (outcome != AgentAccessWrite.Written)
        {
            return outcome;
        }

        return _vault.WriteRecord(new EntryName(ReservedGroups.Agents, name), AcafIds.Agent, Field, text, out undo)
            ? AgentAccessWrite.Written
            : AgentAccessWrite.Unchanged;
    }

    /// <summary>What an agent's record would read as once a change is written, writing nothing.</summary>
    /// <param name="name">The agent's name, or <see cref="AgentNames.Any"/>.</param>
    /// <param name="edit">The change.</param>
    /// <param name="record">What the record would read as, when it can be written; otherwise null.</param>
    /// <returns>
    /// <see cref="AgentAccessWrite.Written"/> when <see cref="Write(string, AccessEdit)"/> would write it,
    /// <see cref="AgentAccessWrite.Unchanged"/> for <see cref="AccessEdit.VaultSettings"/>, which leaves
    /// the record as it reads now, otherwise why not.
    /// </returns>
    public AgentAccessWrite Preview(string name, AccessEdit edit, out AcafRead? record)
    {
        var outcome = Rewritten(name, edit, out var text);

        record = outcome is AgentAccessWrite.Written or AgentAccessWrite.Unchanged ? AcafRecord.Read(text) : null;
        return outcome;
    }

    /// <summary>Writes an agent's settings whole into the open vault, which the caller saves.</summary>
    /// <param name="name">The agent's name, or <see cref="AgentNames.Any"/>.</param>
    /// <param name="access">Its settings.</param>
    /// <returns>What happened.</returns>
    /// <remarks>
    /// Every member the settings say is replaced, and the top-level members of the records it replaces
    /// that this version does not know are kept. A name with a record of a version keypaste does not
    /// know is refused, and so are settings for <see cref="AgentNames.Any"/> that give more than the
    /// defaults in any way, and settings with more project rows than a record keeps.
    /// </remarks>
    public AgentAccessWrite Write(string name, AgentAccess access)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(access);

        if (!AgentNames.IsValid(name, out _))
        {
            return AgentAccessWrite.InvalidName;
        }

        if (access.Projects.Count > AgentAccess.MaximumProjects)
        {
            return AgentAccessWrite.TooManyProjects;
        }

        if (string.Equals(name, AgentNames.Any, StringComparison.Ordinal)
            && !AccessChange.Classify(EffectiveAccess.Defaults, access).IsNarrowing)
        {
            return AgentAccessWrite.WiderThanDefaults;
        }

        var previous = Titled(_vault.ReadRecords(ReservedGroups.Agents, Field), name);

        if (previous.Any(text => text is not null && !AcafRecord.Read(text).Rewritable))
        {
            return AgentAccessWrite.UnknownVersion;
        }

        return _vault.WriteRecord(new EntryName(ReservedGroups.Agents, name), AcafIds.Agent, Field, AcafRecord.Write(access, previous))
            ? AgentAccessWrite.Written
            : AgentAccessWrite.Unchanged;
    }

    /// <summary>Writes a record of the defaults for an agent, which the caller saves.</summary>
    /// <param name="name">The agent's name, or <see cref="AgentNames.Any"/>.</param>
    /// <returns>What happened.</returns>
    public AgentAccessWrite Reset(string name) => Write(name, AgentAccess.Defaults);

    private AgentAccessWrite Rewritten(string name, AccessEdit edit, out string text)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(edit);

        text = string.Empty;

        if (!AgentNames.IsValid(name, out _))
        {
            return AgentAccessWrite.InvalidName;
        }

        var records = _vault.ReadRecords(ReservedGroups.Agents, Field);
        var previous = Titled(records, name);

        if (previous.Any(record => record is not null && !AcafRecord.Read(record).Rewritable))
        {
            return AgentAccessWrite.UnknownVersion;
        }

        var start = Records(records, AgentNames.Any) is [{ } any, ..] ? any.Access : AgentAccess.Defaults;

        if (edit.UsesVault)
        {
            text = previous is [var held, ..] ? held ?? string.Empty : AcafRecord.Write(start);
            return AgentAccessWrite.Unchanged;
        }

        var outcome = AgentAccessWrite.Written;

        if (previous.Count > 0)
        {
            outcome = AcafRecord.WriteChange(edit, previous, out text);
        }
        else
        {
            var access = edit.Apply(start);

            if (access.Projects.Count > AgentAccess.MaximumProjects)
            {
                outcome = AgentAccessWrite.TooManyProjects;
            }
            else
            {
                text = AcafRecord.Write(access);
            }
        }

        return outcome == AgentAccessWrite.Written
            && string.Equals(name, AgentNames.Any, StringComparison.Ordinal)
            && !AccessChange.Classify(EffectiveAccess.Defaults, AcafRecord.Read(text).Access).IsNarrowing
            ? AgentAccessWrite.WiderThanDefaults
            : outcome;
    }

    private static List<AcafRead> Records(IReadOnlyList<ReservedRecord> records, string name) =>
        [.. Titled(records, name).Select(text => AcafRecord.Read(text ?? string.Empty))];

    private static List<string?> Titled(IReadOnlyList<ReservedRecord> records, string name)
    {
        var id = AcafIds.Agent(name);

        return
        [
            .. records
                .Where(record => string.Equals(record.Title, name, StringComparison.Ordinal))
                .OrderBy(record => record.Id != id)
                .Select(record => record.Value),
        ];
    }
}
