using System.Globalization;
using Keypaste.Core.Approval;

namespace Keypaste.Core;

/// <summary>What resolving a project's env set came to.</summary>
public enum EnvOutcome
{
    /// <summary>Every entry is usable, and the set may be released.</summary>
    Resolved = 0,

    /// <summary>The vault has no such project: no entry is tagged into it.</summary>
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

    /// <summary>The project exists but no entry is tagged into the environment asked for.</summary>
    NoProfile = 12,

    /// <summary>The token that authorized the request does not cover this set.</summary>
    Unauthorized = 13,
}

/// <summary>Why one variable of an env set cannot be released. Never carries its value.</summary>
/// <param name="Key">The variable's name.</param>
/// <param name="Reason">What is wrong with it, ending with the entries it concerns in parentheses.</param>
public sealed record EnvProblem(string Key, string Reason);

/// <summary>Where one variable released from the vault lives. Holds no value.</summary>
/// <param name="Key">The variable's name as the child gets it.</param>
/// <param name="Entry">The entry holding its value.</param>
/// <param name="Field">The custom field holding it, named as the key.</param>
public sealed record EnvSource(string Key, EntryName Entry, string Field);

/// <summary>What a person is asked about before a set is released: names, never values.</summary>
/// <param name="Project">The project.</param>
/// <param name="Keys">The variable names, ordinal-sorted, or in the order a key subset asked for them.</param>
public sealed record EnvPreview(string Project, IReadOnlyList<string> Keys)
{
    /// <summary>The profile the set belongs to.</summary>
    public string Profile { get; init; } = EnvProfileNames.Default;

    /// <summary>Where each variable's value lives, in <see cref="Keys"/> order, a literal having none; empty for a preview made without a vault.</summary>
    public IReadOnlyList<EnvSource> Sources { get; init; } = [];

    /// <summary>Whether an entry of the set belongs to a protected environment by any of its own tags, so its release is asked live with Allow once only (D-0348, D-0371).</summary>
    public bool RequiresLiveApproval { get; init; }

    /// <summary>Whether another preview names the same keys, from the same entries, under the same protection.</summary>
    /// <param name="other">The other preview.</param>
    /// <returns><see langword="true"/> when a person's answer to one covers the other.</returns>
    public bool SameAs(EnvPreview other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return Keys.SequenceEqual(other.Keys, StringComparer.Ordinal)
            && Sources.SequenceEqual(other.Sources)
            && RequiresLiveApproval == other.RequiresLiveApproval;
    }
}

/// <summary>The result of resolving one project's env set.</summary>
public sealed class EnvResolved
{
    private EnvResolved(
        string project,
        string profile,
        EnvOutcome outcome,
        IReadOnlyList<EnvVariable> variables,
        IReadOnlyList<EnvProblem> problems,
        IReadOnlyList<EnvSource> sources,
        bool requiresLiveApproval)
    {
        Project = project;
        Profile = profile;
        Outcome = outcome;
        Variables = variables;
        Problems = problems;
        Sources = sources;
        RequiresLiveApproval = requiresLiveApproval;
    }

    /// <summary>The project resolved.</summary>
    public string Project { get; }

    /// <summary>The profile resolved.</summary>
    public string Profile { get; }

    /// <summary>What resolving came to.</summary>
    public EnvOutcome Outcome { get; }

    /// <summary>The whole set, ordinal-sorted by name or in the order a key subset asked for, when <see cref="Outcome"/> is <see cref="EnvOutcome.Resolved"/>; otherwise empty.</summary>
    public IReadOnlyList<EnvVariable> Variables { get; }

    /// <summary>Every variable that cannot be released and why, when <see cref="Outcome"/> is <see cref="EnvOutcome.Unusable"/>; otherwise empty.</summary>
    public IReadOnlyList<EnvProblem> Problems { get; }

    /// <summary>Where each variable released from the vault lives, in <see cref="Variables"/> order, a literal having none; empty for a set that came over the owner's endpoint or out of a bundle.</summary>
    public IReadOnlyList<EnvSource> Sources { get; }

    /// <summary>Whether a source entry belongs to a protected environment by any of its own tags (D-0348, D-0371).</summary>
    public bool RequiresLiveApproval { get; }

    /// <summary>The source entries, each once, in the order their first variable comes out.</summary>
    public IReadOnlyList<EntryName> Entries => [.. Sources.Select(source => source.Entry).Distinct()];

    /// <summary>The names a person is asked about.</summary>
    public EnvPreview Preview => new(Project, [.. Variables.Select(variable => variable.Key)])
    {
        Profile = Profile,
        Sources = Sources,
        RequiresLiveApproval = RequiresLiveApproval,
    };

    /// <summary>Why nothing was released, in words a front end prefixes with its own verb, or empty when the set was.</summary>
    public string Refusal => Outcome switch
    {
        EnvOutcome.Resolved => string.Empty,
        EnvOutcome.NoProject => $"no env set for '{Project}'",
        EnvOutcome.NoProfile => $"'{Project}' has no '{Profile}' profile",
        EnvOutcome.Unusable => $"'{Project}/{Profile}' cannot be used: " +
            string.Join("; ", Problems.Select(problem => $"{problem.Key} {problem.Reason}")),
        EnvOutcome.Unsaved => "the vault holds a change that has not been saved",
        EnvOutcome.ChangedOnDisk => "another program saved the vault file; reload it before using it",
        EnvOutcome.Unreadable => "the vault file could not be read to confirm it is unchanged",
        EnvOutcome.Locked => "the vault was locked before the set was released",
        EnvOutcome.Declined => "the set was not confirmed",
        EnvOutcome.ChangedWhileAsked => "the set's names changed while it was being confirmed",
        EnvOutcome.NoSession => "the request belongs to no session holding the vault",
        EnvOutcome.TooLarge => "the set is too large to send in one reply",
        EnvOutcome.Unauthorized => "the token does not authorize this set",
        _ => "the request could not be asked about",
    };

    internal static EnvResolved Released(
        string project,
        IReadOnlyList<EnvVariable> variables,
        string profile = EnvProfileNames.Default,
        IReadOnlyList<EnvSource>? sources = null,
        bool requiresLiveApproval = false) =>
        new(project, profile, EnvOutcome.Resolved, variables, [], sources ?? [], requiresLiveApproval);

    internal static EnvResolved Refused(
        string project, EnvOutcome outcome, IReadOnlyList<EnvProblem>? problems = null, string profile = EnvProfileNames.Default) =>
        new(project, profile, outcome, [], problems ?? [], [], false);
}

/// <summary>One environment's variables as a listing reads them, unjudged.</summary>
/// <param name="Outcome"><see cref="EnvOutcome.Resolved"/>, or <see cref="EnvOutcome.NoProject"/> or <see cref="EnvOutcome.NoProfile"/> when it is not there.</param>
/// <param name="Variables">Its variables, ordinal by key; a key two entries hold is listed twice.</param>
/// <param name="Sources">Where each lives, in <see cref="Variables"/> order.</param>
public sealed record EnvListing(EnvOutcome Outcome, IReadOnlyList<EnvVariable> Variables, IReadOnlyList<EnvSource> Sources);

/// <summary>One variable an environment holds before anything is judged, with the entry it came from.</summary>
/// <param name="Key">The variable's name, which is its field's.</param>
/// <param name="Value">Its value.</param>
/// <param name="Entry">The entry holding it.</param>
/// <param name="Field">The field holding it (<see cref="EnvSource.Field"/>).</param>
internal sealed record EnvMember(string Key, string Value, EnvEntry Entry, string Field)
{
    /// <summary>Where it lives, without its value.</summary>
    public EnvSource Source => new(Key, Entry.Name, Field);

    /// <summary>Its key only, so a log line or a debugger never shows its value.</summary>
    /// <returns>The key.</returns>
    public override string ToString() => Key;
}

/// <summary>
/// Resolves a project's environment for release, whole or not at all. The set is the variable
/// fields of every entry whose own tag puts it in the environment (D-0370); no entry belongs to a
/// project by its group (D-0416).
/// </summary>
/// <remarks>
/// <para>
/// Reading stays permissive elsewhere (<see cref="List"/>) so KeePassXC's view of the file
/// is never hidden; this is the moment a value leaves the vault, where a silently incomplete set
/// becomes a program running with the wrong credentials. A deleted entry is not in the set at all:
/// the recycle bin is outside every traversal (D-0248), and keypaste's own groups hold no project.
/// </para>
/// <para>
/// A refusal names each variable, why and the entries concerned, never a value: a key on two
/// entries, an expired entry, a custom field named like a standard one in another case, and a value
/// holding a KeePass placeholder (<see cref="KeePassPlaceholders"/>). Two keys cannot differ only in
/// case, since a variable's field is named in capitals (<see cref="EnvConvention.IsEnvNamedField"/>).
/// </para>
/// </remarks>
public static class EnvResolution
{
    private const string _notInSet = "is not in this profile's set";

    /// <summary>Resolves a project's default profile from the vault as its file holds it (D-0317).</summary>
    /// <param name="vault">The open vault.</param>
    /// <param name="project">The project name.</param>
    /// <param name="clock">What expiry is judged against.</param>
    /// <returns>The set, or why it cannot be released.</returns>
    public static EnvResolved Resolve(Vault vault, string project, TimeProvider clock) =>
        Resolve(vault, project, EnvProfileNames.Default, clock);

    /// <summary>Resolves one profile of a project from the vault as its file holds it (D-0317).</summary>
    /// <param name="vault">The open vault.</param>
    /// <param name="project">The project name.</param>
    /// <param name="profile">The profile name.</param>
    /// <param name="clock">What expiry is judged against.</param>
    /// <returns>The set, or why it cannot be released.</returns>
    public static EnvResolved Resolve(Vault vault, string project, string profile, TimeProvider clock) =>
        Resolve(vault, project, profile, keys: null, clock);

    /// <summary>Resolves one profile of a project, or only some of its keys, from the vault as its file holds it (D-0317).</summary>
    /// <param name="vault">The open vault.</param>
    /// <param name="project">The project name.</param>
    /// <param name="profile">The profile name.</param>
    /// <param name="keys">The keys to release, in the order they come out, or null for the whole set.</param>
    /// <param name="clock">What expiry is judged against.</param>
    /// <returns>The set, or why it cannot be released.</returns>
    public static EnvResolved Resolve(Vault vault, string project, string profile, IReadOnlyList<string>? keys, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(clock);

        var state = vault.ReadSavedEnv(out var snapshot);

        return state switch
        {
            SavedRead.Current => Resolve(snapshot!, project, profile, keys, clock.GetUtcNow()),
            SavedRead.Unsaved => EnvResolved.Refused(project, EnvOutcome.Unsaved, profile: profile),
            SavedRead.ChangedOnDisk => EnvResolved.Refused(project, EnvOutcome.ChangedOnDisk, profile: profile),
            _ => EnvResolved.Refused(project, EnvOutcome.Unreadable, profile: profile),
        };
    }

    /// <summary>Resolves one profile of a project, or only some of its keys, from one read of a vault.</summary>
    /// <param name="snapshot">The vault's entries, tags and group paths, read together.</param>
    /// <param name="project">The project name.</param>
    /// <param name="profile">The profile name.</param>
    /// <param name="keys">The keys to release, in the order they come out, or null for the whole set.</param>
    /// <param name="now">What expiry is judged against.</param>
    /// <returns>The set, or why it cannot be released.</returns>
    /// <remarks>A key subset is taken before any entry is judged, so an unusable key nobody asked for refuses nothing.</remarks>
    internal static EnvResolved Resolve(EnvSnapshot snapshot, string project, string profile, IReadOnlyList<string>? keys, DateTimeOffset now)
    {
        var members = Members(snapshot, project, profile, out var exists);

        if (!exists)
        {
            return EnvResolved.Refused(project, ProjectExists(snapshot, project) ? EnvOutcome.NoProfile : EnvOutcome.NoProject, profile: profile);
        }

        var problems = new SortedSet<EnvProblem>(Comparer<EnvProblem>.Create(static (a, b) =>
            string.CompareOrdinal(a.Key, b.Key) is var byKey and not 0 ? byKey : string.CompareOrdinal(a.Reason, b.Reason)));

        if (keys is not null)
        {
            members = [.. members.Where(member => keys.Contains(member.Key, StringComparer.Ordinal))];

            foreach (var missing in keys.Where(key => !members.Any(member => string.Equals(member.Key, key, StringComparison.Ordinal))))
            {
                problems.Add(new EnvProblem(missing, _notInSet));
            }
        }

        foreach (var problem in Judge(members, now))
        {
            problems.Add(problem);
        }

        if (problems.Count > 0)
        {
            return EnvResolved.Refused(project, EnvOutcome.Unusable, [.. problems], profile);
        }

        if (keys is null)
        {
            members.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));
        }
        else
        {
            members = [.. keys.Distinct(StringComparer.Ordinal)
                .Select(key => members.Single(member => string.Equals(member.Key, key, StringComparison.Ordinal)))];
        }

        return EnvResolved.Released(
            project,
            [.. members.Select(member => new EnvVariable(member.Key, member.Value))],
            profile,
            [.. members.Select(member => member.Source)],
            EnvProfileNames.IsProtected(profile) || members.Any(member => RequiresLiveApproval(member.Entry)));
    }

    /// <summary>One environment's variables as the vault holds them now, saved or not, unjudged: what a listing or an export shows, KeePassXC's view never hidden.</summary>
    /// <param name="vault">The open vault.</param>
    /// <param name="project">The project name.</param>
    /// <param name="profile">The environment's name.</param>
    /// <returns>Its variables ordinal by key, then by entry, each with its source; or why there are none.</returns>
    public static EnvListing List(Vault vault, string project, string profile)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(profile);

        var snapshot = vault.ReadEnvSnapshot();
        var members = Members(snapshot, project, profile, out var exists);

        if (!exists)
        {
            return new EnvListing(ProjectExists(snapshot, project) ? EnvOutcome.NoProfile : EnvOutcome.NoProject, [], []);
        }

        members = [.. members
            .OrderBy(member => member.Key, StringComparer.Ordinal)
            .ThenBy(member => ApprovalPrompt.Shown(member.Entry.Name), StringComparer.Ordinal)];

        return new EnvListing(
            EnvOutcome.Resolved,
            [.. members.Select(member => new EnvVariable(member.Key, member.Value))],
            [.. members.Select(member => member.Source)]);
    }

    /// <summary>Every variable one environment holds, unjudged, and whether the environment exists at all.</summary>
    /// <param name="snapshot">The vault's entries, tags and group paths.</param>
    /// <param name="project">The project name.</param>
    /// <param name="profile">The environment's name.</param>
    /// <param name="exists">Whether an entry is tagged into it, even with no variables.</param>
    /// <returns>Each tagged entry's variable fields, in the vault's order.</returns>
    internal static List<EnvMember> Members(EnvSnapshot snapshot, string project, string profile, out bool exists)
    {
        var tagged = snapshot.Entries
            .Where(entry => !ReservedGroups.IsReserved(entry.Entry.GroupPath) && entry.IsTaggedInto(project, profile))
            .ToList();

        exists = tagged.Count > 0;
        return [.. tagged.SelectMany(entry => entry.Fields.Select(field => new EnvMember(field.Key, field.Value, entry, field.Key)))];
    }

    /// <summary>Whether a vault holds a project: an entry tagged into any of its environments.</summary>
    internal static bool ProjectExists(EnvSnapshot snapshot, string project) =>
        snapshot.Entries.Any(entry => !ReservedGroups.IsReserved(entry.Entry.GroupPath)
            && entry.Tags.Select(ProjectTag.Read).Any(tag => tag.Kind == ProjectTagKind.Member && string.Equals(tag.Project, project, StringComparison.Ordinal)));

    /// <summary>Whether a variable is a custom field named like a standard one in another case, which keypaste never releases (D-0388).</summary>
    /// <param name="source">Where the variable lives.</param>
    /// <returns><see langword="true"/> for such a field.</returns>
    public static bool IsNamedLikeStandard(EnvSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return FieldNameRules.IsStandard(source.Key);
    }

    /// <summary>Every reason each variable cannot be released, naming the entries concerned.</summary>
    internal static IEnumerable<EnvProblem> Judge(IReadOnlyList<EnvMember> members, DateTimeOffset now)
    {
        foreach (var member in members)
        {
            var at = Where([member]);

            if (IsNamedLikeStandard(member.Source))
            {
                yield return new EnvProblem(member.Key, $"is a custom field named like a standard one, which keypaste never releases ({at})");
            }

            if (member.Entry.Entry.Expires is { } expires && expires <= now)
            {
                yield return new EnvProblem(
                    member.Key,
                    $"expired {expires.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture)} ({at})");
            }

            if (KeePassPlaceholders.Find(member.Value) is { } placeholder)
            {
                yield return new EnvProblem(member.Key, $"holds the KeePass placeholder {placeholder}, which keypaste does not resolve ({at})");
            }
        }

        foreach (var same in members.GroupBy(member => member.Key, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            yield return new EnvProblem(same.Key, $"is on more than one entry ({Where([.. same])})");
        }
    }

    /// <summary>Whether releasing a member's value must be asked about live, by any of its entry's own tags.</summary>
    internal static bool RequiresLiveApproval(EnvEntry entry) => EnvProfileNames.RequiresLiveApproval(entry.Tags);

    /// <summary>The entries holding some members, as a prompt shows them, in ordinal order; two entries sharing a path are both named.</summary>
    private static string Where(IReadOnlyList<EnvMember> members) =>
        string.Join(", ", members.Select(member => ApprovalPrompt.Shown(member.Entry.Name)).Order(StringComparer.Ordinal));
}
