using System.Globalization;

namespace Keypaste.Core;

/// <summary>
/// Resolves a <c>.env.keypaste</c> from an open vault, whole or not at all: every reference must
/// resolve, and the child gets each value under the file's name for it.
/// </summary>
/// <remarks>
/// An env reference is judged with its whole profile, as <see cref="EnvResolution"/> judges a set,
/// so an unusable profile refuses the file. An entry reference is read from the vault as its file
/// holds it (D-0317), never from a reserved group, and its field must hold something. A refusal
/// names each variable and its reference, never a value.
/// </remarks>
public static class EnvReferenceResolution
{
    /// <summary>What a resolved file is called when it names more than one project, or an entry.</summary>
    public const string MixedProject = EnvReferenceFile.FileName;

    /// <summary>What a resolved file's profile is called when it names more than one.</summary>
    public const string MixedProfile = "mixed";

    /// <summary>Resolves every line of a file.</summary>
    /// <param name="vault">The open vault.</param>
    /// <param name="document">A file <see cref="EnvReferenceFile.TryParse"/> read without problems.</param>
    /// <param name="clock">What expiry is judged against.</param>
    /// <returns>The variables in the file's order, or why none of them.</returns>
    public static EnvResolved Resolve(Vault vault, EnvReferenceDocument document, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(clock);

        var envs = document.Lines.Select(line => line.Reference).OfType<EnvReference>().ToList();
        var projects = envs.Select(env => env.Project).Distinct(StringComparer.Ordinal).ToList();
        var profiles = envs.Select(env => env.Profile).Distinct(StringComparer.Ordinal).ToList();
        var project = projects.Count == 1 && envs.Count == document.Lines.Count(line => line.Reference is not null) ? projects[0] : MixedProject;
        var profile = profiles.Count == 1 ? profiles[0] : MixedProfile;

        if (document.Problems.Count > 0)
        {
            return EnvResolved.Refused(
                project,
                EnvOutcome.Invalid,
                [.. document.Problems.Select(problem => new EnvProblem(string.Empty, problem.Message))],
                profile);
        }

        var state = vault.ReadSaved(out var entries, out var groupPaths);

        if (state != SavedRead.Current)
        {
            var outcome = state switch
            {
                SavedRead.Unsaved => EnvOutcome.Unsaved,
                SavedRead.ChangedOnDisk => EnvOutcome.ChangedOnDisk,
                _ => EnvOutcome.Unreadable,
            };

            return EnvResolved.Refused(project, outcome, profile: profile);
        }

        var now = clock.GetUtcNow();
        var sets = new Dictionary<(string Project, string Profile), EnvResolved>();
        List<EnvVariable> variables = [];
        List<EnvProblem> problems = [];

        foreach (var line in document.Lines)
        {
            string? value;
            string? why;

            switch (line.Reference)
            {
                case EnvReference env:
                    if (!sets.TryGetValue((env.Project, env.Profile), out var set))
                    {
                        set = EnvResolution.Resolve(entries!, groupPaths!, env.Project, env.Profile, now);
                        sets[(env.Project, env.Profile)] = set;
                    }

                    (value, why) = Pick(set, env);
                    break;

                case EntryReference entry:
                    (value, why) = Read(entries!, entry, now);
                    break;

                default:
                    (value, why) = (line.Literal ?? string.Empty, null);
                    break;
            }

            if (why is not null)
            {
                problems.Add(new EnvProblem(line.Name, $"({line.Reference}) {why}"));
            }
            else
            {
                variables.Add(new EnvVariable(line.Name, value!));
            }
        }

        if (problems.Count == 0 && !EnvNameRules.TryCheck(variables, out var names))
        {
            problems.Add(new EnvProblem(string.Empty, names));
        }

        return problems.Count > 0
            ? EnvResolved.Refused(project, EnvOutcome.Unusable, problems, profile)
            : EnvResolved.Released(project, variables, profile);
    }

    /// <summary>What a file makes of a set released for its references: each value under the file's name for it, and each literal.</summary>
    /// <param name="document">A file whose references all name <paramref name="released"/>'s project and profile.</param>
    /// <param name="released">The set an owner released for the file's keys.</param>
    /// <returns>The file's variables in its order, or why none of them; a refused set is returned as it came.</returns>
    /// <remarks>For <c>keypaste run --session</c>, where the owner resolves keys and only the runner knows the file.</remarks>
    public static EnvResolved Apply(EnvReferenceDocument document, EnvResolved released)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(released);

        if (released.Outcome != EnvOutcome.Resolved)
        {
            return released;
        }

        List<EnvVariable> variables = [];
        List<EnvProblem> problems = [];

        foreach (var line in document.Lines)
        {
            if (line.Reference is null)
            {
                variables.Add(new EnvVariable(line.Name, line.Literal ?? string.Empty));
            }
            else if (line.Reference is EnvReference env
                && string.Equals(env.Project, released.Project, StringComparison.Ordinal)
                && string.Equals(env.Profile, released.Profile, StringComparison.Ordinal)
                && released.Variables.FirstOrDefault(variable => string.Equals(variable.Key, env.Key, StringComparison.Ordinal)) is { } found)
            {
                variables.Add(new EnvVariable(line.Name, found.Value));
            }
            else
            {
                problems.Add(new EnvProblem(line.Name, $"({line.Reference}) was not in the set released"));
            }
        }

        if (problems.Count == 0 && !EnvNameRules.TryCheck(variables, out var names))
        {
            problems.Add(new EnvProblem(string.Empty, names));
        }

        return problems.Count > 0
            ? EnvResolved.Refused(released.Project, EnvOutcome.Unusable, problems, released.Profile)
            : EnvResolved.Released(released.Project, variables, released.Profile);
    }

    private static (string? Value, string? Why) Pick(EnvResolved set, EnvReference env)
    {
        if (set.Outcome != EnvOutcome.Resolved)
        {
            return (null, set.Outcome == EnvOutcome.Unusable
                ? $"'{EnvProfileNames.GroupPath(env.Project, env.Profile)}' cannot be used: " +
                  string.Join("; ", set.Problems.Select(problem => $"{EnvResolved.Display(problem.Key)} {problem.Reason}"))
                : set.Refusal);
        }

        return set.Variables.FirstOrDefault(variable => string.Equals(variable.Key, env.Key, StringComparison.Ordinal)) is { } found
            ? (found.Value, null)
            : (null, $"{env.Key} is not in this profile's set");
    }

    private static (string? Value, string? Why) Read(IReadOnlyList<VaultEntry> entries, EntryReference reference, DateTimeOffset now)
    {
        if (ReservedGroups.IsReserved(reference.Entry.GroupPath))
        {
            return (null, "is in a group keypaste keeps for itself");
        }

        var matches = entries.Where(entry => EntryName.Of(entry) == reference.Entry).ToList();

        if (matches.Count != 1)
        {
            return (null, matches.Count == 0 ? "names no entry" : "names more than one entry");
        }

        var found = matches[0];

        if (found.Expires is { } expires && expires <= now)
        {
            return (null, "expired " + expires.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        }

        var value = reference.Field switch
        {
            "username" => found.Username,
            "url" => found.Url,
            "notes" => found.Notes,
            _ => found.Password,
        };

        return value.Length == 0 ? (null, $"has an empty {reference.Field}") : (value, null);
    }
}
