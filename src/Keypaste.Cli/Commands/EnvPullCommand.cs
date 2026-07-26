using Keypaste.Core;

namespace Keypaste.Cli.Commands;

/// <summary>Imports a <c>.env</c> file: <c>keypaste env pull &lt;project&gt; [file]</c>.</summary>
/// <remarks>
/// <para>
/// <b>All or nothing.</b> The file is read, decoded and checked completely before the vault is
/// opened, and every problem is reported at once. A half-imported <c>.env</c> whose original was
/// then deleted is unrecoverable, and this command offers to delete the original — so the import
/// has to be atomic or the offer is a trap.
/// </para>
/// <para>
/// Variable names are printed; values never are, on any path, including diagnostics.
/// </para>
/// </remarks>
internal static class EnvPullCommand
{
    /// <summary>The file used when the command is given no path.</summary>
    internal const string DefaultFileName = ".env";

    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new("yes", TakesValue: false),
        new("delete-source", TakesValue: false),
        new("keep", TakesValue: false),
    ];

    internal static int Execute(string[] args, CliContext context)
    {
        if (!CommandLine.TryParse(args, 2, _options, out var line, out var error))
        {
            return Fail(context, error);
        }

        if (line.WantsHelp)
        {
            WriteUsage(context.Stdout);
            return CliApp.ExitSuccess;
        }

        if (line.Operands.Count is 0 or > 2)
        {
            return Fail(context, "expected a project and an optional path to a .env file");
        }

        var deleteSource = line.HasFlag("delete-source");
        var keep = line.HasFlag("keep");
        if (deleteSource && keep)
        {
            return Fail(context, "--delete-source and --keep contradict each other");
        }

        var project = line.Operands[0];
        if (!EnvConvention.IsValidProject(project, out var projectError))
        {
            return Fail(context, projectError);
        }

        var assumeYes = line.HasFlag("yes");

        // Same rule as `rm`: a piped run has to ask for the write explicitly rather than have a
        // confirmation answered by whatever the next line of stdin happens to be.
        if (!assumeYes && !context.Prompt.IsInteractive)
        {
            return Fail(context, "--yes is required when stdin is not a terminal");
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var vaultPath, out var locateError))
        {
            return Fail(context, locateError);
        }

        var sourcePath = Path.GetFullPath(line.Operands.Count == 2 ? line.Operands[1] : DefaultFileName);

        // Everything about the file is settled before the master password is asked for: a typo in
        // the path should not cost a password entry and a key derivation to discover.
        if (!TryReadFile(sourcePath, context, out var document, out var readExit))
        {
            return readExit;
        }

        foreach (var advisory in Advisories(document))
        {
            context.Stderr.WriteLine(advisory);
        }

        var exit = VaultSession.Open(vaultPath, context, vault =>
            Import(vault, project, document, assumeYes, context));

        if (exit != CliApp.ExitSuccess)
        {
            return exit;
        }

        return Cleanup(sourcePath, deleteSource, keep, context);
    }

    /// <summary>Reads and checks the file, reporting everything wrong with it at once.</summary>
    private static bool TryReadFile(
        string sourcePath,
        CliContext context,
        out DotEnvDocument document,
        out int exit)
    {
        document = null!;

        if (!File.Exists(sourcePath))
        {
            context.Stderr.WriteLine($"keypaste env pull: no file at '{sourcePath}'");
            exit = CliApp.ExitNotFound;
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(sourcePath);
        }
        catch (IOException ex)
        {
            context.Stderr.WriteLine($"keypaste env pull: could not read '{sourcePath}': {ex.Message}");
            exit = CliApp.ExitInternalError;
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            context.Stderr.WriteLine($"keypaste env pull: could not read '{sourcePath}': {ex.Message}");
            exit = CliApp.ExitInternalError;
            return false;
        }

        if (!DotEnv.TryDecode(bytes, out var text, out var decodeError))
        {
            exit = Fail(context, $"'{sourcePath}': {decodeError}");
            return false;
        }

        if (!DotEnv.TryParse(text, out document))
        {
            context.Stderr.WriteLine($"keypaste env pull: '{sourcePath}' has {Count(document.Problems.Count, "problem")}:");
            exit = ReportProblems(document.Problems, context);
            return false;
        }

        exit = CliApp.ExitSuccess;
        return true;
    }

    /// <summary>Prints at most ten problems, then says how many were left out.</summary>
    /// <remarks>
    /// Pointing this at the wrong file — a log, a lock file — should not produce a wall of output
    /// that hides the one line saying it was the wrong file.
    /// </remarks>
    private static int ReportProblems(IReadOnlyList<DotEnvProblem> problems, CliContext context)
    {
        const int Shown = 10;

        for (var i = 0; i < problems.Count && i < Shown; i++)
        {
            context.Stderr.WriteLine($"  {problems[i].Message}");
        }

        if (problems.Count > Shown)
        {
            context.Stderr.WriteLine($"  ({problems.Count - Shown} more not shown)");
        }

        context.Stderr.WriteLine("Nothing was imported.");
        return CliApp.ExitUsageError;
    }

    /// <summary>Things the reader interpreted, said once, naming keys and never values.</summary>
    private static IEnumerable<string> Advisories(DotEnvDocument document)
    {
        var comments = NamesOf(document, DotEnvNoteKind.InlineCommentRemoved);
        if (comments.Length > 0)
        {
            yield return $"note: a trailing ' #' comment was removed from: {comments}. " +
                "Quote the value if the '#' was part of it.";
        }

        var literal = NamesOf(document, DotEnvNoteKind.LiteralInterpolation);
        if (literal.Length > 0)
        {
            yield return $"note: values are stored exactly as written; ${{...}} is not expanded in: {literal}";
        }
    }

    private static string NamesOf(DotEnvDocument document, DotEnvNoteKind kind) =>
        string.Join(", ", document.Notes.Where(n => n.Kind == kind).Select(n => n.Key));

    /// <summary>Plans the import, confirms it, and writes it in one save.</summary>
    private static int Import(
        Vault vault,
        string project,
        DotEnvDocument document,
        bool assumeYes,
        CliContext context)
    {
        var store = new EnvStore(vault);
        var existing = store.Read(project).ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);

        if (Collision(document.Variables, existing) is { } collision)
        {
            return Fail(context, collision);
        }

        List<string> created = [];
        List<string> updated = [];
        var unchanged = 0;

        foreach (var variable in document.Variables)
        {
            if (!existing.TryGetValue(variable.Key, out var current))
            {
                created.Add(variable.Key);
            }
            else if (!string.Equals(current, variable.Value, StringComparison.Ordinal))
            {
                updated.Add(variable.Key);
            }
            else
            {
                unchanged++;
            }
        }

        // Sorted, like every other listing keypaste prints, so the plan reads the same way as the
        // `env ls` the user runs straight afterwards.
        created.Sort(StringComparer.Ordinal);
        updated.Sort(StringComparer.Ordinal);

        var groupPath = EnvConvention.GroupPath(project);
        context.Stderr.WriteLine(
            $"{groupPath}: {created.Count} new, {updated.Count} updated, {unchanged} unchanged");
        WriteNames(context, "new", created);
        WriteNames(context, "updated", updated);

        if (created.Count + updated.Count == 0)
        {
            context.Stderr.WriteLine($"{groupPath} already matches the file; nothing to do.");
            return CliApp.ExitSuccess;
        }

        if (updated.Count > 0)
        {
            context.Stderr.WriteLine(
                $"{Count(updated.Count, "variable")} will be updated; the previous values stay in the entry history.");
        }

        if (!assumeYes)
        {
            var answer = context.Prompt.ReadLine(
                $"Import {Count(created.Count + updated.Count, "variable")} into {groupPath}? [y/N] ");
            if (answer is null || !answer.Trim().StartsWith('y') && !answer.Trim().StartsWith('Y'))
            {
                context.Stderr.WriteLine("Cancelled.");
                return CliApp.ExitUsageError;
            }
        }

        var write = new HashSet<string>(created.Concat(updated), StringComparer.Ordinal);

        foreach (var variable in document.Variables)
        {
            // Unchanged variables are skipped rather than rewritten. TrySet cannot tell that the
            // new value equals the old one, so it would spend a KDBX history slot — of the ten the
            // format keeps — recording a change that did not happen, and bump the modification
            // time of an entry the user also maintains in KeePassXC.
            if (!write.Contains(variable.Key))
            {
                continue;
            }

            if (store.TrySet(project, variable.Key, variable.Value, out var rejection) != EnvSetOutcome.Rejected)
            {
                continue;
            }

            // Everything that could be refused was checked before the confirmation, so reaching
            // here means the check and the writer disagree. Nothing has been saved, so the file on
            // disk is still untouched — say so and stop rather than write part of the set.
            context.Stderr.WriteLine($"keypaste env pull: {rejection}");
            context.Stderr.WriteLine("Nothing was imported.");
            return CliApp.ExitInternalError;
        }

        vault.Save();

        context.Stderr.WriteLine(
            $"Imported {Count(created.Count + updated.Count, "variable")} into {groupPath}.");
        return CliApp.ExitSuccess;
    }

    /// <summary>
    /// Finds a pair of names that differ only in case, in the file or against the vault.
    /// </summary>
    /// <remarks>
    /// Two such names are two variables on Linux and one on Windows, so there is no import that
    /// means the same thing everywhere. <see cref="EnvStore.TrySet"/> refuses the second one
    /// anyway; catching it here means the refusal arrives before the confirmation rather than
    /// halfway through the loop.
    /// </remarks>
    private static string? Collision(
        IReadOnlyList<DotEnvVariable> variables,
        Dictionary<string, string> existing)
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var variable in variables)
        {
            if (seen.TryGetValue(variable.Key, out var other) &&
                !string.Equals(other, variable.Key, StringComparison.Ordinal))
            {
                return $"the file sets both '{other}' and '{variable.Key}', which differ only in case";
            }

            seen[variable.Key] = variable.Key;

            foreach (var name in existing.Keys)
            {
                if (string.Equals(name, variable.Key, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, variable.Key, StringComparison.Ordinal))
                {
                    return $"the project already has '{name}', which differs from '{variable.Key}' only in case";
                }
            }
        }

        return null;
    }

    /// <summary>Offers to remove the file, and says plainly what removing it does not do.</summary>
    private static int Cleanup(string sourcePath, bool deleteSource, bool keep, CliContext context)
    {
        if (FindRepository(sourcePath) is { } repository)
        {
            // Deliberately conditional, and deliberately not `git log` in a subprocess: a .git
            // directory says the file is inside a repository, not that it was ever committed.
            // Claiming history for a .gitignore'd file would be exactly the overclaim the rest of
            // this command is written to avoid, so it hands over the command instead.
            context.Stderr.WriteLine($"note: '{sourcePath}' is inside a git repository ('{repository}').");
            context.Stderr.WriteLine("      Deleting it does not remove it from git history. If it was ever");
            context.Stderr.WriteLine("      committed, the values are still in the repository and in every clone:");
            context.Stderr.WriteLine("        git log --oneline -- <path>");
            context.Stderr.WriteLine("      Treat anything that was committed as leaked.");
        }

        if (keep)
        {
            return CliApp.ExitSuccess;
        }

        if (!deleteSource)
        {
            if (!context.Prompt.IsInteractive)
            {
                // Unlike `rm`, where the deletion is the command, here the import already
                // succeeded and this is an optional epilogue. Failing the whole run over a
                // cleanup question would be wrong, and deleting silently would be worse.
                context.Stderr.WriteLine(
                    $"note: '{sourcePath}' was left in place; pass --delete-source to remove it.");
                return CliApp.ExitSuccess;
            }

            context.Stderr.WriteLine(
                "Deleting removes the file from the directory. It does not overwrite the blocks it");
            context.Stderr.WriteLine(
                "used: on an SSD, on a copy-on-write filesystem, or on any volume with snapshots or");
            context.Stderr.WriteLine(
                "backups, the old contents can outlive the file. If these values were exposed, rotate them.");

            var answer = context.Prompt.ReadLine($"Delete '{sourcePath}'? [y/N] ");
            if (answer is null || !answer.Trim().StartsWith('y') && !answer.Trim().StartsWith('Y'))
            {
                context.Stderr.WriteLine($"Left {sourcePath} in place.");
                return CliApp.ExitSuccess;
            }
        }

        try
        {
            File.Delete(sourcePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The import is already committed, so this is a partial success, and the half that
            // failed is the half that left plaintext on disk. A script should be able to see that.
            context.Stderr.WriteLine(
                $"keypaste env pull: imported, but could not delete '{sourcePath}': {ex.Message}");
            context.Stderr.WriteLine("The file still contains the values.");
            return CliApp.ExitInternalError;
        }

        context.Stderr.WriteLine($"Deleted {sourcePath}.");

        if (deleteSource)
        {
            context.Stderr.WriteLine(
                "note: deleting does not overwrite the disk blocks the file used. " +
                "If these values were exposed, rotate them.");
        }

        return CliApp.ExitSuccess;
    }

    /// <summary>The root of the git repository containing <paramref name="sourcePath"/>, if any.</summary>
    /// <remarks>
    /// A worktree and a submodule carry a <c>.git</c> <em>file</em> rather than a directory, so
    /// both are checked; looking only for the directory would miss exactly the setups where the
    /// history is somebody else's to clean up.
    /// </remarks>
    private static string? FindRepository(string sourcePath)
    {
        var directory = Path.GetDirectoryName(sourcePath);

        while (!string.IsNullOrEmpty(directory))
        {
            var candidate = Path.Combine(directory, ".git");
            if (Directory.Exists(candidate) || File.Exists(candidate))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

    private static void WriteNames(CliContext context, string label, List<string> names)
    {
        if (names.Count > 0)
        {
            context.Stderr.WriteLine($"  {label,-9} {string.Join(", ", names)}");
        }
    }

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    private static int Fail(CliContext context, string message)
    {
        context.Stderr.WriteLine($"keypaste env pull: {message}");
        return CliApp.ExitUsageError;
    }

    internal static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("usage: keypaste env pull <project> [file] [--yes] [--delete-source | --keep]");
        writer.WriteLine();
        writer.WriteLine($"imports a .env file, defaulting to ./{DefaultFileName}, then offers to delete it.");
        writer.WriteLine("if any line is malformed, every problem is reported and nothing is imported.");
        writer.WriteLine("values are stored exactly as written -- ${VAR} is not expanded.");
    }
}
