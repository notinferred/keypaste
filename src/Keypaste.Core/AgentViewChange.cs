using Keypaste.Core.Agents;

namespace Keypaste.Core;

/// <summary>
/// Which agents a move changes the view of, since what an agent sees by place follows the path and not the item (THREATS.md T-13).
/// Judged through any bridge, since a bridge's own <c>--expose</c> can only narrow it.
/// </summary>
/// <param name="Gaining">The agents that see the item only after the move.</param>
/// <param name="Losing">The agents that see the item only before it.</param>
/// <param name="Widening">The agents that saw its variables by a project tag and see all of it after the move, password included.</param>
/// <param name="Narrowing">The agents that saw all of it and see only its variables after the move.</param>
public sealed record AgentViewChange(
    IReadOnlyList<string> Gaining,
    IReadOnlyList<string> Losing,
    IReadOnlyList<string> Widening,
    IReadOnlyList<string> Narrowing)
{
    private static readonly EntryExposure _everything =
        EntryExposure.TryCreate(["**"], out var everything, out var error) ? everything : throw new InvalidOperationException(error);

    /// <summary>Whether any agent's view changes.</summary>
    public bool Any => Gaining.Count > 0 || Losing.Count > 0 || Widening.Count > 0 || Narrowing.Count > 0;

    /// <summary>What moving an entry would do to each agent's view, beneath this computer's ceilings.</summary>
    /// <param name="vault">The open vault, before the move.</param>
    /// <param name="from">The entry where it is.</param>
    /// <param name="to">The entry where it would go.</param>
    /// <param name="clientsPath">This computer's <c>clients.toml</c>; while it cannot be read, the vault's settings alone say.</param>
    /// <returns>The agents, each once, in the records' order.</returns>
    public static AgentViewChange OfMove(Vault vault, EntryName from, EntryName to, string clientsPath)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(clientsPath);

        var caps = AccessCaps.TryLoad(clientsPath, out var loaded, out _) ? loaded! : AccessCaps.Empty;
        var tags = vault.Tags(from) ?? [];
        var store = new AgentAccessStore(vault);
        List<string> gaining = [];
        List<string> losing = [];
        List<string> widening = [];
        List<string> narrowing = [];

        foreach (var name in store.List().Select(record => record.Name).Where(AgentNames.IsNamed).Distinct(StringComparer.Ordinal))
        {
            var access = EffectiveAccess.Create(name, store.Read(name), caps);
            var before = access.Reach(_everything, from, tags);
            var after = access.Reach(_everything, to, tags);

            if (before == after)
            {
                continue;
            }

            var changed = (before, after) switch
            {
                (ExposureReach.None, _) => gaining,
                (_, ExposureReach.None) => losing,
                (_, ExposureReach.Entry) => widening,
                _ => narrowing,
            };
            changed.Add(name);
        }

        return new AgentViewChange(gaining, losing, widening, narrowing);
    }

    /// <summary>What the move did for agents, naming the item by its title and each agent by its label, sanitized, and no value.</summary>
    /// <param name="title">The item's title.</param>
    /// <returns>One sentence, or null when no agent's view changed.</returns>
    public string? Describe(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        if (!Any)
        {
            return null;
        }

        var item = EntryNameSanitizer.Sanitize(title).Text;
        List<string> parts = [];

        if (Gaining.Count > 0)
        {
            parts.Add($"{Names(Gaining)} can now see {item}");
        }

        if (Widening.Count > 0)
        {
            parts.Add($"{Names(Widening)} can now see all of {item}");
        }

        if (Narrowing.Count > 0)
        {
            parts.Add($"{Names(Narrowing)} can now see only the variables of {item}");
        }

        if (Losing.Count > 0)
        {
            parts.Add($"{Names(Losing)} can no longer see {item}");
        }

        return string.Join("; ", parts) + ".";

        static string Names(IReadOnlyList<string> agents) =>
            string.Join(", ", agents.Select(agent => EntryNameSanitizer.Sanitize(agent).Text));
    }
}
