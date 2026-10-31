using System.CommandLine;
using Keypaste.Cli.Styling;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Infrastructure;

namespace Keypaste.Cli.Commands;

/// <summary>
/// Writes a profile out as <c>kp://</c> references, or with <c>--dotenv</c> as a plaintext
/// <c>.env</c>: <c>keypaste env export [project] [file] [-p &lt;profile&gt;] [--dotenv]</c>.
/// </summary>
/// <remarks>
/// <para>
/// By default no value is read into the output: a reference file names where each variable lives,
/// is safe to commit, and <c>keypaste run</c> resolves it (D-0349).
/// </para>
/// <para>
/// <b><c>--dotenv</c> is the one command that puts plaintext on disk.</b> docs/PRODUCT.md law 3.4 forbids a secret touching
/// disk unencrypted <em>by keypaste's doing</em>; here the user names the format, names the
/// destination, and answers for it, which is the same distinction that lets <c>get --reveal</c> exist
/// beside a clipboard that is otherwise the only way out. It is an escape hatch, and a vault you
/// cannot leave is a vault nobody should adopt — but it is loud, and it says what it just did.
/// </para>
/// <para>
/// Each value is written as it leaves keypaste, its own item's placeholders filled in, and one keypaste
/// cannot fill in refuses the whole export (D-0465), as an item that has expired does (D-0337).
/// </para>
/// </remarks>
internal static class EnvExportCommand
{
    /// <summary>The file used when the command is given no path.</summary>
    internal const string DefaultFileName = ".env";

    private static readonly Argument<string?> _project = Syntax.Optional("project", "the project, or the one projects.json maps this directory to");
    private static readonly Argument<string?> _file = Syntax.Optional("file", "where to write");
    private static readonly Option<bool> _dotenv = Syntax.Flag("--dotenv", $"write the values instead, as a .env file, ./{DefaultFileName} by default");
    private static readonly Option<bool> _stdout = Syntax.Flag("--stdout", "print the values rather than write them");
    private static readonly Option<bool> _force = Syntax.Flag("--force", "replace a file that is there");

    internal static CliVerb Command { get; } = new(
        "export",
        "write kp:// references, safe to commit; --dotenv for plain text\n\n"
            + $"writes a kp:// reference for every variable of an environment, as a {EnvReferenceFile.FileName} that holds "
            + "no value and is safe to commit, which `keypaste run` resolves. --dotenv puts your secrets on disk in plain "
            + "text, and asks first; prefer `keypaste run <project> -- <command>`, which needs no file at all.",
        Execute)
    {
        _project, _file, CliOptions.Profile, _dotenv, _stdout, CliOptions.Yes, _force, CliOptions.Vault, CliOptions.Keyfile,
    };

    private static int Execute(ParseResult line, CliContext context)
    {
        var dotenv = line.GetValue(_dotenv);
        var named = line.GetValue(_project);
        var file = line.GetValue(_file);

        if (dotenv && named is null)
        {
            return Fail(context, "expected a project and an optional path to write");
        }

        if (!EnvCommand.TryProfile(line, out var profile, out var profileError))
        {
            return Fail(context, profileError);
        }

        var toStdout = line.GetValue(_stdout);
        var force = line.GetValue(_force);

        if (toStdout && file is not null)
        {
            return Fail(context, "--stdout and a file path are two destinations; pick one");
        }

        if (toStdout && force)
        {
            return Fail(context, "--force only means anything when writing a file");
        }

        if (named is not null && !EnvConvention.IsValidProject(named, out var projectError))
        {
            return Fail(context, projectError);
        }

        var assumeYes = line.GetValue(CliOptions.Yes);

        // Same rule as `rm` and `env pull`. --stdout is exempt: naming that flag is the consent,
        // exactly as `get --reveal` is, and there is nothing left behind to answer for.
        if (dotenv && !toStdout && !assumeYes && !context.Prompt.IsInteractive)
        {
            return Fail(context, "--yes is required when stdin is not a terminal");
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var vaultPath, out var locateError))
        {
            return Fail(context, locateError);
        }

        string project;

        if (named is not null)
        {
            project = named;
        }
        else if (EnvCommand.TryInferProject(context, vaultPath, out var inferred, out var inferError))
        {
            project = inferred;
        }
        else
        {
            return Fail(context, inferError);
        }

        if (!dotenv)
        {
            return ExportReferences(line, context, vaultPath, project, profile, toStdout, force);
        }

        string? targetPath = null;
        if (!toStdout)
        {
            targetPath = Path.GetFullPath(file ?? DefaultFileName);

            // Before the check below, which would otherwise answer a vault with the one sentence
            // that must never be said about it: pass --force to overwrite it.
            if (!TryRefuseTheVault(vaultPath, targetPath, context, out var guardExit))
            {
                return guardExit;
            }

            // Settled before the master password is asked for: an unwritable destination should not
            // cost a password entry and a key derivation to discover.
            if (!TryClearTheWay(targetPath, force, context, out var prepareExit))
            {
                return prepareExit;
            }
        }

        return VaultSession.Open(vaultPath, line, context, vault =>
            Export(vault, vaultPath, project, profile, targetPath, force, assumeYes, context));
    }

    /// <summary>Writes a reference for every key of the profile, to a file or to stdout, and never a value.</summary>
    private static int ExportReferences(
        ParseResult line, CliContext context, string vaultPath, string project, string profile, bool toStdout, bool force)
    {
        var targetPath = !toStdout && line.GetValue(_file) is { } file ? Path.GetFullPath(file) : null;

        if (targetPath is not null
            && (!TryRefuseTheVault(vaultPath, targetPath, context, out var exit) || !TryClearTheWay(targetPath, force, context, out exit)))
        {
            return exit;
        }

        return VaultSession.Open(vaultPath, line, context, vault =>
        {
            var export = EnvReferenceExport.Run(vault, project, profile, targetPath, force);

            switch (export.Outcome)
            {
                case EnvReferenceExportOutcome.NoProject or EnvReferenceExportOutcome.NoProfile:
                    return Missing(export.Outcome == EnvReferenceExportOutcome.NoProject ? EnvOutcome.NoProject : EnvOutcome.NoProfile, project, profile, context)
                        ?? CliApp.ExitNotFound;

                case EnvReferenceExportOutcome.Refused:
                    context.Stderr.WriteLine($"keypaste env export: {export.Problem}");
                    context.Stderr.WriteLine("Nothing was written.");
                    return CliApp.ExitInternalError;

                case EnvReferenceExportOutcome.OverVault:
                    return VaultOverwriteGuard.Refuse("keypaste env export", vaultPath, targetPath!, export.Overwrite, "a .env", context);

                case EnvReferenceExportOutcome.Unwritable:
                    context.Stderr.WriteLine($"keypaste env export: could not write '{targetPath}': {export.Problem}");
                    return CliApp.ExitInternalError;
            }

            if (targetPath is null)
            {
                context.Stdout.Write(export.Text);
            }

            var style = context.ConsoleStyle;
            var dot = style.Glyph(context.Stderr, Mark.Dot);
            context.Stderr.WriteLine(
                $"  {style.Paint(context.Stderr, Tone.Ok, style.Glyph(context.Stderr, Mark.Done))} wrote " +
                $"{Count(export.Count, "reference")} {dot} 0 values {dot} safe to commit");

            return CliApp.ExitSuccess;
        });
    }

    /// <summary>Says a project or its profile is not there, or null when both are.</summary>
    private static int? Missing(EnvOutcome outcome, string project, string profile, CliContext context)
    {
        switch (outcome)
        {
            case EnvOutcome.NoProject:
                context.Stderr.WriteLine($"keypaste env export: no env set for '{project}'");
                return CliApp.ExitNotFound;

            case EnvOutcome.NoProfile:
                context.Stderr.WriteLine($"keypaste env export: '{project}' has no '{profile}' environment");
                return CliApp.ExitNotFound;

            default:
                return null;
        }
    }

    /// <summary>Refuses a key two entries hold, naming them, or null when every key has one.</summary>
    private static int? Repeated(EnvListing listing, CliContext context)
    {
        if (EnvReferenceExport.RepeatedKey(listing) is not { } repeated)
        {
            return null;
        }

        context.Stderr.WriteLine($"keypaste env export: {repeated}; nothing was written.");
        return CliApp.ExitInternalError;
    }

    // Only --dotenv writes values; a reference to such a field is refused when it is resolved.
    private static int? NamedLikeStandard(EnvListing listing, CliContext context)
    {
        if (listing.Sources.FirstOrDefault(EnvResolution.IsNamedLikeStandard) is not { } named)
        {
            return null;
        }

        context.Stderr.WriteLine(
            $"keypaste env export: {EntryNameSanitizer.Sanitize(named.Key).Text} is a custom field named like a standard one, which keypaste never releases ({ApprovalPrompt.Shown(named.Entry)}); nothing was written.");
        return CliApp.ExitInternalError;
    }

    /// <summary>
    /// Refuses the export when a variable's entry has expired, as <c>run</c> does (D-0337), or its value holds a
    /// placeholder keypaste cannot fill in (D-0465), naming each variable and its entry; null when every value may leave.
    /// </summary>
    private static int? Unusable(IReadOnlyList<EnvProblem> refused, CliContext context)
    {
        if (refused.Count == 0)
        {
            return null;
        }

        var reasons = string.Join("; ", refused.Select(problem => $"{EntryNameSanitizer.Sanitize(problem.Key).Text} {problem.Reason}"));
        context.Stderr.WriteLine($"keypaste env export: {reasons}; nothing was written.");
        return CliApp.ExitInternalError;
    }

    private static bool TryRefuseTheVault(string vaultPath, string targetPath, CliContext context, out int exit) =>
        VaultOverwriteGuard.TryRefuse("keypaste env export", vaultPath, targetPath, "a .env", context, out exit);

    /// <summary>Checks the destination is writable, and refuses to clobber anything by default.</summary>
    private static bool TryClearTheWay(string targetPath, bool force, CliContext context, out int exit)
    {
        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            context.Stderr.WriteLine($"keypaste env export: no directory '{directory}'");
            exit = CliApp.ExitNotFound;
            return false;
        }

        if (!force && File.Exists(targetPath))
        {
            // The `init` precedent. Overwriting a .env is how somebody loses the handful of
            // variables they had not got round to importing yet.
            exit = Fail(context, $"'{targetPath}' already exists; pass --force to overwrite it");
            return false;
        }

        exit = CliApp.ExitSuccess;
        return true;
    }

    private static int Export(
        Vault vault,
        string vaultPath,
        string project,
        string profile,
        string? targetPath,
        bool force,
        bool assumeYes,
        CliContext context)
    {
        var listing = EnvResolution.ListFilled(vault, project, profile, context.Clock, out var unusable);
        var setName = EnvProfileNames.SetName(project, profile);

        if ((Missing(listing.Outcome, project, profile, context)
            ?? Repeated(listing, context)
            ?? NamedLikeStandard(listing, context)
            ?? Unusable(unusable, context)) is { } refused)
        {
            return refused;
        }

        var variables = listing.Variables;

        if (!DotEnvWriter.TryFormat(variables, out var file, out var formatError))
        {
            context.Stderr.WriteLine($"keypaste env export: '{setName}' {formatError}");
            context.Stderr.WriteLine("Nothing was written.");
            return CliApp.ExitInternalError;
        }

        foreach (var advisory in Advisories(file.Notes))
        {
            context.Stderr.WriteLine(advisory);
        }

        return targetPath is null
            ? ToStdout(file, variables.Count, setName, context)
            : ToFile(file, variables.Count, setName, vaultPath, targetPath, force, assumeYes, context);
    }

    private static int ToStdout(DotEnvText file, int count, string setName, CliContext context)
    {
        if (count > 0)
        {
            context.ConsoleStyle.Alarm(context.Stderr, "! plaintext secrets are going to stdout");
            context.Stderr.WriteLine(
                $"  {setName} has {Count(count, "value")}, and they are about to leave the vault in the");
            context.Stderr.WriteLine("  clear. Whatever you pipe them into now owns a copy.");
        }

        context.Stdout.Write(file.Text);
        return CliApp.ExitSuccess;
    }

    private static int ToFile(
        DotEnvText file,
        int count,
        string setName,
        string vaultPath,
        string targetPath,
        bool force,
        bool assumeYes,
        CliContext context)
    {
        // No secret, no alarm. Shouting about an empty file is how a warning becomes furniture.
        if (count > 0)
        {
            context.ConsoleStyle.Alarm(context.Stderr, "! plaintext secrets are about to be written to disk");
            context.Stderr.WriteLine(
                $"  {targetPath} will hold {Count(count, "value")} from {setName} in the clear. Anything");
            context.Stderr.WriteLine(
                "  that can read the file can read them, including your editor's swap file and");
            context.Stderr.WriteLine(
                "  your backups. `keypaste run` injects these without a file; this is the way out.");
        }

        if (GitRepository.Find(targetPath) is { } repository)
        {
            context.Stderr.WriteLine($"note: '{targetPath}' is inside a git repository ('{repository}').");
            context.Stderr.WriteLine("      Add it to .gitignore before you commit anything.");
        }

        if (!assumeYes && count > 0)
        {
            var answer = context.Prompt.ReadLine($"Write {Count(count, "value")} to '{targetPath}'? [y/N] ");
            if (answer is null || !answer.Trim().StartsWith('y') && !answer.Trim().StartsWith('Y'))
            {
                context.Stderr.WriteLine("Cancelled.");
                return CliApp.ExitUsageError;
            }
        }

        // Asked again, because the answer can have changed: the password prompt sits between the
        // first check and this line, and it is the destination being deleted that has to be safe.
        if (!TryRefuseTheVault(vaultPath, targetPath, context, out var guardExit))
        {
            return guardExit;
        }

        if (!DotEnvFile.TryWrite(targetPath, file.Utf8.Span, force, out var writeError))
        {
            context.Stderr.WriteLine($"keypaste env export: could not write '{targetPath}': {writeError}");
            return CliApp.ExitInternalError;
        }

        context.Stderr.WriteLine($"Wrote {Count(count, "value")} from {setName} to {targetPath}.");

        if (count > 0)
        {
            context.Stderr.WriteLine(OperatingSystem.IsWindows()
                ? "note: the file inherits its directory's permissions; keypaste does not restrict them on Windows."
                : "note: the file is readable only by you (mode 600).");
            context.Stderr.WriteLine("      Delete it when you are done; keypaste cannot take it back.");
        }

        return CliApp.ExitSuccess;
    }

    /// <summary>What the writer had to do that a reader might disagree with. Keys, never values.</summary>
    private static IEnumerable<string> Advisories(IReadOnlyList<DotEnvWriteNote> notes)
    {
        var quoted = NamesOf(notes, DotEnvWriteNoteKind.EscapeDialect);
        if (quoted.Length > 0)
        {
            yield return $"note: {quoted} needed double quotes, and not every .env reader processes " +
                "the escapes in that form the way keypaste does. Check them if another tool reads this file.";
        }
    }

    private static string NamesOf(IReadOnlyList<DotEnvWriteNote> notes, DotEnvWriteNoteKind kind) =>
        string.Join(", ", notes.Where(n => n.Kind == kind).Select(n => n.Key));

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    private static int Fail(CliContext context, string message)
    {
        context.Stderr.WriteLine($"keypaste env export: {message}");
        return CliApp.ExitUsageError;
    }
}
