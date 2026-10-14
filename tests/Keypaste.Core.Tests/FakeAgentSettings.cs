using System.Diagnostics.CodeAnalysis;
using Keypaste.Core.Agents;

namespace Keypaste.Core.Tests;

/// <summary>Agents' settings held in memory for a test: the vault's records by name, this computer's ceilings, and what was written.</summary>
internal sealed class FakeAgentSettings : IAgentSettings
{
    private readonly Lock _gate = new();

    /// <summary>The vault's records, by agent name or <c>*</c>.</summary>
    internal Dictionary<string, AgentAccess> Records { get; } = new(StringComparer.Ordinal);

    /// <summary>This computer's ceilings.</summary>
    internal AccessCaps Caps { get; set; } = AccessCaps.Empty;

    /// <summary>Set to refuse every read, as an unusable <c>clients.toml</c> does.</summary>
    internal string? Problem { get; set; }

    /// <summary>The names whose records hold parts keypaste cannot read, now and after any change.</summary>
    internal HashSet<string> Unreadable { get; } = new(StringComparer.Ordinal);

    /// <summary>Whether the records are read as the vault's file holds them.</summary>
    internal SavedRead State { get; set; } = SavedRead.Current;

    /// <summary>What each save comes to.</summary>
    internal AgentSave Saving { get; set; } = AgentSave.Saved;

    /// <summary>Every record written, in order, as the change written left it.</summary>
    internal List<(string Name, AgentAccess Access)> Written { get; } = [];

    /// <summary>Every <c>clients.toml</c> written, in order, each changed from the ceilings as they were at that moment.</summary>
    internal List<AccessCaps> CapsWritten { get; } = [];

    /// <summary>Set to refuse every write of <c>clients.toml</c>, as a file that cannot be written does.</summary>
    internal string? CapsProblem { get; set; }

    /// <summary>What <c>clients.toml</c> is when probed: usable unless a test says it is malformed or unreadable.</summary>
    internal CapsFile CapsFile { get; set; } = CapsFile.Usable;

    /// <summary>How many times <c>clients.toml</c> was reset.</summary>
    internal int Resets { get; private set; }

    /// <summary>Settings under which the named agent may run commands, everything else at the defaults.</summary>
    internal static FakeAgentSettings Running(string agent)
    {
        var settings = new FakeAgentSettings();
        settings.Records[agent] = AgentAccess.Defaults with { Run = true };
        return settings;
    }

    public bool TryRead(string? label, [NotNullWhen(true)] out AgentRecords? records, [NotNullWhen(true)] out AccessCaps? caps, out string problem)
    {
        lock (_gate)
        {
            if (Problem is { } why)
            {
                records = null;
                caps = null;
                problem = why;
                return false;
            }

            records = new AgentRecords(
                State,
                AgentNames.IsNamed(label) && Records.TryGetValue(label, out var own) ? [Read(label, own)] : [],
                Records.TryGetValue(AgentNames.Any, out var any) ? [Read(AgentNames.Any, any)] : []);
            caps = Caps;
            problem = string.Empty;
            return true;
        }
    }

    public AgentRecordList List()
    {
        lock (_gate)
        {
            return new AgentRecordList(
                State,
                [.. Records.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new AgentRecord(pair.Key, Read(pair.Key, pair.Value)))]);
        }
    }

    public bool TryPreview(string name, AccessEdit edit, [NotNullWhen(true)] out AcafRead? record, out string problem)
    {
        lock (_gate)
        {
            record = Read(name, edit.Apply(Start(name)));
            problem = string.Empty;
            return true;
        }
    }

    public AgentSave Write(string name, AccessEdit edit)
    {
        lock (_gate)
        {
            var access = edit.Apply(Start(name));
            Written.Add((name, access));

            if (Saving.Holds)
            {
                Records[name] = access;
            }

            return Saving;
        }
    }

    /// <summary>What a change to a name starts from: its own record, else the <c>*</c> record, else the defaults.</summary>
    private AgentAccess Start(string name) =>
        Records.TryGetValue(name, out var own) ? own
        : Records.TryGetValue(AgentNames.Any, out var any) ? any
        : AgentAccess.Defaults;

    private AcafRead Read(string name, AgentAccess access) => new(access, !Unreadable.Contains(name), true);

    public CapsFile ProbeCaps(out string problem)
    {
        lock (_gate)
        {
            problem = CapsFile == CapsFile.Usable ? string.Empty : "line 3: 'policy' is not a setting";
            return CapsFile;
        }
    }

    public CapsChange TryResetCaps(out string kept, out string problem)
    {
        lock (_gate)
        {
            kept = string.Empty;
            problem = string.Empty;

            if (CapsFile != CapsFile.Malformed)
            {
                return CapsFile == CapsFile.Usable ? CapsChange.Unchanged : CapsChange.Unreadable;
            }

            Resets++;
            kept = "clients.toml" + AccessCaps.KeptSuffix;
            CapsFile = CapsFile.Usable;
            Caps = AccessCaps.Empty;
            return CapsChange.Written;
        }
    }

    public bool TryChangeCaps(Func<AccessCaps, AccessCaps> change, out string problem)
    {
        lock (_gate)
        {
            var changed = change(Caps);
            problem = string.Empty;

            if (changed.Rows.SequenceEqual(Caps.Rows))
            {
                return true;
            }

            if (CapsProblem is { } why)
            {
                problem = why;
                return false;
            }

            CapsWritten.Add(changed);
            Caps = changed;
            return true;
        }
    }
}
