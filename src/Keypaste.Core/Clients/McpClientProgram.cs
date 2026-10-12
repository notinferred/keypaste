using System.Diagnostics.CodeAnalysis;
using Keypaste.Core.Launch;
using Keypaste.Core.Processes;

namespace Keypaste.Core.Clients;

/// <summary>How keypaste starts a tool's own command: the program, and what goes before the tool's arguments.</summary>
/// <param name="FileName">The absolute program the runner is given.</param>
/// <param name="Prefix">The script <c>node</c> runs, or nothing.</param>
/// <remarks>
/// <para>
/// A tool's command is found only in <c>PATH</c>'s absolute entries and, for the macOS app, the tool's documented install
/// folders after them: never in the working directory or keypaste's own, which <c>Process.Start</c> and
/// <c>CreateProcess</c> would try first for a bare name. That is <see cref="RunProgram"/>'s rule for T-35, and a tool
/// not found there is not installed and never started (D-0436).
/// </para>
/// <para>
/// On Windows an npm launcher is started as the program it names, so no argument passes through <c>cmd.exe</c>. Any
/// other batch launcher is found but cannot be started. Elsewhere a <c>#!/usr/bin/env node</c> script found in an
/// install folder off <c>PATH</c> is started with a <c>node</c> found the same way, because <c>env</c> would search a
/// <c>PATH</c> without one.
/// </para>
/// </remarks>
public sealed record McpClientProgram(string FileName, IReadOnlyList<string> Prefix)
{
    private const int _largestLauncher = 64 * 1024;

    private static readonly Encoding _utf8 = new UTF8Encoding(false);

    private static ReadOnlySpan<byte> EnvNode => "#!/usr/bin/env node"u8;

    /// <summary>Runs the program with <paramref name="arguments"/> after its prefix, passed without a shell.</summary>
    /// <param name="runner">Starts the process.</param>
    /// <param name="arguments">The tool's arguments, passed as they are.</param>
    /// <param name="timeout">How long the tool may take.</param>
    /// <returns>What the tool did.</returns>
    public ProcessResult Run(IProcessRunner runner, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(arguments);

        return runner.Run(FileName, [.. Prefix, .. arguments], stdin: null, _utf8, timeout);
    }

    /// <summary>Finds how to start <paramref name="client"/>'s command on <paramref name="machine"/>, starting nothing.</summary>
    /// <param name="client">The tool.</param>
    /// <param name="machine">Where to look.</param>
    /// <param name="program">How to start it, when it can be.</param>
    /// <param name="problem">Why a command that was found cannot be started, naming it; null when none was found.</param>
    /// <returns>Whether it was found and can be started. False with no problem means not installed.</returns>
    public static bool TryLocate(
        McpClient client,
        McpClientMachine machine,
        [NotNullWhen(true)] out McpClientProgram? program,
        out string? problem)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(machine);

        program = null;
        problem = null;

        if (client.Executable is not { } command)
        {
            return false;
        }

        if (command.Length == 0 || command.Any(c => c is '/' or '\\' or ':'))
        {
            throw new ArgumentException("a tool's command is a bare name", nameof(client));
        }

        var (folders, onPath) = Search(client, machine);

        for (var i = 0; i < folders.Count; i++)
        {
            if (Resolve(command, [folders[i]], machine.PathExtensions) is { } found)
            {
                return OperatingSystem.IsWindows()
                    ? TryStartOnWindows(found, folders, machine.PathExtensions, out program, out problem)
                    : TryStartElsewhere(found, offPath: i >= onPath, folders, out program, out problem);
            }
        }

        return false;
    }

    /// <summary>The folders searched for <paramref name="client"/>'s command, in order.</summary>
    internal static IReadOnlyList<string> SearchFolders(McpClient client, McpClientMachine machine) => Search(client, machine).Folders;

    /// <summary>Where <paramref name="command"/> is, searched for as <paramref name="client"/>'s own command would be, starting nothing.</summary>
    /// <param name="command">A bare command name, such as a launcher that is never started.</param>
    /// <param name="client">The tool whose folders are searched.</param>
    /// <param name="machine">Where to look.</param>
    /// <returns>Its path, or null when it is not there.</returns>
    internal static string? Find(string command, McpClient client, McpClientMachine machine) =>
        Resolve(command, Search(client, machine).Folders, machine.PathExtensions);

    private static (List<string> Folders, int OnPath) Search(McpClient client, McpClientMachine machine)
    {
        var folders = new List<string>();

        foreach (var entry in (machine.PathVariable ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            Add(folders, entry);
        }

        var onPath = folders.Count;

        if (machine.LooksInInstallFolders)
        {
            foreach (var folder in client.InstallFolders)
            {
                if (!folder.StartsWith("~/", StringComparison.Ordinal))
                {
                    Add(folders, folder);
                }
                else if (machine.Home is { } home)
                {
                    Add(folders, Path.Combine(home, folder[2..]));
                }
            }
        }

        return (folders, onPath);
    }

    // A relative folder would be read against the directory keypaste runs in, and one holding the separator would be
    // split by PATH's rule.
    private static void Add(List<string> folders, string folder)
    {
        if (!Path.IsPathFullyQualified(folder) || folder.Contains(Path.PathSeparator, StringComparison.Ordinal))
        {
            return;
        }

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));

        if (!folders.Any(known => string.Equals(known, full, PathIdentity.Comparison)))
        {
            folders.Add(full);
        }
    }

    private static string? Resolve(string name, IEnumerable<string> folders, string? pathExtensions)
    {
        foreach (var folder in folders)
        {
            if (RunProgram.TryResolve(name, folder, folder, pathExtensions, out var found, out _))
            {
                return found;
            }
        }

        return null;
    }

    private static bool TryStartOnWindows(
        string found,
        IReadOnlyList<string> folders,
        string? pathExtensions,
        [NotNullWhen(true)] out McpClientProgram? program,
        out string? problem)
    {
        program = null;
        problem = null;

        if (StartsDirectly(found))
        {
            program = new McpClientProgram(found, []);
            return true;
        }

        var directory = Path.GetDirectoryName(found)!;

        if (Path.GetExtension(found).Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            && ReadLauncher(found) is { } text
            && NpmCmdShim.TryRead(text, out var relative, out var throughNode)
            && Path.GetFullPath(Path.Combine(directory, relative)) is var target
            && File.Exists(target))
        {
            if (!throughNode)
            {
                program = new McpClientProgram(target, []);
                return true;
            }

            var beside = Path.Combine(directory, "node.exe");
            var node = File.Exists(beside) ? beside : Resolve("node", folders, pathExtensions);

            if (node is not null && StartsDirectly(node))
            {
                program = new McpClientProgram(node, [target]);
                return true;
            }
        }

        problem = $"found {found}, which keypaste cannot start without cmd.exe";
        return false;
    }

    private static bool TryStartElsewhere(
        string found,
        bool offPath,
        IReadOnlyList<string> folders,
        [NotNullWhen(true)] out McpClientProgram? program,
        out string? problem)
    {
        program = null;
        problem = null;

        if (!offPath || !NeedsNode(found))
        {
            program = new McpClientProgram(found, []);
            return true;
        }

        if (Resolve("node", [Path.GetDirectoryName(found)!, .. folders], pathExtensions: null) is { } node)
        {
            program = new McpClientProgram(node, [found]);
            return true;
        }

        problem = $"found {found}, which needs Node, and keypaste found no node beside it or on PATH";
        return false;
    }

    private static bool StartsDirectly(string path) =>
        Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(path).Equals(".com", StringComparison.OrdinalIgnoreCase);

    private static string? ReadLauncher(string path)
    {
        try
        {
            return new FileInfo(path).Length <= _largestLauncher ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool NeedsNode(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> start = stackalloc byte[EnvNode.Length + 1];
            var read = stream.ReadAtLeast(start, start.Length, throwOnEndOfStream: false);

            return read >= EnvNode.Length
                && start[..EnvNode.Length].SequenceEqual(EnvNode)
                && (read == EnvNode.Length || start[EnvNode.Length] == (byte)'\n');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
