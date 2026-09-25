using Keypaste.Core.Internal;

namespace Keypaste.Core.Import;

/// <summary>An unlocked KDBX file to copy from, and the plan for where its groups land.</summary>
/// <remarks>
/// <para>
/// Its key lives here and nowhere else, and goes when this is disposed. Nothing reachable from it
/// writes the file.
/// </para>
/// <para>
/// Every top-level group is a row. An <c>env</c> group is split into one row per project, holding
/// only the project group's own entries, and one per profile subgroup, so each env set can land
/// beside or apart from the target's. The recycle bin, wherever it is, and <see cref="ReservedGroups.Root"/>
/// are never rows: tokens and share records belong to the vault that made them.
/// </para>
/// </remarks>
public sealed class ImportSource : IDisposable
{
    private readonly KeePassInterop _interop;
    private readonly List<Unit> _units = [];
    private readonly List<ImportSkip> _skipped = [];
    private bool _disposed;

    internal ImportSource(KeePassInterop interop, KdbxProbe probe)
    {
        _interop = interop;
        Probe = probe;
        KeyFactors = interop.KeyFactors;
        Read(interop.DescribeForImport());
    }

    /// <summary>What the file's header said.</summary>
    public KdbxProbe Probe { get; }

    /// <summary>How many entries a whole import copies: all but the recycle bin's and reserved ones.</summary>
    public int EntryCount { get; private set; }

    /// <summary>How many <c>env/&lt;project&gt;</c> groups the file holds.</summary>
    public int ProjectCount { get; private set; }

    /// <summary>What unlocked it: <c>password</c>, <c>key file</c>, or both.</summary>
    public IReadOnlyList<string> KeyFactors { get; }

    /// <summary>How many entries are never copied.</summary>
    public int SkippedEntries => _skipped.Sum(skip => skip.EntryCount);

    /// <summary>The groups never copied, and how many entries each holds.</summary>
    public IReadOnlyList<ImportSkip> Skipped => _skipped;

    internal KeePassInterop Interop
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _interop;
        }
    }

    /// <summary>Where every group lands unless the person says otherwise.</summary>
    /// <param name="target">The vault imported into.</param>
    /// <param name="into">
    /// The group everything is collected under; null for the file's name without leading dots,
    /// with <c> (2)</c>, <c> (3)</c> … while that is taken.
    /// </param>
    /// <returns>
    /// Every top-level group at <c>&lt;into&gt;/&lt;name&gt;</c>, the root's own entries at
    /// <c>&lt;into&gt;</c>, and each env set at its own path when it is a valid set new to the
    /// target; a project the target already has, or one whose set <see cref="Check"/> would block,
    /// goes with all its profiles to <c>&lt;into&gt;/env/…</c> as plain groups.
    /// </returns>
    public ImportPlan DefaultPlan(Vault target, string? into)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(target);

        var groups = target.ReadGroupPaths().ToHashSet(StringComparer.Ordinal);
        var intoGroup = into ?? DefaultInto(groups);
        var titles = Titles(target);
        var rerouted = new Dictionary<string, string>(StringComparer.Ordinal);
        List<ImportRow> rows = [];

        foreach (var (unit, index) in _units.Select((unit, index) => (unit, index)))
        {
            var reason = unit.IsRootEntries || unit.Project is not { } project
                ? null
                : groups.Contains(EnvConvention.GroupPath(project))
                    ? $"{EnvConvention.GroupPath(project)} exists here"
                    : rerouted.TryGetValue(project, out var earlier)
                        ? earlier
                        : RefuseEnvSet(unit, unit.SourceGroup, titles.GetValueOrDefault(unit.SourceGroup) ?? []) is { } refused
                            ? $"not a valid env set: {refused}"
                            : null;

            var destination = unit.IsRootEntries
                ? intoGroup
                : unit.Project is not null && reason is null ? unit.SourceGroup : Unreserved(intoGroup, unit.SourceGroup, groups, rows, ref reason);

            // A project that moves takes its profiles with it; a profile that moves goes alone.
            if (reason is not null && unit.Project is { } moved && string.Equals(unit.SourceGroup, EnvConvention.GroupPath(moved), StringComparison.Ordinal))
            {
                rerouted.TryAdd(moved, reason);
            }

            foreach (var (relative, list) in unit.Groups)
            {
                At(titles, Below(destination, relative)).AddRange(list);
            }

            rows.Add(new ImportRow(index, unit.SourceGroup, unit.EntryCount, destination, true, unit.IsRootEntries) { Rerouted = reason });
        }

        return new ImportPlan(rows, intoGroup);
    }

    /// <summary>
    /// <paramref name="sourceGroup"/> under <paramref name="into"/>, with any segment spelled like the
    /// recycle bin renamed, since a group of that name is refused anywhere in a vault.
    /// </summary>
    private static string Unreserved(string into, string sourceGroup, HashSet<string> groups, List<ImportRow> rows, ref string? reason)
    {
        var path = into;

        foreach (var segment in sourceGroup.Split('/'))
        {
            var name = segment;

            if (string.Equals(segment, KeePassInterop.RecycleBinName, StringComparison.Ordinal))
            {
                var renamed = $"{KeePassInterop.RecycleBinName} (imported)";
                name = renamed;

                for (var suffix = 2; Taken(path + "/" + name); suffix++)
                {
                    name = $"{renamed} ({suffix})";
                }

                reason ??= $"{KeePassInterop.RecycleBinName} is the recycle bin's name";
            }

            path = path + "/" + name;
        }

        return path;

        bool Taken(string candidate) =>
            groups.Contains(candidate) || rows.Any(row => string.Equals(row.Destination, candidate, StringComparison.Ordinal));
    }

    private static string Below(string destination, string relative) =>
        relative.Length == 0 ? destination : destination + "/" + relative;

    private static List<string> At(Dictionary<string, List<string>> titles, string path)
    {
        if (!titles.TryGetValue(path, out var there))
        {
            titles[path] = there = [];
        }

        return there;
    }

    private static Dictionary<string, List<string>> Titles(Vault target) =>
        target.Search(string.Empty).GroupBy(match => match.Name.GroupPath, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(match => match.Name.Title).ToList(), StringComparer.Ordinal);

    /// <summary>What stops a plan, or is worth saying about it, against the vault it would change.</summary>
    /// <param name="target">The vault imported into.</param>
    /// <param name="plan">The plan, as the person left it.</param>
    /// <returns>Every problem with an included row, in row order; empty when there is nothing to say.</returns>
    public IReadOnlyList<ImportProblem> Check(Vault target, ImportPlan plan)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(plan);

        List<ImportProblem> problems = [];

        if (PathIdentity.SameFile(Probe.Path, target.Path))
        {
            problems.Add(new ImportProblem(-1, "that is the vault you are importing into", Blocks: true));
        }

        var groupCounts = target.ReadGroupPaths().CountBy(path => path, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var titles = Titles(target);

        foreach (var row in plan.Rows)
        {
            if (!row.Include)
            {
                continue;
            }

            if (row.Index < 0 || row.Index >= _units.Count || !string.Equals(_units[row.Index].SourceGroup, row.SourceGroup, StringComparison.Ordinal))
            {
                problems.Add(new ImportProblem(row.Index, "that row is not a group of this file", Blocks: true));
                continue;
            }

            var unit = _units[row.Index];
            var destination = row.Destination;

            if (RefuseDestination(destination, groupCounts) is { } refused)
            {
                problems.Add(new ImportProblem(row.Index, refused, Blocks: true));
                continue;
            }

            if (IsEnv(destination))
            {
                var there = At(titles, destination);

                if (RefuseEnvSet(unit, destination, there) is { } env)
                {
                    problems.Add(new ImportProblem(row.Index, env, Blocks: true));
                }

                there.AddRange(unit.Titles);
                continue;
            }

            var duplicates = 0;
            var inSubgroups = false;

            foreach (var (relative, list) in unit.Groups)
            {
                var at = At(titles, Below(destination, relative));
                var repeated = list.Count(title => at.Contains(title, StringComparer.Ordinal))
                    + list.Count - list.Distinct(StringComparer.Ordinal).Count();

                duplicates += repeated;
                inSubgroups |= repeated > 0 && relative.Length > 0;
                at.AddRange(list);
            }

            if (duplicates > 0)
            {
                var where = inSubgroups ? $"{destination} and its subgroups" : destination;
                problems.Add(new ImportProblem(
                    row.Index,
                    duplicates == 1
                        ? $"1 title in {where} names another entry too; both are kept"
                        : $"{duplicates} titles in {where} name another entry too; all are kept",
                    Blocks: false));
            }
        }

        return problems;
    }

    /// <summary>Copies every included row into <paramref name="target"/>, or nothing. Does not save.</summary>
    /// <param name="target">The vault imported into.</param>
    /// <param name="plan">The plan, as the person confirmed it.</param>
    /// <returns>What was copied.</returns>
    /// <exception cref="VaultException"><see cref="Check"/> finds a problem that blocks; nothing was copied.</exception>
    public ImportResult ApplyTo(Vault target, ImportPlan plan)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(plan);

        if (Check(target, plan).FirstOrDefault(problem => problem.Blocks) is { } blocked)
        {
            throw new VaultException($"Nothing was imported: {blocked.Message}.");
        }

        return target.Import(
            this,
            [.. plan.Rows.Where(row => row.Include)
                .Select(row => new ImportPiece(_units[row.Index].Id, _units[row.Index].Scope, row.Destination))]);
    }

    /// <summary>Releases the source's key and decrypted contents.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _interop.Dispose();
    }

    private void Read(ImportNode root)
    {
        if (root.Titles.Count > 0)
        {
            Add(root, ImportScope.EntriesOnly, string.Empty, isRoot: true);
        }

        foreach (var top in root.Children)
        {
            if (top.IsRecycleBin || ReservedGroups.IsReserved(top.Name))
            {
                _skipped.Add(new ImportSkip(top.Name, Raw(top)));
            }
            else if (string.Equals(top.Name, EnvConvention.RootGroup, StringComparison.Ordinal))
            {
                ReadEnv(top);
            }
            else
            {
                Add(top, ImportScope.WholeGroup, top.Name);
            }
        }

        EntryCount = _units.Sum(unit => unit.EntryCount);
    }

    private void ReadEnv(ImportNode env)
    {
        if (env.Titles.Count > 0)
        {
            Add(env, ImportScope.GroupOwnEntries, env.Name);
        }

        foreach (var project in Live(env, env.Name))
        {
            var projectPath = env.Name + "/" + project.Name;
            ProjectCount++;
            Add(project, ImportScope.GroupOwnEntries, projectPath, project: project.Name);

            foreach (var profile in Live(project, projectPath))
            {
                Add(profile, ImportScope.WholeGroup, projectPath + "/" + profile.Name, project: project.Name);
            }
        }
    }

    private void Add(ImportNode node, ImportScope scope, string sourceGroup, bool isRoot = false, string? project = null)
    {
        List<(string Relative, IReadOnlyList<string> Titles)> groups = [(string.Empty, node.Titles)];

        if (scope == ImportScope.WholeGroup)
        {
            Collect(node, sourceGroup, string.Empty, groups);
        }

        _units.Add(new Unit(
            node.Id,
            scope,
            sourceGroup,
            node.Titles,
            groups,
            groups.Sum(group => group.Titles.Count),
            scope == ImportScope.WholeGroup && node.Children.Count > 0,
            isRoot,
            project));
    }

    /// <summary>Every live subgroup's titles, by its path below the unit's group.</summary>
    private void Collect(ImportNode node, string path, string relative, List<(string Relative, IReadOnlyList<string> Titles)> groups)
    {
        foreach (var child in Live(node, path))
        {
            var below = relative.Length == 0 ? child.Name : relative + "/" + child.Name;
            groups.Add((below, child.Titles));
            Collect(child, path + "/" + child.Name, below, groups);
        }
    }

    /// <summary>A group's children, noting any recycle bin among them as skipped.</summary>
    private List<ImportNode> Live(ImportNode node, string path)
    {
        List<ImportNode> live = [];
        foreach (var child in node.Children)
        {
            if (child.IsRecycleBin)
            {
                _skipped.Add(new ImportSkip(path + "/" + child.Name, Raw(child)));
            }
            else
            {
                live.Add(child);
            }
        }

        return live;
    }

    private static int Raw(ImportNode node) => node.Titles.Count + node.Children.Sum(Raw);

    private string DefaultInto(HashSet<string> groups)
    {
        var stem = Path.GetFileNameWithoutExtension(Probe.FileName).TrimStart('.');
        var name = new string([.. stem.Select(c => c is '/' or '\\' || char.IsControl(c) ? '-' : c)]).Trim();
        if (!VaultNameRules.IsValidGroupName(name, out _))
        {
            name = "imported";
        }

        var candidate = name;
        for (var suffix = 2; Taken(candidate); suffix++)
        {
            candidate = $"{name} ({suffix})";
        }

        return candidate;

        bool Taken(string group) =>
            groups.Contains(group)
            || string.Equals(group, EnvConvention.RootGroup, StringComparison.Ordinal)
            || string.Equals(group, KeePassInterop.RecycleBinName, StringComparison.Ordinal)
            || ReservedGroups.IsReserved(group);
    }

    private static bool IsEnv(string destination) =>
        string.Equals(destination, EnvConvention.RootGroup, StringComparison.Ordinal)
        || destination.StartsWith(EnvConvention.RootGroup + "/", StringComparison.Ordinal);

    private static string? RefuseDestination(string destination, Dictionary<string, int> groupCounts)
    {
        if (destination.Length == 0)
        {
            return "name a group to import into";
        }

        if (ReservedGroups.IsReserved(destination))
        {
            return $"{ReservedGroups.Root} is keypaste's own group; nothing is imported into it";
        }

        var prefix = string.Empty;
        foreach (var segment in destination.Split('/'))
        {
            if (!VaultNameRules.IsValidGroupName(segment, out var error))
            {
                return $"{destination}: {error}";
            }

            if (string.Equals(segment, KeePassInterop.RecycleBinName, StringComparison.Ordinal))
            {
                return $"{KeePassInterop.RecycleBinName} is the recycle bin's name";
            }

            prefix = prefix.Length == 0 ? segment : prefix + "/" + segment;
            if (groupCounts.GetValueOrDefault(prefix) > 1)
            {
                return $"{prefix} names {groupCounts[prefix]} groups in this vault; rename one in KeePassXC";
            }
        }

        return null;
    }

    private static string? RefuseEnvSet(Unit unit, string destination, List<string> there)
    {
        var segments = destination.Split('/');

        if (segments.Length == 1)
        {
            return "entries directly in env belong to no project; import into env/<project>";
        }

        if (segments.Length > 3)
        {
            return $"{destination} is not an env set: a set is env/<project> or env/<project>/<profile>";
        }

        if (!EnvConvention.IsValidProject(segments[1], out var projectError))
        {
            return projectError;
        }

        if (segments.Length == 3)
        {
            if (string.Equals(segments[2], EnvProfileNames.Default, StringComparison.Ordinal))
            {
                return $"'{EnvProfileNames.Default}' is the default profile; its variables live in {EnvConvention.GroupPath(segments[1])} itself";
            }

            if (!EnvProfileNames.IsValid(segments[2], out var profileError))
            {
                return profileError;
            }
        }
        else if (unit.HasSubgroups)
        {
            return $"{unit.SourceGroup} has subgroups, which would become profiles of {destination}; import it elsewhere";
        }

        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (var title in unit.Titles)
        {
            if (!EnvConvention.IsValidKey(title, out var keyError))
            {
                return keyError;
            }

            if (there.Contains(title, StringComparer.Ordinal))
            {
                return $"'{title}' is already in {destination}";
            }

            if (!seen.Add(title))
            {
                return $"'{title}' is in {unit.SourceGroup} twice";
            }
        }

        return EnvNameRules.TryCheckCase([.. there, .. unit.Titles], out var caseError)
            ? null
            : $"{destination} {caseError}";
    }

    private sealed record Unit(
        string Id,
        ImportScope Scope,
        string SourceGroup,
        IReadOnlyList<string> Titles,
        IReadOnlyList<(string Relative, IReadOnlyList<string> Titles)> Groups,
        int EntryCount,
        bool HasSubgroups,
        bool IsRootEntries,
        string? Project);
}

/// <summary>A source group as an import sees it: names and titles only.</summary>
internal sealed record ImportNode(string Id, string Name, IReadOnlyList<string> Titles, IReadOnlyList<ImportNode> Children, bool IsRecycleBin);

/// <summary>How much of a source group one row copies.</summary>
internal enum ImportScope
{
    /// <summary>The group, everything under it, and its own metadata.</summary>
    WholeGroup = 0,

    /// <summary>The group and its own entries, without its subgroups.</summary>
    GroupOwnEntries = 1,

    /// <summary>The group's own entries, into the destination; the root's are copied this way.</summary>
    EntriesOnly = 2,
}

/// <summary>One confirmed row, as the vault copies it.</summary>
internal sealed record ImportPiece(string SourceId, ImportScope Scope, string Destination);
