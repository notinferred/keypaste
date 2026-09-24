using System.Globalization;

namespace Keypaste.Core;

/// <summary>What resolving a project's env set came to.</summary>
public enum EnvOutcome
{
    /// <summary>Every entry is usable, and the set may be released.</summary>
    Resolved = 0,

    /// <summary>The vault has no group for the project.</summary>
    NoProject = 1,

    /// <summary>At least one entry cannot be released; <see cref="EnvResolved.Problems"/> names each.</summary>
    Unusable = 2,

    /// <summary>The open vault holds a change its file does not.</summary>
    Unsaved = 3,

    /// <summary>Another program saved the file since the vault read or wrote it.</summary>
    ChangedOnDisk = 4,

    /// <summary>The file could not be read, so it could not be confirmed as unchanged.</summary>
    Unreadable = 5,

    /// <summary>The session the request belonged to is locked or has ended.</summary>
    Locked = 6,

    /// <summary>The person asked did not confirm.</summary>
    Declined = 7,

    /// <summary>The set's names changed while the person was being asked about them.</summary>
    ChangedWhileAsked = 8,

    /// <summary>The request did not come from a connection attached to the session holding the vault.</summary>
    NoSession = 9,

    /// <summary>The set is larger than one reply on the owner's endpoint can carry.</summary>
    TooLarge = 10,

    /// <summary>The request itself could not be asked about, such as a command too long to show whole.</summary>
    Invalid = 11,
}

/// <summary>Why one entry of an env set cannot be released. Never carries its value.</summary>
/// <param name="Key">The entry's title, which is the variable's name.</param>
/// <param name="Reason">What is wrong with it.</param>
public sealed record EnvProblem(string Key, string Reason);

/// <summary>What a person is asked about before a set is released: names, never values.</summary>
/// <param name="Project">The project.</param>
/// <param name="Keys">The variable names, ordinal-sorted.</param>
public sealed record EnvPreview(string Project, IReadOnlyList<string> Keys);

/// <summary>The result of resolving one project's env set.</summary>
public sealed class EnvResolved
{
    private EnvResolved(string project, EnvOutcome outcome, IReadOnlyList<EnvVariable> variables, IReadOnlyList<EnvProblem> problems)
    {
        Project = project;
        Outcome = outcome;
        Variables = variables;
        Problems = problems;
    }

    /// <summary>The project resolved.</summary>
    public string Project { get; }

    /// <summary>What resolving came to.</summary>
    public EnvOutcome Outcome { get; }

    /// <summary>The whole set, ordinal-sorted by name, when <see cref="Outcome"/> is <see cref="EnvOutcome.Resolved"/>; otherwise empty.</summary>
    public IReadOnlyList<EnvVariable> Variables { get; }

    /// <summary>Every entry that cannot be released and why, when <see cref="Outcome"/> is <see cref="EnvOutcome.Unusable"/>; otherwise empty.</summary>
    public IReadOnlyList<EnvProblem> Problems { get; }

    /// <summary>The names a person is asked about.</summary>
    public EnvPreview Preview => new(Project, [.. Variables.Select(variable => variable.Key)]);

    /// <summary>Why nothing was released, in words a front end prefixes with its own verb, or empty when the set was.</summary>
    public string Refusal => Outcome switch
    {
        EnvOutcome.Resolved => string.Empty,
        EnvOutcome.NoProject => $"no env set for '{Project}'",
        EnvOutcome.Unusable => $"'{EnvConvention.GroupPath(Project)}' cannot be used: " +
            string.Join("; ", Problems.Select(problem => $"{Display(problem.Key)} {problem.Reason}")),
        EnvOutcome.Unsaved => "the vault holds a change that has not been saved",
        EnvOutcome.ChangedOnDisk => "another program saved the vault file; reload it before using it",
        EnvOutcome.Unreadable => "the vault file could not be read to confirm it is unchanged",
        EnvOutcome.Locked => "the vault was locked before the set was released",
        EnvOutcome.Declined => "the set was not confirmed",
        EnvOutcome.ChangedWhileAsked => "the set's names changed while it was being confirmed",
        EnvOutcome.NoSession => "the request belongs to no session holding the vault",
        EnvOutcome.TooLarge => "the set is too large to send in one reply",
        _ => "the request could not be asked about",
    };

    /// <summary>A key as a refusal shows it, including one with no title.</summary>
    public static string Display(string key) => key.Length == 0 ? "(untitled entry)" : key;

    internal static EnvResolved Released(string project, IReadOnlyList<EnvVariable> variables) =>
        new(project, EnvOutcome.Resolved, variables, []);

    internal static EnvResolved Refused(string project, EnvOutcome outcome, IReadOnlyList<EnvProblem>? problems = null) =>
        new(project, outcome, [], problems ?? []);
}

/// <summary>
/// Resolves a project's env set for release, whole or not at all: every entry under
/// <c>env/&lt;project&gt;</c> must have a name that can be exported and must not have expired.
/// </summary>
/// <remarks>
/// <para>
/// Reading stays permissive elsewhere (<see cref="EnvStore.Read"/>) so KeePassXC's view of the file
/// is never hidden; this is the moment a value leaves the vault, where a silently incomplete set
/// becomes a program running with the wrong credentials. A deleted entry is not in the set at all:
/// the recycle bin is outside every traversal (D-0248).
/// </para>
/// <para>
/// A refusal names each entry and why, never a value.
/// </para>
/// </remarks>
public static class EnvResolution
{
    /// <summary>Resolves a project from the vault as its file holds it (D-0317).</summary>
    /// <param name="vault">The open vault.</param>
    /// <param name="project">The project name.</param>
    /// <param name="clock">What expiry is judged against.</param>
    /// <returns>The set, or why it cannot be released.</returns>
    public static EnvResolved Resolve(Vault vault, string project, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(clock);

        var state = vault.ReadSaved(out var entries, out var groupPaths);

        return state switch
        {
            SavedRead.Current => Resolve(entries!, groupPaths!, project, clock.GetUtcNow()),
            SavedRead.Unsaved => EnvResolved.Refused(project, EnvOutcome.Unsaved),
            SavedRead.ChangedOnDisk => EnvResolved.Refused(project, EnvOutcome.ChangedOnDisk),
            _ => EnvResolved.Refused(project, EnvOutcome.Unreadable),
        };
    }

    /// <summary>Resolves a project from one read of a vault.</summary>
    /// <param name="entries">Every entry, read together with <paramref name="groupPaths"/>.</param>
    /// <param name="groupPaths">Every group path.</param>
    /// <param name="project">The project name.</param>
    /// <param name="now">What expiry is judged against.</param>
    /// <returns>The set, or why it cannot be released.</returns>
    internal static EnvResolved Resolve(
        IReadOnlyList<VaultEntry> entries,
        IReadOnlyList<string> groupPaths,
        string project,
        DateTimeOffset now)
    {
        var group = EnvConvention.GroupPath(project);

        if (!groupPaths.Contains(group, StringComparer.Ordinal))
        {
            return EnvResolved.Refused(project, EnvOutcome.NoProject);
        }

        var members = entries.Where(entry => string.Equals(entry.GroupPath, group, StringComparison.Ordinal)).ToList();
        var problems = new SortedSet<EnvProblem>(Comparer<EnvProblem>.Create(static (a, b) =>
            string.CompareOrdinal(a.Key, b.Key) is var byKey and not 0 ? byKey : string.CompareOrdinal(a.Reason, b.Reason)));

        foreach (var entry in members)
        {
            if (entry.Title.Length == 0)
            {
                problems.Add(new EnvProblem(entry.Title, "has no title to be its variable name"));
            }
            else if (!EnvConvention.IsValidKey(entry.Title, out var invalid))
            {
                var quoted = $"'{entry.Title}' ";
                problems.Add(new EnvProblem(entry.Title, invalid.StartsWith(quoted, StringComparison.Ordinal) ? invalid[quoted.Length..] : invalid));
            }

            if (entry.Expires is { } expires && expires <= now)
            {
                problems.Add(new EnvProblem(
                    entry.Title,
                    "expired " + expires.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
            }
        }

        foreach (var same in members.GroupBy(entry => entry.Title, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            problems.Add(new EnvProblem(same.Key, "is the name of more than one entry"));
        }

        foreach (var alike in members.Select(entry => entry.Title).Distinct(StringComparer.Ordinal)
            .GroupBy(title => title, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            foreach (var key in alike)
            {
                var others = string.Join(", ", alike.Where(other => !string.Equals(other, key, StringComparison.Ordinal)).Select(other => $"'{other}'"));
                problems.Add(new EnvProblem(key, $"differs only in case from {others}, which Windows treats as one variable"));
            }
        }

        if (problems.Count > 0)
        {
            return EnvResolved.Refused(project, EnvOutcome.Unusable, [.. problems]);
        }

        var variables = members.Select(entry => new EnvVariable(entry.Title, entry.Password)).ToList();
        variables.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));

        return EnvResolved.Released(project, variables);
    }
}
