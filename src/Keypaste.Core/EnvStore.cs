using Keypaste.Core.Approval;

namespace Keypaste.Core;

/// <summary>One environment variable read out of a vault.</summary>
/// <param name="Key">The variable's name: a tagged entry's field name, or a legacy entry's title.</param>
/// <param name="Value">The variable's value.</param>
public sealed record EnvVariable(string Key, string Value)
{
    /// <summary>
    /// Whether <see cref="Key"/> is a name that can actually be exported to a child process.
    /// </summary>
    /// <remarks>
    /// False only for variables written by something other than keypaste, since
    /// <see cref="EnvStore.Plan"/> refuses to create one. Reading them anyway is deliberate:
    /// hiding a variable that KeePassXC displays would make the two tools disagree about the
    /// contents of one file (docs/PRODUCT.md law 4.6).
    /// </remarks>
    public bool IsUsableName => EnvConvention.IsValidKey(Key, out _);
}

/// <summary>One profile of a project, as a listing shows it.</summary>
/// <param name="Name">The profile's name.</param>
/// <param name="IsProtected">Whether every release through a session is asked about live (<see cref="EnvProfileNames.IsProtected"/>).</param>
public sealed record EnvProfileInfo(string Name, bool IsProtected);

/// <summary>
/// Reads and writes environment-variable sets in a <see cref="Vault"/>, following
/// <see cref="EnvConvention"/>.
/// </summary>
/// <remarks>
/// <para>
/// The convention lives here rather than in the CLI because the MCP bridge and the GUI will store
/// env sets in exactly the same shape, and docs/PRODUCT.md law 4.3 does not allow that rule to be written
/// down three times. Nothing above this type should know that the group is called <c>env</c>.
/// </para>
/// <para>
/// The governing rule throughout is <b>permissive on read, strict on write</b>. Anything KeePassXC
/// can put in the file is listed; only what keypaste itself creates is validated.
/// </para>
/// <para>
/// Deliberately not <see cref="IDisposable"/>: it borrows a vault rather than owning one, so it
/// adds no lifetime of its own to manage.
/// </para>
/// </remarks>
/// <param name="vault">The open vault to work in. The caller retains ownership of it.</param>
public sealed class EnvStore(Vault vault)
{
    private readonly Vault _vault = vault ?? throw new ArgumentNullException(nameof(vault));

    /// <summary>Lists the projects that have an <c>env/&lt;project&gt;</c> group, ordinal-sorted.</summary>
    /// <returns>The project names.</returns>
    /// <exception cref="ObjectDisposedException">The vault has been disposed.</exception>
    /// <remarks>
    /// Only the immediate children of the <c>env</c> group count as projects. A group nested more
    /// deeply is not reported, because no environment reads its entries either.
    /// </remarks>
    public IReadOnlyList<string> Projects()
    {
        string prefix = EnvConvention.RootGroup + "/";
        List<string> projects = [];

        foreach (string groupPath in _vault.ReadGroupPaths())
        {
            if (!groupPath.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            string name = groupPath[prefix.Length..];
            if (name.Length > 0 && !name.Contains('/'))
            {
                projects.Add(name);
            }
        }

        projects.Sort(StringComparer.Ordinal);
        return projects;
    }

    /// <summary>Whether the given project has a group, even an empty one.</summary>
    /// <param name="project">The project name.</param>
    /// <returns><see langword="true"/> if the group exists.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The vault has been disposed.</exception>
    /// <remarks>
    /// Distinguishes "this project has no variables" from "there is no such project", which are
    /// different answers to <c>keypaste env ls</c> and deserve different exit codes.
    /// </remarks>
    public bool ProjectExists(string project)
    {
        string groupPath = EnvConvention.GroupPath(project);

        foreach (string candidate in _vault.ReadGroupPaths())
        {
            if (string.Equals(candidate, groupPath, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The profiles a project has: <c>dev</c> first, then the others ordinal-sorted, protected ones last.</summary>
    /// <param name="project">The project name.</param>
    /// <returns>The profiles, empty when the project does not exist.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The vault has been disposed.</exception>
    /// <remarks>
    /// A subgroup keypaste would not resolve is left out here and named by <see cref="ProfileProblems"/>,
    /// so a listing never offers a profile that <c>run</c> would refuse.
    /// </remarks>
    public IReadOnlyList<EnvProfileInfo> Profiles(string project)
    {
        if (!ProjectExists(project))
        {
            return [];
        }

        var others = Subgroups(project)
            .Where(name => !string.Equals(name, EnvProfileNames.Default, StringComparison.Ordinal) && EnvProfileNames.IsValid(name, out _))
            .Select(name => new EnvProfileInfo(name, EnvProfileNames.IsProtected(name)))
            .OrderBy(profile => profile.IsProtected)
            .ThenBy(profile => profile.Name, StringComparer.Ordinal);

        return [new EnvProfileInfo(EnvProfileNames.Default, false), .. others];
    }

    /// <summary>The subgroups of a project that are never read, each with why, in keypaste's words.</summary>
    /// <param name="project">The project name.</param>
    /// <returns>One sentence per ignored subgroup, ordinal by name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The vault has been disposed.</exception>
    public IReadOnlyList<string> ProfileProblems(string project)
    {
        List<string> problems = [];

        foreach (var name in Subgroups(project).Order(StringComparer.Ordinal))
        {
            var group = EnvConvention.GroupPath(project) + "/" + name;

            if (string.Equals(name, EnvProfileNames.Default, StringComparison.Ordinal))
            {
                problems.Add($"'{group}' is ignored: the {EnvProfileNames.Default} profile is the project group itself; move its entries up");
            }
            else if (!EnvProfileNames.IsValid(name, out var invalid))
            {
                problems.Add($"'{group}' is ignored: {invalid}");
            }
        }

        return problems;
    }

    /// <summary>Whether a project has a profile keypaste resolves, even an empty one.</summary>
    /// <param name="project">The project name.</param>
    /// <param name="profile">The profile name.</param>
    /// <returns><see langword="true"/> if the profile's group exists and its name is one keypaste reads.</returns>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="ObjectDisposedException">The vault has been disposed.</exception>
    public bool ProfileExists(string project, string profile) =>
        GroupOf(project, profile) is { } group && _vault.ReadGroupPaths().Contains(group, StringComparer.Ordinal);

    /// <summary>The title of the <c>dev</c> environment's home entry.</summary>
    public const string HomeTitle = ".env";

    /// <summary>
    /// Where an environment's new keys go when no entry is named: <c>env/&lt;project&gt;/.env</c> for
    /// <c>dev</c> and <c>env/&lt;project&gt;/.env.&lt;environment&gt;</c> for another, tagged into it (D-0413).
    /// </summary>
    /// <param name="project">The project name.</param>
    /// <param name="environment">The environment's name.</param>
    /// <returns>The home entry's name, whether or not it exists.</returns>
    public static EntryName HomeEntry(string project, string environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return new EntryName(
            EnvConvention.GroupPath(project),
            string.Equals(environment, EnvProfileNames.Default, StringComparison.Ordinal) ? HomeTitle : HomeTitle + "." + environment);
    }

    /// <summary>Plans writing keys to one environment of a project, checking everything before anything is written.</summary>
    /// <param name="project">The project name.</param>
    /// <param name="environment">The environment's name.</param>
    /// <param name="variables">The keys and their values, each key once.</param>
    /// <param name="entry">The entry a new key goes on, which must be tagged into the environment, and the one written when several hold a key; null for the home entry.</param>
    /// <returns>What writing would do, or why it cannot, naming each key and entry concerned.</returns>
    /// <remarks>
    /// An existing key is written where it lives: on the tagged entry holding it, or in place as a
    /// legacy variable. A new key must be a field a project releases (<see cref="EnvConvention.IsEnvNamedField"/>,
    /// and no standard name), and goes on <paramref name="entry"/> or the home entry, which is created
    /// tagged when missing. A key several entries hold, unless <paramref name="entry"/> is one of them,
    /// and a key differing from another only in case, are refused.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The vault has been disposed.</exception>
    public EnvWritePlan Plan(string project, string environment, IReadOnlyList<KeyValuePair<string, string>> variables, EntryName? entry = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(variables);

        if (!ProjectTag.TryFor(project, environment, out var tag, out var invalid))
        {
            return EnvWritePlan.Refused(project, environment, invalid);
        }

        var snapshot = _vault.ReadEnvSnapshot();
        var members = EnvResolution.Members(snapshot, project, environment, out _);
        var target = entry ?? HomeEntry(project, environment);
        var where = $"'{project}/{environment}'";
        List<string> refusals = [];
        List<EnvKeyWrite> keys = [];
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        string? targetProblem = null;
        var targetJudged = false;
        var createsHome = false;

        foreach (var (key, value) in variables)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(value);

            values[key] = value;
            var holding = members.Where(member => string.Equals(member.Key, key, StringComparison.Ordinal)).ToList();

            if (members.FirstOrDefault(member => string.Equals(member.Key, key, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(member.Key, key, StringComparison.Ordinal)) is { } alike)
            {
                refusals.Add($"{where} already has '{alike.Key}', which differs from '{key}' only in case ({Where([alike])})");
                continue;
            }

            if (entry is not null && holding.Count > 1 && holding.Where(member => member.Entry.Name == entry).ToList() is { Count: > 0 } named)
            {
                holding = named;
            }

            if (holding.Count > 1)
            {
                refusals.Add($"{key} is on more than one entry ({Where(holding)}); name the one to write");
                continue;
            }

            if (holding.Count == 1)
            {
                var member = holding[0];

                if (!string.Equals(member.Field, EnvSource.LegacyField, StringComparison.Ordinal) && !FieldNameRules.IsWritable(key, out var unwritable))
                {
                    refusals.Add($"{unwritable} ({Where([member])})");
                    continue;
                }

                keys.Add(new EnvKeyWrite(
                    key,
                    string.Equals(member.Value, value, StringComparison.Ordinal) ? EnvWriteChange.Unchanged : EnvWriteChange.Replaces,
                    member.Entry.Name,
                    member.Field));
                continue;
            }

            if (!IsNewFieldName(key, out var badName))
            {
                refusals.Add(badName);
                continue;
            }

            if (!targetJudged)
            {
                targetJudged = true;
                targetProblem = JudgeTarget(snapshot, target, explicitly: entry is not null, project, environment, tag, out createsHome);

                if (targetProblem is not null)
                {
                    refusals.Add(targetProblem);
                }
            }

            if (targetProblem is null)
            {
                keys.Add(new EnvKeyWrite(key, EnvWriteChange.New, target, key));
            }
        }

        if (refusals.Count > 0)
        {
            return EnvWritePlan.Refused(project, environment, string.Join("; ", refusals));
        }

        return new EnvWritePlan(
            project,
            environment,
            [.. keys.OrderBy(key => key.Key, StringComparer.Ordinal)],
            values,
            tag,
            createsHome,
            null);
    }

    /// <summary>Writes every new and replaced key of a plan, one revision per entry. The caller must <see cref="Vault.Save"/> to persist it.</summary>
    /// <param name="plan">A plan made against this vault, with no refusal.</param>
    /// <param name="rejection">What could not be written, when this returns false.</param>
    /// <returns>Whether every key was written. When false, the caller must not save.</returns>
    /// <remarks>
    /// The fields an entry gains or changes are written together, so the entry's history gains one
    /// item for the whole plan, and a created home entry gains none. Every value is written protected
    /// (D-0413). A legacy variable is updated in place, its title being the key.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The vault has been disposed.</exception>
    public bool TryApply(EnvWritePlan plan, out string rejection)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Refusal is { } refusal)
        {
            rejection = refusal;
            return false;
        }

        foreach (var writes in plan.ToWrite)
        {
            if (writes.All(write => write.IsLegacy))
            {
                foreach (var write in writes)
                {
                    if (_vault.Find(write.Entry) is not { } current || !_vault.UpdateEntry(current with { Password = plan.Value(write.Key) }))
                    {
                        rejection = $"{ApprovalPrompt.Shown(write.Entry)} could not be found to update";
                        return false;
                    }
                }

                continue;
            }

            List<FieldWrite> fields = [.. writes.Select(write => new FieldWrite(write.Key, plan.Value(write.Key), Protect: true))];

            if (plan.CreatesHome && writes.Key == plan.Home)
            {
                _vault.CreateHomeEntry(writes.Key, plan.Tag, fields);
            }
            else if (!_vault.SetFields(writes.Key, fields))
            {
                rejection = $"{ApprovalPrompt.Shown(writes.Key)} could not be found to write";
                return false;
            }
        }

        rejection = string.Empty;
        return true;
    }

    /// <summary>Sets one key of an environment where it lives, or adds it as a field. The caller must <see cref="Vault.Save"/> to persist it.</summary>
    /// <param name="project">The project name.</param>
    /// <param name="environment">The environment's name.</param>
    /// <param name="key">The variable's name.</param>
    /// <param name="value">The value. An empty value is allowed, matching <c>KEY=</c> in a .env file.</param>
    /// <param name="entry">The entry a new key goes on, and the one written when several hold the key; null for the home entry.</param>
    /// <returns>The plan as written: its one key says what changed and where, and <see cref="EnvWritePlan.Refusal"/> why nothing did.</returns>
    /// <exception cref="ObjectDisposedException">The vault has been disposed.</exception>
    /// <exception cref="VaultException">The vault refused the write, and nothing was changed.</exception>
    public EnvWritePlan Set(string project, string environment, string key, string value, EntryName? entry = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        var plan = Plan(project, environment, [new(key, value)], entry);

        return plan.WritesAnything && !TryApply(plan, out var rejection)
            ? EnvWritePlan.Refused(project, environment, rejection)
            : plan;
    }

    /// <summary>Removes one key of an environment from where it lives. The caller must <see cref="Vault.Save"/> to persist it.</summary>
    /// <param name="project">The project name.</param>
    /// <param name="environment">The environment's name.</param>
    /// <param name="key">The variable's name, matched exactly.</param>
    /// <param name="entry">The entry to remove it from, when several hold it; null for the one that does.</param>
    /// <returns>What happened and where.</returns>
    /// <remarks>
    /// A field leaves its entry as an edit, so the entry's history keeps the value; a legacy variable's
    /// entry goes to the recycle bin as before. The name is not validated: a variable KeePassXC wrote
    /// under a name keypaste would refuse still has to be removable. A legacy variable is addressed by
    /// group and title, never by the two joined.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The vault has been disposed.</exception>
    public EnvRemoval Remove(string project, string environment, string key, EntryName? entry = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(key);

        if (!EnvProfileNames.IsValid(environment, out _))
        {
            return new EnvRemoval(EnvRemoveOutcome.NothingMatched, null, string.Empty);
        }

        var holding = EnvResolution.Members(_vault.ReadEnvSnapshot(), project, environment, out _)
            .Where(member => string.Equals(member.Key, key, StringComparison.Ordinal) && (entry is null || member.Entry.Name == entry))
            .ToList();

        if (holding.Count == 0)
        {
            return new EnvRemoval(EnvRemoveOutcome.NothingMatched, null, string.Empty);
        }

        if (holding.Count > 1)
        {
            return new EnvRemoval(EnvRemoveOutcome.Ambiguous, null, $"{key} is on more than one entry ({Where(holding)}); name the one to remove from");
        }

        var source = holding[0].Source;

        if (string.Equals(source.Field, EnvSource.LegacyField, StringComparison.Ordinal))
        {
            return _vault.RemoveEntry(source.Entry) switch
            {
                DeletionOutcome.Recycled => new EnvRemoval(EnvRemoveOutcome.Recycled, source, string.Empty),
                DeletionOutcome.NothingMatched => new EnvRemoval(EnvRemoveOutcome.NothingMatched, null, string.Empty),
                _ => new EnvRemoval(EnvRemoveOutcome.Deleted, source, string.Empty),
            };
        }

        if (!FieldNameRules.IsWritable(key, out var unwritable))
        {
            return new EnvRemoval(EnvRemoveOutcome.Refused, source, $"{unwritable} ({ApprovalPrompt.Shown(source.Entry)})");
        }

        return _vault.RemoveField(source.Entry, key)
            ? new EnvRemoval(EnvRemoveOutcome.FieldRemoved, source, string.Empty)
            : new EnvRemoval(EnvRemoveOutcome.NothingMatched, null, string.Empty);
    }

    /// <summary>Whether a key may become a new field of a project, and if not, why.</summary>
    /// <param name="key">The variable's name.</param>
    /// <param name="error">Why not, otherwise empty.</param>
    /// <returns>Whether a project would release a field of that name (D-0388).</returns>
    public static bool IsNewFieldName(string key, out string error)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (FieldNameRules.IsStandard(key))
        {
            error = $"'{key}' is named like one of an entry's standard fields, which no project releases as a variable";
            return false;
        }

        if (!EnvConvention.IsEnvNamedField(key))
        {
            error = $"'{key}' cannot be a project's variable: a variable is a field named with capital letters, digits and '_', starting with a letter and not with KPEX_, KPXC_ or KP2A_";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>Why a new key cannot go on the target, or null; and whether the target is a home entry to create.</summary>
    private static string? JudgeTarget(EnvSnapshot snapshot, EntryName target, bool explicitly, string project, string environment, string tag, out bool createsHome)
    {
        createsHome = false;
        var shown = ApprovalPrompt.Shown(target);
        var found = snapshot.Entries
            .Where(entry => !ReservedGroups.IsReserved(entry.Entry.GroupPath) && entry.Name == target)
            .ToList();

        if (found.Count > 1)
        {
            return $"{shown} names more than one entry; rename one";
        }

        if (found.Count == 0)
        {
            createsHome = !explicitly;
            return explicitly ? $"there is no entry {shown}" : null;
        }

        if (found[0].IsTaggedInto(project, environment))
        {
            return null;
        }

        return explicitly
            ? $"{shown} is not in '{project}/{environment}'; tag it into the environment first"
            : $"{shown} is not tagged {tag}; tag it into '{project}/{environment}' or rename it";
    }

    /// <summary>The entries holding some members, as a prompt shows them, in ordinal order.</summary>
    private static string Where(IEnumerable<EnvMember> members) =>
        string.Join(", ", members.Select(member => ApprovalPrompt.Shown(member.Entry.Name)).Order(StringComparer.Ordinal));

    /// <summary>The group a profile lives in, or null for a name keypaste never reads.</summary>
    private static string? GroupOf(string project, string profile)
    {
        ArgumentNullException.ThrowIfNull(project);

        return EnvProfileNames.IsValid(profile, out _) ? EnvProfileNames.GroupPath(project, profile) : null;
    }

    /// <summary>The names of a project's direct subgroups, whatever they are called.</summary>
    private IEnumerable<string> Subgroups(string project)
    {
        var prefix = EnvConvention.GroupPath(project) + "/";

        return _vault.ReadGroupPaths()
            .Where(path => path.StartsWith(prefix, StringComparison.Ordinal))
            .Select(path => path[prefix.Length..])
            .Where(name => name.Length > 0 && !name.Contains('/'));
    }
}
