using System.Globalization;
using Keypaste.Cli.Styling;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Import;
using Keypaste.Core.Ownership;
using Keypaste.Core.Recent;

namespace Keypaste.Cli.Commands;

/// <summary>
/// Brings another KDBX file in: <c>keypaste import &lt;file.kdbx&gt;</c> copies its entries into the
/// vault in use, or, where no vault is configured or with <c>--in-place</c>, keeps editing it where it is.
/// </summary>
/// <remarks>
/// <para>
/// The source is opened read-only and never saved. A copy takes the target's claim before either
/// password is asked for, so a vault the app or an agent holds is refused before anybody types, and
/// is saved only when no row is blocked.
/// </para>
/// <para>
/// The claim is taken here rather than through <see cref="VaultSession.OpenHeld"/> because the
/// source's password is asked for first, between the claim and the target's master password.
/// </para>
/// </remarks>
internal static class ImportCommand
{
    private const string _none = "none";

    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
        new("into", TakesValue: true),
        new("dry-run", TakesValue: false),
        new("in-place", TakesValue: false),
        new("source-keyfile", TakesValue: true),
    ];

    internal static int Execute(string[] args, CliContext context)
    {
        if (!CommandLine.TryParse(args, 1, _options, out var line, out var error))
        {
            context.Stderr.WriteLine($"keypaste import: {error}");
            return CliApp.ExitUsageError;
        }

        if (line.WantsHelp)
        {
            WriteHelp(context.Stdout);
            return CliApp.ExitSuccess;
        }

        if (line.Operands.Count != 1)
        {
            context.Stderr.WriteLine("keypaste import: expected exactly one .kdbx file");
            return CliApp.ExitUsageError;
        }

        var into = line.Value("into");
        var hasTarget = VaultLocator.TryResolve(line, context.Environment, out var target, out _);
        var inPlace = line.HasFlag("in-place") || !hasTarget;

        if (inPlace && into is not null)
        {
            context.Stderr.WriteLine(line.HasFlag("in-place")
                ? "keypaste import: --into copies into a vault, and --in-place keeps editing the file itself; choose one"
                : $"keypaste import: --into needs a vault to copy into: pass --vault or set {VaultLocator.EnvironmentVariable}");
            return CliApp.ExitUsageError;
        }

        var source = Path.GetFullPath(line.Operands[0]);
        if (!File.Exists(source))
        {
            context.Stderr.WriteLine($"keypaste import: no file at '{source}'");
            return CliApp.ExitNotFound;
        }

        if (!KdbxImport.TryProbe(source, out var probe, out var probeError))
        {
            context.Stderr.WriteLine($"keypaste import: {probeError}");
            return CliApp.ExitInternalError;
        }

        if (!TrySourceKeyfile(line, probe, context, out var keyfile))
        {
            return CliApp.ExitNotFound;
        }

        if (!inPlace)
        {
            if (!File.Exists(target))
            {
                context.Stderr.WriteLine($"keypaste import: no vault at '{target}'");
                return CliApp.ExitNotFound;
            }

            if (PathIdentity.SameFile(source, target))
            {
                context.Stderr.WriteLine("keypaste import: that is the vault you are importing into");
                return CliApp.ExitUsageError;
            }
        }

        context.Stderr.WriteLine($"  {Describe(probe, keyfile, Dot(context))}");

        return inPlace
            ? KeepInPlace(probe, keyfile, line.HasFlag("dry-run"), context)
            : Copy(probe, keyfile, target, line, into, context);
    }

    private static int Copy(KdbxProbe probe, string? keyfile, string target, CommandLine line, string? into, CliContext context)
    {
        var dryRun = line.HasFlag("dry-run");
        VaultClaim? claim = null;

        if (!dryRun)
        {
            var home = KeypasteHome.Resolve(context.Environment.Get(KeypasteHome.EnvironmentVariable));
            if (!VaultClaim.TryAcquire(home, target, OwnerKind.CommandLine, out claim, out var refusal))
            {
                context.Stderr.WriteLine($"keypaste: {refusal}");
                return CliApp.ExitInternalError;
            }
        }

        using (claim)
        {
            return WithSource(probe, keyfile, context, opened => VaultSession.Open(target, line, context, vault => dryRun
                ? Preview(opened, vault, into, context)
                : Apply(opened, vault, into, context)));
        }
    }

    private static int Preview(ImportSource source, Vault vault, string? into, CliContext context)
    {
        var plan = source.DefaultPlan(vault, into);
        var problems = source.Check(vault, plan);
        var output = context.Stdout;
        var arrow = ConsoleMarks.Carried(output, "→", "->");

        output.WriteLine($"  {Count(source.EntryCount, "entry", "entries")} in {Count(plan.Rows.Count + source.Skipped.Count, "group", "groups")}");

        var labels = plan.Rows.Select(row => Label(row.SourceGroup, row.IsRootEntries))
            .Concat(source.Skipped.Select(skip => Label(skip.SourceGroup, isRoot: false)))
            .ToList();
        var width = labels.Select(label => label.Length).DefaultIfEmpty(0).Max();
        var countWidth = plan.Rows.Select(row => row.EntryCount)
            .Concat(source.Skipped.Select(skip => skip.EntryCount))
            .DefaultIfEmpty(0).Max().ToString(CultureInfo.InvariantCulture).Length;

        for (var i = 0; i < plan.Rows.Count; i++)
        {
            var row = plan.Rows[i];
            var destination = EntryNameSanitizer.SanitizePath(row.Destination).Text;
            var note = row.Rerouted is { } rerouted
                ? $"      ({EntryNameSanitizer.SanitizeProse(rerouted, 256).Text})"
                : string.Empty;
            output.WriteLine($"    {labels[i].PadRight(width)}  {Number(row.EntryCount, countWidth)}  {arrow} {destination}{note}");
        }

        for (var i = 0; i < source.Skipped.Count; i++)
        {
            output.WriteLine($"    {labels[plan.Rows.Count + i].PadRight(width)}  {Number(source.Skipped[i].EntryCount, countWidth)}  skipped");
        }

        var blocked = Report(plan, problems, context);
        output.WriteLine("  nothing was written (--dry-run)");
        return blocked ? CliApp.ExitUsageError : CliApp.ExitSuccess;
    }

    private static int Apply(ImportSource source, Vault vault, string? into, CliContext context)
    {
        var plan = source.DefaultPlan(vault, into);

        if (Report(plan, source.Check(vault, plan), context))
        {
            context.Stderr.WriteLine("keypaste import: nothing was written");
            return CliApp.ExitUsageError;
        }

        var result = source.ApplyTo(vault, plan);
        vault.Save();

        var style = context.ConsoleStyle;
        var done = style.Paint(context.Stderr, Tone.Ok, style.Glyph(context.Stderr, Mark.Done));
        context.Stderr.WriteLine(
            $"  {done} {string.Join(Dot(context), Count(result.Entries, "entry", "entries"), Count(source.ProjectCount, "project", "projects"), $"copied into {EntryNameSanitizer.SanitizePath(plan.Into).Text}")}");

        if (result.DuplicateTitles > 0)
        {
            context.Stderr.WriteLine(
                $"  {Count(result.DuplicateTitles, "copied entry shares", "copied entries share")} a group and title with another entry; every one was kept");
        }

        return CliApp.ExitSuccess;
    }

    private static int KeepInPlace(KdbxProbe probe, string? keyfile, bool dryRun, CliContext context)
    {
        var exit = WithSource(probe, keyfile, context, opened =>
        {
            var style = context.ConsoleStyle;
            var done = style.Paint(context.Stderr, Tone.Ok, style.Glyph(context.Stderr, Mark.Done));
            context.Stderr.WriteLine(
                $"  {done} {string.Join(Dot(context), Count(opened.EntryCount, "entry", "entries"), Count(opened.ProjectCount, "project", "projects"), "editing in place")}");
            return CliApp.ExitSuccess;
        });

        if (exit != CliApp.ExitSuccess)
        {
            return exit;
        }

        if (dryRun)
        {
            context.Stderr.WriteLine("  nothing was written (--dry-run)");
            return CliApp.ExitSuccess;
        }

        var recent = KeypasteHome.RecentPath(context.Environment.Get(KeypasteHome.EnvironmentVariable));
        RecentVaults.Save(recent, RecentVaults.Remember(RecentVaults.Load(recent), probe.Path, context.Clock.GetUtcNow(), keyfile));

        var pass = keyfile is null ? $"--vault {probe.Path}" : $"--vault {probe.Path} --keyfile {keyfile}";
        context.Stderr.WriteLine(
            $"  the desktop app lists it under recent vaults; from the CLI pass {pass} or set {VaultLocator.EnvironmentVariable}");
        return CliApp.ExitSuccess;
    }

    /// <summary>Asks for the source's password, unlocks it and runs <paramref name="body"/>, mapping every failure to an exit code.</summary>
    /// <remarks>The password buffer is zeroed before <paramref name="body"/> runs, and the source's key when it returns.</remarks>
    private static int WithSource(KdbxProbe probe, string? keyfile, CliContext context, Func<ImportSource, int> body)
    {
        using var password = context.Prompt.ReadSecret($"Password for {probe.FileName}: ");
        if (password is null)
        {
            context.Stderr.WriteLine($"keypaste import: no password given for {probe.FileName}");
            return CliApp.ExitAuthFailed;
        }

        ImportSource? opened = null;
        try
        {
            opened = KdbxImport.Open(probe.Path, password.Value, keyfile);
            password.Dispose();
            return body(opened);
        }
        catch (InvalidMasterPasswordException) when (opened is null)
        {
            context.Stderr.WriteLine(keyfile is null
                ? $"keypaste import: wrong password for {probe.FileName}"
                : $"keypaste import: wrong password or key file for {probe.FileName}");
            return CliApp.ExitAuthFailed;
        }
        catch (UnreadableKeyfileException ex) when (opened is null)
        {
            context.Stderr.WriteLine($"keypaste import: {ex.Message}");
            return CliApp.ExitNotFound;
        }
        catch (VaultException ex) when (opened is null)
        {
            context.Stderr.WriteLine($"keypaste import: {ex.Message}");
            return CliApp.ExitInternalError;
        }
        finally
        {
            opened?.Dispose();
        }
    }

    /// <summary>Prints every blocked row, and every note, on stderr.</summary>
    /// <returns>Whether anything blocks.</returns>
    private static bool Report(ImportPlan plan, IReadOnlyList<ImportProblem> problems, CliContext context)
    {
        var cross = ConsoleMarks.Carried(context.Stderr, "✗", "x");
        var arrow = ConsoleMarks.Carried(context.Stderr, "→", "->");
        var blocked = false;

        foreach (var problem in problems)
        {
            var message = DisplayTextSanitizer.Sanitize(problem.Message).Text;
            var row = plan.Rows.FirstOrDefault(candidate => candidate.Index == problem.Index);
            var where = row is null
                ? string.Empty
                : $"{Label(row.SourceGroup, row.IsRootEntries)} {arrow} {EntryNameSanitizer.SanitizePath(row.Destination).Text}: ";

            if (problem.Blocks)
            {
                blocked = true;
                context.Stderr.WriteLine(context.ConsoleStyle.Paint(context.Stderr, Tone.Danger, $"  {cross} {where}{message}"));
            }
            else
            {
                context.Stderr.WriteLine($"  note: {where}{message}");
            }
        }

        return blocked;
    }

    private static bool TrySourceKeyfile(CommandLine line, KdbxProbe probe, CliContext context, out string? keyfile)
    {
        var given = line.Value("source-keyfile");
        keyfile = given switch
        {
            null => probe.SiblingKeyfile,
            _none => null,
            _ => Path.GetFullPath(given),
        };

        if (keyfile is not null && !File.Exists(keyfile))
        {
            context.Stderr.WriteLine($"keypaste import: no keyfile at '{keyfile}'");
            return false;
        }

        return true;
    }

    private static string Describe(KdbxProbe probe, string? keyfile, string separator)
    {
        var parts = new List<string> { probe.FileName, probe.Version, probe.Kdf, probe.Cipher };
        if (keyfile is not null)
        {
            parts.Add(string.Equals(keyfile, probe.SiblingKeyfile, StringComparison.Ordinal)
                ? $"key file found: {Path.GetFileName(keyfile)}"
                : $"key file: {Path.GetFileName(keyfile)}");
        }

        return EntryNameSanitizer.SanitizeProse(string.Join(separator, parts), 512).Text;
    }

    private static string Dot(CliContext context) => $" {context.ConsoleStyle.Glyph(context.Stderr, Mark.Dot)} ";

    private static string Label(string sourceGroup, bool isRoot) =>
        isRoot ? "(top level)" : EntryNameSanitizer.SanitizePath(sourceGroup).Text;

    private static string Number(int value, int width) =>
        value.ToString(CultureInfo.InvariantCulture).PadLeft(width);

    private static string Count(int value, string one, string many) =>
        value == 1 ? $"1 {one}" : string.Create(CultureInfo.InvariantCulture, $"{value} {many}");

    /// <summary>The glyph where the writer's encoding carries it, otherwise its ASCII stand-in.</summary>
    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("usage: keypaste import <file.kdbx> [--into <group>] [--dry-run] [--in-place]");
        writer.WriteLine("                       [--source-keyfile <path|none>] [--vault <path>] [--keyfile <path>]");
        writer.WriteLine();
        writer.WriteLine("  copies every entry of another KeePass file into your vault, with its fields, attachments");
        writer.WriteLine("  and history, under a group named after the file. An env set that is valid and new to your");
        writer.WriteLine("  vault keeps its env/... path; any other env group goes under that group too. The file");
        writer.WriteLine("  itself is never changed. Its recycle bin is left behind. With no vault configured, or");
        writer.WriteLine("  --in-place, the file is kept where it is and remembered as a vault to open.");
        writer.WriteLine();
        writer.WriteLine("  --into <group>            the group to copy into (default: the file's name)");
        writer.WriteLine("  --dry-run                 show where each group would land, and write nothing");
        writer.WriteLine("  --in-place                keep editing the file where it is instead of copying it");
        writer.WriteLine("  --source-keyfile <path>   the file's keyfile; a <name>.keyx or <name>.key beside it is used");
        writer.WriteLine("                            unless this says none");
    }
}
