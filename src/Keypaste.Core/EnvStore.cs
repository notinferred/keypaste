using Keypaste.Core.Approval;

namespace Keypaste.Core;

/// <summary>One environment variable read out of a vault.</summary>
/// <param name="Key">The variable's name, which is its field's.</param>
/// <param name="Value">The variable's value.</param>
public sealed record EnvVariable(string Key, string Value);

/// <summary>One profile of a project, as a listing shows it.</summary>
/// <param name="Name">The profile's name.</param>
/// <param name="IsProtected">Whether every release through a session is asked about live (<see cref="EnvProfileNames.IsProtected"/>).</param>
public sealed record EnvProfileInfo(string Name, bool IsProtected);

/// <summary>
/// Writes and removes a project's keys as fields of the entries tagged into its environments, and
/// creates an environment's home entry (D-0413).
/// </summary>
/// <remarks>
/// <para>
/// The writes live here rather than in a front end because the CLI and the desktop write projects
/// in exactly the same shape, and docs/PRODUCT.md law 4.2 does not allow that rule to be written
/// down twice. Reading a project is <see cref="EnvResolution"/>'s and <see cref="ProjectCatalog"/>'s.
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
    /// An existing key is written where it lives, on the tagged entry holding it. A new key must be a field a project releases (<see cref="EnvConvention.IsEnvNamedField"/>,
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

                if (!FieldNameRules.IsWritable(key, out var unwritable))
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
    /// (D-0413).
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
    /// A field leaves its entry as an edit, so the entry's history keeps the value.
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
}
