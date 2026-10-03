namespace Keypaste.Core;

/// <summary>What one profile holds for one key.</summary>
public enum EnvCellState
{
    /// <summary>The key is there and would be released.</summary>
    Set = 0,

    /// <summary>Another profile has the key and this one does not.</summary>
    Missing = 1,

    /// <summary>The key is there and resolving the profile would refuse it.</summary>
    Unusable = 2,
}

/// <summary>One key in one profile. Holds no value.</summary>
/// <param name="Profile">The profile.</param>
/// <param name="State">What the profile holds for the key.</param>
/// <param name="Problem">Why it is unusable, in <see cref="EnvResolution"/>'s words, or null.</param>
/// <param name="SameValueAs">The other profiles holding an identical value, for a set key.</param>
public sealed record EnvCell(string Profile, EnvCellState State, string? Problem, IReadOnlyList<string> SameValueAs)
{
    /// <summary>The entries holding the key as a field in this profile; more than one is a refusal.</summary>
    public IReadOnlyList<EntryName> Sources { get; init; } = [];
}

/// <summary>One key across every profile of a project.</summary>
/// <param name="Key">The variable name.</param>
/// <param name="Cells">One cell per profile, in <see cref="EnvMatrix.Profiles"/> order.</param>
public sealed record EnvMatrixRow(string Key, IReadOnlyList<EnvCell> Cells);

/// <summary>
/// A project's keys against its profiles, for the Env profiles screen and <c>keypaste env diff</c>.
/// </summary>
/// <remarks>
/// Values are compared while it is built and then dropped: no record here has a member that could
/// hold one, so a matrix can be bound, logged or printed without that question arising.
/// </remarks>
/// <param name="Project">The project.</param>
/// <param name="Profiles">Its environments, as <see cref="ProjectCatalog"/> orders them.</param>
/// <param name="Rows">One row per key any profile holds, ordinal by key.</param>
public sealed record EnvMatrix(string Project, IReadOnlyList<EnvProfileInfo> Profiles, IReadOnlyList<EnvMatrixRow> Rows)
{
    /// <summary>Builds the matrix from the open vault as it is now.</summary>
    /// <param name="vault">The open vault.</param>
    /// <param name="project">The project.</param>
    /// <param name="clock">What expiry is judged against.</param>
    /// <returns>The matrix; empty when the project does not exist.</returns>
    public static EnvMatrix Build(Vault vault, string project, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(clock);

        var snapshot = vault.ReadEnvSnapshot();
        List<EnvProfileInfo> profiles = ProjectCatalog.Read(vault).Projects
            .FirstOrDefault(listing => string.Equals(listing.Name, project, StringComparison.Ordinal))?.Environments
            .Select(environment => new EnvProfileInfo(environment.Name, environment.IsProtected))
            .ToList() ?? [];
        var now = clock.GetUtcNow();

        var held = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var holders = new Dictionary<(string Profile, string Key), List<EntryName>>();
        var problems = new Dictionary<(string Profile, string Key), string>();

        foreach (var profile in profiles)
        {
            var resolved = EnvResolution.Resolve(snapshot, project, profile.Name, keys: null, now);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var member in EnvResolution.Members(snapshot, project, profile.Name, out _))
            {
                values[member.Key] = member.Value;

                if (!holders.TryGetValue((profile.Name, member.Key), out var entries))
                {
                    entries = [];
                    holders[(profile.Name, member.Key)] = entries;
                }

                entries.Add(member.Entry.Name);
            }

            foreach (var problem in resolved.Problems.GroupBy(problem => problem.Key, StringComparer.Ordinal))
            {
                problems[(profile.Name, problem.Key)] = string.Join("; ", problem.Select(p => p.Reason));
            }

            held[profile.Name] = values;
        }

        var rows = held.Values
            .SelectMany(values => values.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(key => new EnvMatrixRow(key, [.. profiles.Select(profile => Cell(profile.Name, key))]))
            .ToList();

        return new EnvMatrix(project, profiles, rows);

        EnvCell Cell(string profile, string key)
        {
            if (!held[profile].TryGetValue(key, out var value))
            {
                return new EnvCell(profile, EnvCellState.Missing, null, []);
            }

            var sources = holders[(profile, key)];

            if (problems.TryGetValue((profile, key), out var problem))
            {
                return new EnvCell(profile, EnvCellState.Unusable, problem, []) { Sources = sources };
            }

            var same = profiles
                .Where(other => !string.Equals(other.Name, profile, StringComparison.Ordinal)
                    && !problems.ContainsKey((other.Name, key))
                    && held[other.Name].TryGetValue(key, out var otherValue)
                    && string.Equals(otherValue, value, StringComparison.Ordinal))
                .Select(other => other.Name)
                .ToList();

            return new EnvCell(profile, EnvCellState.Set, null, same) { Sources = sources };
        }
    }

    /// <summary>The row for one key, or null when no profile holds it.</summary>
    /// <param name="key">The variable name.</param>
    /// <returns>The row.</returns>
    public EnvMatrixRow? Row(string key) =>
        Rows.FirstOrDefault(row => string.Equals(row.Key, key, StringComparison.Ordinal));
}
