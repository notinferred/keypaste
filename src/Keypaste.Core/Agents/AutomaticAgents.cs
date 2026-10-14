namespace Keypaste.Core.Agents;

/// <summary>
/// Who is answered without asking: each named agent whose settings make somewhere Automatic, as an
/// owner tells the person before they leave it serving agents.
/// </summary>
/// <remarks>
/// Judged as a request would be, beneath this computer's ceilings and against an end that has
/// passed, so an agent held back here, or whose Automatic has ended, is not named. A record not wholly
/// understood applies only beneath the defaults, which are never Automatic, and <c>*</c> never is.
/// </remarks>
public static class AutomaticAgents
{
    private const string _byDefault = "by default";

    /// <summary>Names each agent Automatic somewhere now, with where.</summary>
    /// <param name="records">The vault's records, as <see cref="AgentAccessStore.List"/> reads them.</param>
    /// <param name="caps">This computer's ceilings.</param>
    /// <param name="now">What an end to Automatic is judged against.</param>
    /// <returns>One item per agent, in the records' order, such as <c>claude-code (billing:dev)</c>.</returns>
    public static IReadOnlyList<string> Named(IReadOnlyList<AgentRecord> records, AccessCaps caps, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(caps);

        List<AcafRead> any = [.. records.Where(record => string.Equals(record.Name, AgentNames.Any, StringComparison.Ordinal)).Select(record => record.Record)];
        List<string> named = [];

        foreach (var agent in records.Where(record => AgentNames.IsNamed(record.Name)).GroupBy(record => record.Name, StringComparer.Ordinal))
        {
            var access = EffectiveAccess.Create(agent.Key, new AgentRecords(SavedRead.Current, [.. agent.Select(record => record.Record)], any), caps);
            var places = agent
                .SelectMany(record => Places(record.Record.Access))
                .Select(scope => (Scope: scope, Name: scope?.Name ?? _byDefault))
                .DistinctBy(place => place.Name, StringComparer.Ordinal)
                .Where(place => access.IsAutomaticIn(place.Scope, now))
                .Select(place => place.Name)
                .ToList();

            if (places.Count > 0)
            {
                named.Add($"{agent.Key} ({string.Join(", ", places)})");
            }
        }

        return named;
    }

    /// <summary>Where a record says Automatic: its default, then each of its rows.</summary>
    private static IEnumerable<ProjectScope?> Places(AgentAccess access)
    {
        if (access.Mode == AgentMode.Automatic)
        {
            yield return null;
        }

        foreach (var row in access.Projects.Where(project => project.Mode == AgentMode.Automatic))
        {
            yield return row.Scope;
        }
    }
}
