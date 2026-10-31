using System.CommandLine;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Editing;
using Keypaste.Infrastructure;

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

    private static readonly Argument<string> _project = Syntax.Operand("project", "the project");
    private static readonly Argument<string?> _file = Syntax.Optional("file", $"the .env file, ./{DefaultFileName} by default");
    private static readonly Option<bool> _deleteSource = Syntax.Flag("--delete-source", "delete the file afterwards without asking");
    private static readonly Option<bool> _keep = Syntax.Flag("--keep", "keep the file without asking");

    internal static CliVerb Command { get; } = new(
        "pull",
        "import a .env file, then offer to delete it\n\n"
            + "a key the project has is updated where it lives; a new one becomes a protected field of --entry, or of the "
            + "environment's home item env/<project>/.env, created tagged. if any line is malformed, every problem is "
            + "reported and nothing is imported. values are stored exactly as written: ${VAR} is not expanded.",
        Execute)
    {
        _project, _file, CliOptions.Profile, CliOptions.Entry, CliOptions.Yes, _deleteSource, _keep, CliOptions.Vault, CliOptions.Keyfile,
    };

    private static int Execute(ParseResult line, CliContext context)
    {
        var deleteSource = line.GetValue(_deleteSource);
        var keep = line.GetValue(_keep);
        if (deleteSource && keep)
        {
            return Fail(context, "--delete-source and --keep contradict each other");
        }

        var project = line.GetValue(_project)!;
        if (!EnvConvention.IsValidProject(project, out var projectError))
        {
            return Fail(context, projectError);
        }

        if (!EnvCommand.TryProfile(line, out var profile, out var profileError))
        {
            return Fail(context, profileError);
        }

        var assumeYes = line.GetValue(CliOptions.Yes);

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

        var sourcePath = Path.GetFullPath(line.GetValue(_file) ?? DefaultFileName);

        // Everything about the file is settled before the master password is asked for: a typo in
        // the path should not cost a password entry and a key derivation to discover.
        if (!TryReadFile(sourcePath, context, out var text, out var document, out var source, out var readExit))
        {
            return readExit;
        }

        foreach (var advisory in Advisories(document))
        {
            context.Stderr.WriteLine(advisory);
        }

        var edit = new EnvPullFile(project, profile, line.GetValue(CliOptions.Entry), text);
        var route = new OwnerRoute("env pull", _ => edit, Done);
        var exit = VaultSession.OpenHeld(vaultPath, line, context, route, vault => Import(vault, edit, assumeYes, context));

        if (exit != CliApp.ExitSuccess)
        {
            return exit;
        }

        return Cleanup(sourcePath, source, deleteSource, keep, context);
    }

    /// <summary>Reads and checks the file, reporting everything wrong with it at once.</summary>
    private static bool TryReadFile(
        string sourcePath,
        CliContext context,
        out string text,
        out DotEnvDocument document,
        out SourceSnapshot source,
        out int exit)
    {
        text = string.Empty;
        document = null!;
        source = null!;

        if (!DotEnvFile.TryRead(sourcePath, out var bytes, out var failure, out var readError))
        {
            switch (failure)
            {
                case DotEnvFileFailure.Missing:
                    context.Stderr.WriteLine($"keypaste env pull: no file at '{sourcePath}'");
                    exit = CliApp.ExitNotFound;
                    break;

                case DotEnvFileFailure.TooLarge:
                    exit = Fail(context, $"'{sourcePath}': {readError}");
                    break;

                default:
                    context.Stderr.WriteLine($"keypaste env pull: could not read '{sourcePath}': {readError}");
                    exit = CliApp.ExitInternalError;
                    break;
            }

            return false;
        }

        if (!DotEnv.TryDecode(bytes, out text, out var decodeError))
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

        // Taken from the bytes just read, not from a second read of the file: what the cleanup at
        // the end is allowed to delete is exactly what the import above acted on.
        source = SourceSnapshot.Take(sourcePath, bytes);

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
            // The message quotes the offending key back, and this file arrived from somewhere else
            // — it is the one place in keypaste where untrusted bytes reach a terminal without
            // passing through the vault, so a C0 escape really can survive here.
            context.Stderr.WriteLine($"  {EntryNameSanitizer.Sanitize(problems[i].Message, 512).Text}");
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
    private static int Import(Vault vault, EnvPullFile edit, bool assumeYes, CliContext context)
    {
        var check = TerminalEdits.Check(vault, edit, EditLines.Scene(context));
        if (check.Refusal is { } refused)
        {
            return EditLines.Refuse(context, "env pull", refused);
        }

        var where = $"{edit.Project}/{edit.Environment}";

        if (check.Unchanged is { } unchanged)
        {
            return Done(unchanged, context);
        }

        var plan = check.Preview!.Plan!;
        Summary(context, where, plan.Keys);

        if (plan.CreatesHome)
        {
            context.Stderr.WriteLine($"{ApprovalPrompt.Shown(plan.Home)} will be created and tagged into {where}.");
        }

        if (plan.Updated.Count > 0)
        {
            context.Stderr.WriteLine(
                $"{Count(plan.Updated.Count, "variable")} will be updated; the previous values stay in the item history.");
        }

        if (!assumeYes)
        {
            var answer = context.Prompt.ReadLine(
                $"Import {Count(plan.Created.Count + plan.Updated.Count, "variable")} into {where}? [y/N] ");
            if (!EditLines.Confirmed(answer))
            {
                context.Stderr.WriteLine("Cancelled.");
                return CliApp.ExitUsageError;
            }
        }

        var result = TerminalEdits.Apply(vault, edit, EditLines.Scene(context), shown: null);
        if (result.Refusal is { } late)
        {
            return EditLines.Refuse(context, "env pull", late);
        }

        vault.Save();
        return Imported(result.Done!, context);
    }

    /// <summary>Says what an import found, then that nothing needed writing or what it wrote.</summary>
    internal static int Done(EditDone done, CliContext context)
    {
        Summary(context, done.Item, done.Keys);

        if (done.Outcome == EditOutcome.Unchanged)
        {
            context.Stderr.WriteLine($"{done.Item} already matches the file; nothing to do.");
            return CliApp.ExitSuccess;
        }

        return Imported(done, context);
    }

    private static int Imported(EditDone done, CliContext context)
    {
        var written = done.Keys.Count(key => key.Change != EnvWriteChange.Unchanged);
        context.Stderr.WriteLine($"Imported {Count(written, "variable")} into {done.Item}.");
        return CliApp.ExitSuccess;
    }

    /// <summary>Counts the variables by what happens to them, and names the new and updated ones by item.</summary>
    private static void Summary(CliContext context, string where, IReadOnlyList<EnvKeyWrite> keys)
    {
        context.Stderr.WriteLine(
            $"{where}: {keys.Count(key => key.Change == EnvWriteChange.New)} new, {keys.Count(key => key.Change == EnvWriteChange.Replaces)} updated, {keys.Count(key => key.Change == EnvWriteChange.Unchanged)} unchanged");
        WriteKeys(context, "new", keys, EnvWriteChange.New);
        WriteKeys(context, "updated", keys, EnvWriteChange.Replaces);
    }

    /// <summary>Offers to remove the file, and says plainly what removing it does not do.</summary>
    /// <remarks>
    /// <para>
    /// The offer is bound to what was imported, not to the path it came from. Between the read and
    /// this point sit a master-password prompt, a key derivation and two confirmations, and a
    /// person answering them is a person with an editor open — so the file is checked twice, the
    /// same shape <c>env export</c>'s vault guard has (D-0092): once here, so nobody is asked
    /// whether to delete bytes keypaste never imported, and once inside
    /// <see cref="SourceSnapshot.DeleteIfUnchanged"/>, where the answer cannot go stale between the
    /// check and the removal.
    /// </para>
    /// <para>
    /// A refusal exits nonzero only when the deletion was actually asked for — by
    /// <c>--delete-source</c>, or by a <c>y</c> at the prompt. D-0015's rule: the user asked for
    /// two things and got one, and the half that failed is the half that left plaintext on disk. A
    /// file that changed before anyone was asked cost nothing and says so on the way past.
    /// </para>
    /// </remarks>
    private static int Cleanup(
        string sourcePath,
        SourceSnapshot source,
        bool deleteSource,
        bool keep,
        CliContext context)
    {
        if (GitRepository.Find(sourcePath) is { } repository)
        {
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

        if (!source.Matches(sourcePath))
        {
            return Kept(sourcePath, requested: deleteSource, context);
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
                "backups, the old contents can outlive the file. If these values may have leaked, rotate them.");

            var answer = context.Prompt.ReadLine($"Delete '{sourcePath}'? [y/N] ");
            if (answer is null || !answer.Trim().StartsWith('y') && !answer.Trim().StartsWith('Y'))
            {
                context.Stderr.WriteLine($"Left {sourcePath} in place.");
                return CliApp.ExitSuccess;
            }
        }

        switch (source.DeleteIfUnchanged(sourcePath, out var detail))
        {
            case SourceCleanup.Deleted:
                break;

            case SourceCleanup.Missing:
                return AlreadyGone(sourcePath, context);

            case SourceCleanup.Stranded:
                // Moved out of the way and then neither deletable nor returnable — because the
                // removal failed, or because something took the original name while it was held.
                // It is the user's plaintext either way, so it is named rather than dropped, and
                // whatever is at the original path is left alone.
                context.Stderr.WriteLine(
                    $"keypaste env pull: imported, but '{sourcePath}' could not be removed.");
                context.Stderr.WriteLine(
                    $"The file keypaste read is now at '{detail}'; delete it yourself when you are ready.");
                context.Stderr.WriteLine("The file still contains the values.");
                return CliApp.ExitInternalError;

            case SourceCleanup.Unreadable:
                // The import is already committed, so this is a partial success, and the half that
                // failed is the half that left plaintext on disk. A script should be able to see that.
                context.Stderr.WriteLine(
                    $"keypaste env pull: imported, but '{sourcePath}' could not be established as" +
                    $" unchanged ({detail}), so it was not deleted.");
                context.Stderr.WriteLine("The file still contains the values.");
                return CliApp.ExitInternalError;

            default:
                context.Stderr.WriteLine(
                    $"keypaste env pull: imported, but '{sourcePath}' changed while keypaste was asking,");
                context.Stderr.WriteLine("so it was not deleted and what it holds now was not imported.");
                context.Stderr.WriteLine("The file still contains the values.");
                return CliApp.ExitInternalError;
        }

        context.Stderr.WriteLine($"Deleted {sourcePath}.");

        if (deleteSource)
        {
            context.Stderr.WriteLine(
                "note: deleting does not overwrite the disk blocks the file used. " +
                "If these values may have leaked, rotate them.");
        }

        return CliApp.ExitSuccess;
    }

    /// <summary>Says why the file is still there, and whether that failed anything.</summary>
    private static int Kept(string sourcePath, bool requested, CliContext context)
    {
        if (!File.Exists(sourcePath))
        {
            return AlreadyGone(sourcePath, context);
        }

        if (!requested)
        {
            context.Stderr.WriteLine(
                $"note: '{sourcePath}' changed after keypaste read it, so what it holds now was");
            context.Stderr.WriteLine(
                "      not imported. Leaving it in place; run keypaste env pull again to import it.");
            return CliApp.ExitSuccess;
        }

        context.Stderr.WriteLine(
            $"keypaste env pull: imported, but '{sourcePath}' changed after keypaste read it, so what");
        context.Stderr.WriteLine("it holds now was not imported and it was not deleted.");
        context.Stderr.WriteLine("The file still contains the values.");
        return CliApp.ExitInternalError;
    }

    /// <summary>
    /// Something else removed the file. <c>File.Delete</c> does not complain about a path that is
    /// not there, so the old code reported a deletion it had not performed.
    /// </summary>
    private static int AlreadyGone(string sourcePath, CliContext context)
    {
        context.Stderr.WriteLine($"note: '{sourcePath}' was already gone; keypaste deleted nothing.");
        return CliApp.ExitSuccess;
    }

    /// <summary>Names the keys of one kind, each with the entry it is written on.</summary>
    private static void WriteKeys(CliContext context, string label, IReadOnlyList<EnvKeyWrite> keys, EnvWriteChange change)
    {
        foreach (var entry in keys.Where(key => key.Change == change).GroupBy(key => key.Entry))
        {
            context.Stderr.WriteLine($"  {label,-9} {string.Join(", ", entry.Select(key => key.Key))} on {ApprovalPrompt.Shown(entry.Key)}");
        }
    }

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    private static int Fail(CliContext context, string message, int exit = CliApp.ExitUsageError)
    {
        context.Stderr.WriteLine($"keypaste env pull: {message}");
        return exit;
    }
}
