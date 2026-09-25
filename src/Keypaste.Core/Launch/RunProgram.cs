using System.Diagnostics.CodeAnalysis;

namespace Keypaste.Core.Launch;

/// <summary>
/// Finds the exact program and directory an agent's run will use, before anybody is asked, so what a
/// person approves is what starts (THREATS.md T-35).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="System.Diagnostics.ProcessStartInfo.FileName"/> is not resolved against the working
/// directory: .NET on Unix tries the launching executable's directory and its current directory before
/// <c>PATH</c>, and Windows' <c>CreateProcess</c> does the same. Either would let an agent that can write
/// a file where the bridge runs replace <c>npm</c>. Here a name holding a separator is taken relative to
/// the directory the person is shown, and a bare name is searched only on the inherited <c>PATH</c>'s
/// absolute entries.
/// </para>
/// <para>
/// On Windows a bare name is tried with <c>.exe</c>, <c>.com</c>, <c>.cmd</c> and <c>.bat</c> in
/// <c>PATHEXT</c> order; <see cref="Approval.RunRequestRules.CheckProgram"/> refuses the arguments
/// <c>cmd.exe</c> would reinterpret for the last two.
/// </para>
/// </remarks>
public static class RunProgram
{
    private static readonly string[] _windowsExtensions = [".exe", ".com", ".cmd", ".bat"];

    /// <summary>Finds the program a command's first item names.</summary>
    /// <param name="named">The command's first item.</param>
    /// <param name="directory">The directory the run starts in, already resolved.</param>
    /// <param name="path">The inherited <c>PATH</c>, or null.</param>
    /// <param name="pathExtensions">The inherited <c>PATHEXT</c>, or null; read on Windows only.</param>
    /// <param name="program">The program's absolute path, when found.</param>
    /// <param name="error">Why none was, naming the item as the agent wrote it.</param>
    /// <returns>Whether exactly one program was found.</returns>
    public static bool TryResolve(
        string named,
        string directory,
        string? path,
        string? pathExtensions,
        [NotNullWhen(true)] out string? program,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(named);
        ArgumentNullException.ThrowIfNull(directory);

        program = null;

        if (named.Contains('/', StringComparison.Ordinal)
            || (OperatingSystem.IsWindows() && (named.Contains('\\', StringComparison.Ordinal) || named.Contains(':', StringComparison.Ordinal))))
        {
            program = Candidates(Path.GetFullPath(Path.Combine(directory, named)), pathExtensions).FirstOrDefault(IsProgram);
        }
        else
        {
            foreach (var entry in (path ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                // A relative entry, such as ".", would be read against the bridge's own directory.
                if (!Path.IsPathFullyQualified(entry))
                {
                    continue;
                }

                program = Candidates(Path.Combine(entry, named), pathExtensions).FirstOrDefault(IsProgram);

                if (program is not null)
                {
                    break;
                }
            }
        }

        if (program is null)
        {
            error = $"no program '{named}' was found {(Path.IsPathFullyQualified(named) || named.Contains('/', StringComparison.Ordinal) ? "there" : "on PATH")}";
            return false;
        }

        program = Path.GetFullPath(program);
        error = string.Empty;
        return true;
    }

    /// <summary>Resolves every link in an existing directory's path to its final target.</summary>
    /// <param name="directory">An absolute path <see cref="Approval.RunRequestRules"/> accepted.</param>
    /// <param name="resolved">The directory with no link left in its path, when it exists.</param>
    /// <param name="error">Why it could not be used.</param>
    /// <returns>Whether it names an existing directory.</returns>
    /// <remarks>
    /// So the directory a person is shown is the one the command starts in, not a link an agent could
    /// point elsewhere. Swapping a component after the person answered is not prevented.
    /// </remarks>
    public static bool TryResolveDirectory(string directory, [NotNullWhen(true)] out string? resolved, out string error)
    {
        ArgumentNullException.ThrowIfNull(directory);

        resolved = null;
        error = "must be an existing directory";

        try
        {
            var full = Path.GetFullPath(directory);
            var root = Path.GetPathRoot(full);

            if (string.IsNullOrEmpty(root) || !Directory.Exists(full))
            {
                return false;
            }

            var current = root;

            foreach (var segment in full[root.Length..].Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                var info = new DirectoryInfo(current);

                if (!info.Exists)
                {
                    return false;
                }

                if (info.LinkTarget is not null)
                {
                    if (info.ResolveLinkTarget(returnFinalTarget: true) is not { } target)
                    {
                        return false;
                    }

                    current = Path.GetFullPath(target.FullName);
                }
            }

            resolved = current;
            error = string.Empty;
            return Directory.Exists(resolved);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static IEnumerable<string> Candidates(string path, string? pathExtensions)
    {
        if (!OperatingSystem.IsWindows())
        {
            yield return path;
            yield break;
        }

        if (_windowsExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
        {
            yield return path;
            yield break;
        }

        var ordered = (pathExtensions ?? string.Join(';', _windowsExtensions))
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(extension => _windowsExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            .Concat(_windowsExtensions)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var extension in ordered)
        {
            yield return path + extension;
        }
    }

    private static bool IsProgram(string candidate)
    {
        try
        {
            if (!File.Exists(candidate))
            {
                return false;
            }

            return OperatingSystem.IsWindows()
                || (File.GetUnixFileMode(candidate) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
