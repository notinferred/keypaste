namespace Keypaste.App.ViewModels;

/// <summary>
/// A node of the group tree, built here because the core does not have one.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Core.Vault.ReadGroupPaths"/> returns flat, slash-separated paths, and
/// <see cref="Core.VaultEntry.GroupPath"/> is one of those. Assembling them into a tree is a
/// rendering concern, so it happens in the front end that renders — <c>ListCommand.Indent</c> does
/// the same job in the CLI for a different renderer. Adding a tree type to the core would put a
/// shape in the shared library that only one caller wants.
/// </para>
/// <para>
/// A group with no entries appears here, because it appears in
/// <see cref="Core.Vault.ReadGroupPaths"/> and in KeePassXC. A tree that quietly dropped it would be
/// keypaste disagreeing with KeePassXC about the contents of one file, which is the failure docs/PRODUCT.md
/// law 4.6 exists to prevent.
/// </para>
/// </remarks>
internal sealed class GroupNode
{
    private readonly List<GroupNode> _children = [];

    private GroupNode(string name, string path, int depth)
    {
        Name = name;
        Path = path;
        Depth = depth;
    }

    /// <summary>The group's own name, without its parents.</summary>
    internal string Name { get; }

    /// <summary>The full slash-separated path, or empty for the root.</summary>
    internal string Path { get; }

    /// <summary>How deep this sits, for the indent.</summary>
    internal int Depth { get; }

    /// <summary>The groups directly inside this one, in the order they were seen.</summary>
    internal IReadOnlyList<GroupNode> Children => _children;

    /// <summary>Whether this is the "everything" row rather than a real group.</summary>
    internal bool IsEverything => Path.Length == 0;

    /// <summary>What the sidebar shows.</summary>
    internal string Label => IsEverything ? "All entries" : Name;

    /// <summary>
    /// Builds the tree, flattened depth-first into the order a list should draw it.
    /// </summary>
    /// <param name="groupPaths">Every group path in the vault.</param>
    /// <returns>The root "everything" node first, then every group, parents before children.</returns>
    /// <remarks>
    /// Flattened rather than nested because the view draws an indented list rather than a
    /// <c>TreeView</c>: a tree control brings expand state, keyboard conventions and a virtualisation
    /// story for a structure that is usually three rows deep. <see cref="Depth"/> is the indent.
    /// </remarks>
    internal static IReadOnlyList<GroupNode> Flatten(IEnumerable<string> groupPaths)
    {
        ArgumentNullException.ThrowIfNull(groupPaths);

        var everything = new GroupNode("All entries", string.Empty, 0);
        var byPath = new Dictionary<string, GroupNode>(StringComparer.Ordinal);

        foreach (var path in groupPaths.OrderBy(p => p, StringComparer.Ordinal))
        {
            EnsureNode(path, everything, byPath);
        }

        List<GroupNode> flat = [everything];
        Append(everything, flat);
        return flat;
    }

    /// <summary>Whether an entry in <paramref name="groupPath"/> belongs under this node.</summary>
    /// <param name="groupPath">The entry's group.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    /// <remarks>
    /// Selecting a group shows what is inside it <em>and</em> inside its children, which is what a
    /// person expects of a folder and what <c>keypaste ls</c> shows under a heading. The prefix test
    /// includes the separator so <c>env/billing</c> does not swallow <c>env/billing-api</c>.
    /// </remarks>
    internal bool Contains(string groupPath)
    {
        ArgumentNullException.ThrowIfNull(groupPath);

        return IsEverything
            || string.Equals(groupPath, Path, StringComparison.Ordinal)
            || groupPath.StartsWith(Path + "/", StringComparison.Ordinal);
    }

    private static GroupNode EnsureNode(
        string path,
        GroupNode everything,
        Dictionary<string, GroupNode> byPath)
    {
        if (path.Length == 0)
        {
            return everything;
        }

        if (byPath.TryGetValue(path, out var existing))
        {
            return existing;
        }

        var slash = path.LastIndexOf('/');
        var parentPath = slash < 0 ? string.Empty : path[..slash];
        var name = slash < 0 ? path : path[(slash + 1)..];

        // A group whose parent was never listed still gets one, so a vault holding only
        // "a/b/c" draws a, then b, then c rather than one deeply named row.
        var parent = EnsureNode(parentPath, everything, byPath);
        var node = new GroupNode(name, path, parent.Depth + 1);

        parent._children.Add(node);
        byPath[path] = node;
        return node;
    }

    private static void Append(GroupNode node, List<GroupNode> flat)
    {
        foreach (var child in node._children)
        {
            flat.Add(child);
            Append(child, flat);
        }
    }
}
