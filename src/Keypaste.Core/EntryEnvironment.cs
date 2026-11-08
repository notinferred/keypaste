using Keypaste.Core.Agents;

namespace Keypaste.Core;

/// <summary>
/// One project environment an entry's own tags put it in, as the item's preview lists it, and whether an
/// agent gets its variables only once a person answers. Names only, never a value.
/// </summary>
/// <param name="Project">The project.</param>
/// <param name="Environment">The environment.</param>
/// <param name="ApprovalRequired">
/// Whether an agent's request for the entry's project variables is asked or blocked rather than allowed, by this
/// computer's rules. Null when its <c>agents.toml</c> could not be read, which refuses every agent request until it reads again.
/// </param>
/// <remarks>The rule is one for every project, so every environment of one entry says the same.</remarks>
public sealed record EntryEnvironment(string Project, string Environment, bool? ApprovalRequired)
{
    /// <summary>The environments an entry's own project tags put it in, in the order of its tags.</summary>
    /// <param name="vault">The open vault.</param>
    /// <param name="entry">The entry.</param>
    /// <param name="rules">This computer's rules, from <see cref="Audit.KeypasteHome.AgentsPath"/>.</param>
    /// <returns>Each environment once, empty for an entry in no project, or null when no entry answers to the name.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    public static IReadOnlyList<EntryEnvironment>? Read(Vault vault, EntryName entry, IAgentRules rules)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(rules);

        if (vault.Tags(entry) is not { } tags)
        {
            return null;
        }

        var places = tags
            .Select(ProjectTag.Read)
            .Where(tag => tag.Kind == ProjectTagKind.Member)
            .Select(tag => (Project: tag.Project!, Environment: tag.Environment!))
            .Distinct()
            .ToList();

        if (places.Count == 0)
        {
            return [];
        }

        bool? required = rules.TryRead(out var read, out _) ? read.RuleFor(FieldCategory.ProjectVariable) != CategoryRule.Allow : null;

        return [.. places.Select(place => new EntryEnvironment(place.Project, place.Environment, required))];
    }
}
