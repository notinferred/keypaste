using Keypaste.Core.Agents;

namespace Keypaste.Core;

/// <summary>
/// One project environment an entry's own tags put it in, as the item's preview lists it, and whether an
/// agent gets the entry only once a person answers. Names only, never a value.
/// </summary>
/// <param name="Project">The project.</param>
/// <param name="Environment">The environment.</param>
/// <param name="ApprovalRequired">
/// Whether no named agent gets the entry without asking, judged as a request is (D-0441): the strictest
/// across the entry's project tags, a protected environment always asking (D-0371), beneath this
/// computer's ceilings. Null when this computer's <c>clients.toml</c> could not be read, which refuses
/// every agent request until it reads again.
/// </param>
/// <remarks>
/// An agent's request is decided for the entry rather than for one environment of it, so every
/// environment of one entry says the same.
/// </remarks>
public sealed record EntryEnvironment(string Project, string Environment, bool? ApprovalRequired)
{
    /// <summary>The environments an entry's own project tags put it in, in the order of its tags.</summary>
    /// <param name="vault">The open vault.</param>
    /// <param name="entry">The entry.</param>
    /// <param name="clientsPath">This computer's <c>clients.toml</c>, from <see cref="Audit.KeypasteHome.ClientsPath"/>.</param>
    /// <param name="now">What a limit on Automatic is judged against.</param>
    /// <returns>Each environment once, empty for an entry in no project, or null when no entry answers to the name.</returns>
    /// <exception cref="VaultException">More than one entry answers to that name.</exception>
    public static IReadOnlyList<EntryEnvironment>? Read(Vault vault, EntryName entry, string clientsPath, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(clientsPath);

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

        bool? required = null;

        if (AccessCaps.TryLoad(clientsPath, out var caps, out _))
        {
            var hasVariables = (vault.Fields(entry) ?? []).Any(field => EnvConvention.IsEnvNamedField(field.Name));
            var store = new AgentAccessStore(vault);

            required = !store.List()
                .Select(record => record.Name)
                .Where(AgentNames.IsNamed)
                .Distinct(StringComparer.Ordinal)
                .Any(name => ProjectTagChange.Releases(EffectiveAccess.Create(name, store.Read(name), caps), entry, tags, hasVariables, now));
        }

        return [.. places.Select(place => new EntryEnvironment(place.Project, place.Environment, required))];
    }
}
