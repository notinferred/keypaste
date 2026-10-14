using Keypaste.Core.Agents;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>Short ways to write agents' settings in a test.</summary>
internal static class AgentAccessBuilders
{
    internal static EntryExposure See(params string[] globs)
    {
        Assert.True(EntryExposure.TryCreate(globs, out var exposure, out var error), error);
        return exposure;
    }

    internal static ProjectScope Scope(string name)
    {
        Assert.True(ProjectScope.TryParseName(name, out var scope, out var error), error);
        return scope;
    }

    internal static ProjectAccess Row(string name, AgentMode mode, AccessLimit? limit = null) => new(Scope(name), mode, limit);

    internal static AgentAccess Automatic { get; } = AgentAccess.Defaults with { Mode = AgentMode.Automatic };

    internal static AgentAccess Asks { get; } = AgentAccess.Defaults with { Mode = AgentMode.Ask };

    internal static AcafRead Understood(AgentAccess access) => new(access, true, true);

    internal static AgentRecords Own(params AgentAccess[] records) =>
        new(SavedRead.Current, [.. records.Select(Understood)], []);

    internal static AgentRecords Any(params AgentAccess[] records) =>
        new(SavedRead.Current, [], [.. records.Select(Understood)]);

    internal static AccessCaps Caps(params CapsRow[] rows) => rows.Aggregate(AccessCaps.Empty, (caps, row) => caps.With(row));
}
