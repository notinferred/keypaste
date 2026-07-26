using Keypaste.Core;

namespace Keypaste.Cli.Commands;

/// <summary>
/// Writes a project back out as a <c>.env</c> file:
/// <c>keypaste env export &lt;project&gt; [file] --dotenv</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The one command that puts plaintext on disk.</b> CORE.md law 3.4 forbids a secret touching
/// disk unencrypted <em>by keypaste's doing</em>; here the user names the format, names the
/// destination, and answers for it, which is the same distinction that lets <c>get --show</c> exist
/// beside a clipboard that is otherwise the only way out. It is an escape hatch, and a vault you
/// cannot leave is a vault nobody should adopt — but it is loud, and it says what it just did.
/// </para>
/// <para>
/// <c>--dotenv</c> is required rather than assumed. A format that is implied today changes meaning
/// the day a second one is added, and that is not a change worth making silently on this path.
/// </para>
/// </remarks>
internal static class EnvExportCommand
{
    /// <summary>The file used when the command is given no path.</summary>
    internal const string DefaultFileName = ".env";

    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new("dotenv", TakesValue: false),
        new("stdout", TakesValue: false),
        new("yes", TakesValue: false),
        new("force", TakesValue: false),
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
            return Fail(context, "expected a project and an optional path to write");
        }

        if (!line.HasFlag("dotenv"))
        {
            return Fail(context, "a format is required; --dotenv is the only one");
        }

        var toStdout = line.HasFlag("stdout");
        var force = line.HasFlag("force");

        if (toStdout && line.Operands.Count == 2)
        {
            return Fail(context, "--stdout and a file path are two destinations; pick one");
        }

        if (toStdout && force)
        {
            return Fail(context, "--force only means anything when writing a file");
        }

        var project = line.Operands[0];
        if (!EnvConvention.IsValidProject(project, out var projectError))
        {
            return Fail(context, projectError);
        }

        var assumeYes = line.HasFlag("yes");

        // Same rule as `rm` and `env pull`. --stdout is exempt: naming that flag is the consent,
        // exactly as `get --show` is, and there is nothing left behind to answer for.
        if (!toStdout && !assumeYes && !context.Prompt.IsInteractive)
        {
            return Fail(context, "--yes is required when stdin is not a terminal");
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var vaultPath, out var locateError))
        {
            return Fail(context, locateError);
        }

        string? targetPath = null;
        if (!toStdout)
        {
            targetPath = Path.GetFullPath(line.Operands.Count == 2 ? line.Operands[1] : DefaultFileName);

            // Settled before the master password is asked for: an unwritable destination should not
            // cost a password entry and a key derivation to discover.
            if (!TryClearTheWay(targetPath, force, context, out var prepareExit))
            {
                return prepareExit;
            }
        }

        return VaultSession.Open(vaultPath, context, vault =>
            Export(vault, project, targetPath, force, assumeYes, context));
    }

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
        string project,
        string? targetPath,
        bool force,
        bool assumeYes,
        CliContext context)
    {
        var store = new EnvStore(vault);
        var groupPath = EnvConvention.GroupPath(project);

        if (!store.ProjectExists(project))
        {
            context.Stderr.WriteLine($"keypaste env export: no env set for '{project}'");
            return CliApp.ExitNotFound;
        }

        var variables = store.Read(project);

        if (!DotEnvWriter.TryFormat(variables, out var file, out var formatError))
        {
            context.Stderr.WriteLine($"keypaste env export: '{groupPath}' {formatError}");
            context.Stderr.WriteLine("Nothing was written.");
            return CliApp.ExitInternalError;
        }

        foreach (var advisory in Advisories(file.Notes))
        {
            context.Stderr.WriteLine(advisory);
        }

        return targetPath is null
            ? ToStdout(file, variables.Count, groupPath, context)
            : ToFile(file, variables.Count, groupPath, targetPath, force, assumeYes, context);
    }

    private static int ToStdout(DotEnvText file, int count, string groupPath, CliContext context)
    {
        if (count > 0)
        {
            context.ConsoleStyle.Alarm(context.Stderr, "! plaintext secrets are going to stdout");
            context.Stderr.WriteLine(
                $"  {groupPath} has {Count(count, "value")}, and they are about to leave the vault in the");
            context.Stderr.WriteLine("  clear. Whatever you pipe them into now owns a copy.");
        }

        context.Stdout.Write(file.Text);
        return CliApp.ExitSuccess;
    }

    private static int ToFile(
        DotEnvText file,
        int count,
        string groupPath,
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
                $"  {targetPath} will hold {Count(count, "value")} from {groupPath} in the clear. Anything");
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

        if (!TryWrite(targetPath, file.Utf8.Span, force, out var writeError))
        {
            context.Stderr.WriteLine($"keypaste env export: could not write '{targetPath}': {writeError}");
            return CliApp.ExitInternalError;
        }

        context.Stderr.WriteLine($"Wrote {Count(count, "value")} from {groupPath} to {targetPath}.");
        context.Stderr.WriteLine(OperatingSystem.IsWindows()
            ? "note: the file inherits its directory's permissions; keypaste does not restrict them on Windows."
            : "note: the file is readable only by you (mode 600). Delete it when you are done.");

        return CliApp.ExitSuccess;
    }

    /// <summary>Writes the file, owner-only, refusing to follow anything already there.</summary>
    /// <remarks>
    /// <see cref="FileMode.CreateNew"/> rather than <see cref="FileMode.Create"/> even under
    /// <c>--force</c>, with an explicit delete first: truncating in place would keep the old file's
    /// permissions, so the mode below would apply on a fresh export and quietly not apply on a
    /// repeat. <see cref="FileStreamOptions.UnixCreateMode"/> throws on Windows, which has no
    /// equivalent — SECURITY.md states that gap rather than implying a mode that was never set.
    /// </remarks>
    private static bool TryWrite(string path, ReadOnlySpan<byte> bytes, bool force, out string error)
    {
        try
        {
            if (force && File.Exists(path))
            {
                File.Delete(path);
            }

            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
            };

            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using var stream = new FileStream(path, options);
            stream.Write(bytes);

            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
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

        var backslash = NamesOf(notes, DotEnvWriteNoteKind.TrailingBackslash);
        if (backslash.Length > 0)
        {
            yield return $"note: {backslash} end in a backslash, which some .env readers run past " +
                "the closing quote on. Check them if another tool reads this file.";
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

    internal static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("usage: keypaste env export <project> [file] --dotenv [--stdout] [--yes] [--force]");
        writer.WriteLine();
        writer.WriteLine($"writes a project's variables as a .env file, defaulting to ./{DefaultFileName}.");
        writer.WriteLine("this is the escape hatch: it puts your secrets on disk in plain text, and asks");
        writer.WriteLine("before it does. --stdout prints them instead, for piping.");
        writer.WriteLine("prefer `keypaste run <project> -- <command>`, which needs no file at all.");
    }
}
