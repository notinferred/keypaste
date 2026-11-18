namespace Keypaste.Core.Ipc;

/// <summary>Who is on the other end of an owner's pipe: whether it is keypaste's own program, and the programs that started it, nearest first.</summary>
/// <param name="Executable">The connected program's executable, or null when the system did not say.</param>
/// <param name="IsKeypaste">Whether the connected program is the keypaste binary this owner ships beside itself.</param>
/// <param name="Ancestors">The executables of the processes above it, its parent first, as far as each started before the one below it.</param>
internal sealed record PeerLineage(string? Executable, bool IsKeypaste, IReadOnlyList<string> Ancestors)
{
    /// <summary>The programs that run a script they are given, whose script, not their own file, says which tool they are.</summary>
    internal static readonly IReadOnlySet<string> ScriptHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "node", "nodejs", "bun" };

    /// <summary>The script the parent runs, its links followed to the file itself, when the parent is a <see cref="ScriptHosts">script host</see> given an absolute path; otherwise null.</summary>
    internal string? ParentScript { get; init; }

    /// <summary>How many processes above the caller are read; a terminal is a few levels up, and nothing past this changes a prompt.</summary>
    internal const int MaximumDepth = 12;

    /// <summary>Nothing could be read: not keypaste, started by nobody known.</summary>
    internal static PeerLineage Unknown { get; } = new(null, false, []);

    /// <summary>Reads the lineage of the process <paramref name="pid"/> running <paramref name="path"/>.</summary>
    /// <param name="pid">The caller's id, or null when the system did not say.</param>
    /// <param name="path">The caller's executable, or null.</param>
    /// <param name="tree">Where processes are read.</param>
    /// <param name="isKeypaste">Whether an executable is keypaste's own.</param>
    /// <returns>The lineage; never throws.</returns>
    /// <remarks>
    /// A process's parent counts only when it started no later than the process itself, so an id the system reused for a
    /// newer process after the real parent exited ends the walk rather than naming the newcomer.
    /// </remarks>
    internal static PeerLineage Read(int? pid, string? path, IProcessTree tree, Func<string, bool> isKeypaste)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(isKeypaste);

        if (pid is not { } id || path is null)
        {
            return Unknown;
        }

        bool keypaste;

        try
        {
            keypaste = isKeypaste(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            keypaste = false;
        }

        List<string> ancestors = [];
        string? parentScript = null;
        var current = tree.Of(id);

        while (current is { Parent: > 1 and var parentId } && ancestors.Count < MaximumDepth)
        {
            if (tree.Of(parentId) is not { Path: { } parentPath } parent || !StartedNoLater(parent, current))
            {
                break;
            }

            if (ancestors.Count == 0)
            {
                parentScript = ScriptOf(parent);
            }

            ancestors.Add(parentPath);
            current = parent;
        }

        return new PeerLineage(path, keypaste, ancestors) { ParentScript = parentScript };
    }

    /// <summary>The first argument after a script host's own options, and after <c>run</c> for Bun, as the file it names; null for anything else.</summary>
    private static string? ScriptOf(ProcessFacts host)
    {
        if (host.Path is not { } program || !ScriptHosts.Contains(ProgramName(program)) || host.Arguments is not { Count: > 1 } arguments)
        {
            return null;
        }

        var script = arguments.Skip(1).SkipWhile(argument => argument.StartsWith('-') || argument == "run").FirstOrDefault();

        if (script is null || !Path.IsPathFullyQualified(script))
        {
            return null;
        }

        try
        {
            return RealPath(script);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>A path with every link along it followed, as the system would open it.</summary>
    internal static string RealPath(string path)
    {
        var resolved = Path.GetPathRoot(path) ?? string.Empty;

        foreach (var part in Path.GetRelativePath(resolved, path).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            resolved = Path.Combine(resolved, part);

            // A chain of links is followed to its end, and a cycle the system refuses ends in an IOException.
            if (new FileInfo(resolved).ResolveLinkTarget(returnFinalTarget: true) is { } target)
            {
                resolved = target.FullName;
            }
        }

        return resolved;
    }

    /// <summary>A program's file name without <c>.exe</c>.</summary>
    internal static string ProgramName(string path)
    {
        var name = path[(path.LastIndexOfAny(['/', '\\']) + 1)..];
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    private static bool StartedNoLater(ProcessFacts parent, ProcessFacts child) =>
        parent.Started is { } before && child.Started is { } after && before <= after;
}
