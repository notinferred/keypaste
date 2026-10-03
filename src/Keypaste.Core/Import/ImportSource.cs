using Keypaste.Core.Internal;

namespace Keypaste.Core.Import;

/// <summary>An unlocked KDBX file to copy from, and the plan for where its groups land.</summary>
/// <remarks>
/// <para>
/// Its key lives here and nowhere else, and goes when this is disposed. Nothing reachable from it
/// writes the file.
/// </para>
/// <para>
/// Every top-level group is a row, <c>env</c> included: an entry's projects travel in its own tags
/// (D-0416). The recycle bin, wherever it is, and <see cref="ReservedGroups.Root"/> are never rows:
/// tokens and share records belong to the vault that made them.
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
        ProjectCount = interop.ReadTags()
            .Where(entry => !ReservedGroups.IsReserved(entry.Name.GroupPath))
            .SelectMany(entry => entry.Tags.Select(ProjectTag.Read))
            .Where(tag => tag.Kind == ProjectTagKind.Member)
            .Select(tag => tag.Project)
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    /// <summary>What the file's header said.</summary>
    public KdbxProbe Probe { get; }

    /// <summary>How many entries a whole import copies: all but the recycle bin's and reserved ones.</summary>
    public int EntryCount { get; private set; }

    /// <summary>How many projects the file's entries are tagged into (<see cref="ProjectCatalog"/>).</summary>
    public int ProjectCount { get; }

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
    /// Every top-level group at <c>&lt;into&gt;/&lt;name&gt;</c>, and the root's own entries at
    /// <c>&lt;into&gt;</c>.
    /// </returns>
    public ImportPlan DefaultPlan(Vault target, string? into)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(target);

        var groups = target.ReadGroupPaths().ToHashSet(StringComparer.Ordinal);
        var intoGroup = into ?? DefaultInto(groups);
        List<ImportRow> rows = [];

        foreach (var (unit, index) in _units.Select((unit, index) => (unit, index)))
        {
            string? reason = null;
            var destination = unit.IsRootEntries ? intoGroup : Unreserved(intoGroup, unit.SourceGroup, groups, rows, ref reason);

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
            else
            {
                Add(top, ImportScope.WholeGroup, top.Name);
            }
        }

        EntryCount = _units.Sum(unit => unit.EntryCount);
    }

    private void Add(ImportNode node, ImportScope scope, string sourceGroup, bool isRoot = false)
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
            isRoot));
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

    private sealed record Unit(
        string Id,
        ImportScope Scope,
        string SourceGroup,
        IReadOnlyList<string> Titles,
        IReadOnlyList<(string Relative, IReadOnlyList<string> Titles)> Groups,
        int EntryCount,
        bool IsRootEntries);
}

/// <summary>A source group as an import sees it: names and titles only.</summary>
internal sealed record ImportNode(string Id, string Name, IReadOnlyList<string> Titles, IReadOnlyList<ImportNode> Children, bool IsRecycleBin);

/// <summary>How much of a source group one row copies.</summary>
internal enum ImportScope
{
    /// <summary>The group, everything under it, and its own metadata.</summary>
    WholeGroup = 0,

    /// <summary>The group's own entries, into the destination; the root's are copied this way.</summary>
    EntriesOnly = 2,
}

/// <summary>One confirmed row, as the vault copies it.</summary>
internal sealed record ImportPiece(string SourceId, ImportScope Scope, string Destination);
