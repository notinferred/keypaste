using System.Globalization;
using Keypaste.Cli.Styling;
using Keypaste.Core;
using Keypaste.Core.Import;
using Keypaste.Infrastructure;

namespace Keypaste.Cli.Commands;

/// <summary>
/// <c>keypaste import</c> of another password manager's export: the items its format's reader reads, copied into the
/// vault in use under a group named after the file, all of them or none.
/// </summary>
/// <remarks>
/// The export is read once into memory and never written anywhere. The vault's claim is taken before its password is
/// asked, as for a KeePass file, and a dry run takes none. What is printed names items, fields and attachments, never a
/// value, and names everything that does not land.
/// </remarks>
internal static class ImportFromExport
{
    internal const string SkipExistingOption = "skip-existing";

    /// <summary>Imports <paramref name="path"/> when one of the context's readers recognizes it.</summary>
    /// <param name="path">The file, which is not a KeePass database.</param>
    /// <param name="line">The parsed command line.</param>
    /// <param name="target">The vault to copy into, or null when none is configured.</param>
    /// <param name="into">The group to copy into, or null for the file's name.</param>
    /// <param name="context">Where prompts, output and errors go.</param>
    /// <returns>The exit code, or null when no reader recognizes the file.</returns>
    internal static int? Execute(string path, CommandLine line, string? target, string? into, CliContext context)
    {
        ExportSource? opened = null;

        try
        {
            opened = ExportImport.Open(path, context.ExportReaders);
            return opened is null ? null : Import(opened, line, target, into, context);
        }
        catch (VaultException ex) when (opened is null)
        {
            context.Stderr.WriteLine($"keypaste import: {Prose(ex.Message)}");
            return CliApp.ExitInternalError;
        }
        finally
        {
            opened?.Dispose();
        }
    }

    private static int Import(ExportSource source, CommandLine line, string? target, string? into, CliContext context)
    {
        if (line.HasFlag("in-place") || line.Value("source-keyfile") is not null)
        {
            context.Stderr.WriteLine("keypaste import: --in-place and --source-keyfile are for a KeePass file; an export is copied into your vault");
            return CliApp.ExitUsageError;
        }

        if (target is null)
        {
            context.Stderr.WriteLine($"keypaste import: an export is copied into a vault: pass --vault or set {VaultLocator.EnvironmentVariable}");
            return CliApp.ExitUsageError;
        }

        if (!File.Exists(target))
        {
            context.Stderr.WriteLine($"keypaste import: no vault at '{target}'");
            return CliApp.ExitNotFound;
        }

        context.Stderr.WriteLine($"  {Prose(string.Join(Dot(context), source.FileName, source.Format, Count(source.Items.Count, "item", "items")))}");

        var skip = line.HasFlag(SkipExistingOption);

        return line.HasFlag("dry-run")
            ? VaultSession.Open(target, line, context, vault => Preview(source, vault, into, skip, context))
            : VaultSession.OpenHeld(target, line, context, vault => Apply(source, vault, into, skip, context));
    }

    private static int Preview(ExportSource source, Vault vault, string? into, bool skip, CliContext context)
    {
        var plan = Plan(source, vault, into, skip);
        var output = context.Stdout;
        var arrow = ConsoleMarks.Carried(output, "→", "->");
        var labels = plan.Rows.Select(row => EntryNameSanitizer.SanitizePath(row.Item).Text).ToList();
        var width = labels.Select(label => label.Length).DefaultIfEmpty(0).Max();

        output.WriteLine($"  {Count(plan.Landing, "item", "items")} of {source.Items.Count} to copy into {EntryNameSanitizer.SanitizePath(plan.Into).Text}");

        for (var i = 0; i < plan.Rows.Count; i++)
        {
            var row = plan.Rows[i];
            var lands = row.Lands is { } name ? $"{arrow} {Shown(name)}" : "skipped";
            var note = row.Note is { } said ? $"      ({Prose(said)})" : string.Empty;
            output.WriteLine($"    {labels[i].PadRight(width)}  {lands}{note}");
        }

        if (plan.Losses.Count > 0)
        {
            output.WriteLine("  not imported:");

            foreach (var loss in plan.Losses)
            {
                output.WriteLine($"    {Lost(loss)}");
            }
        }

        var refused = Refused(plan, context);
        output.WriteLine("  nothing was written (--dry-run)");
        return refused ? CliApp.ExitUsageError : CliApp.ExitSuccess;
    }

    private static int Apply(ExportSource source, Vault vault, string? into, bool skip, CliContext context)
    {
        var plan = Plan(source, vault, into, skip);

        if (Refused(plan, context))
        {
            context.Stderr.WriteLine("keypaste import: nothing was written");
            return CliApp.ExitUsageError;
        }

        var result = source.Commit(vault, plan, saving => saving.Save());
        var style = context.ConsoleStyle;
        var done = style.Paint(context.Stderr, Tone.Ok, style.Glyph(context.Stderr, Mark.Done));
        var arrow = ConsoleMarks.Carried(context.Stderr, "→", "->");

        context.Stderr.WriteLine(
            $"  {done} {string.Join(Dot(context), Count(result.Entries, "item", "items"), Count(plan.Projects.Count, "project", "projects"), $"copied into {EntryNameSanitizer.SanitizePath(plan.Into).Text}")}");

        foreach (var row in plan.Rows.Where(row => row.Note is not null))
        {
            var item = EntryNameSanitizer.SanitizePath(row.Item).Text;
            context.Stderr.WriteLine(row.Lands is { } name
                ? $"  note: {item} {arrow} {Shown(name)} ({Prose(row.Note!)})"
                : $"  skipped: {item} ({Prose(row.Note!)})");
        }

        foreach (var loss in plan.Losses)
        {
            context.Stderr.WriteLine($"  not imported: {Lost(loss)}");
        }

        return CliApp.ExitSuccess;
    }

    /// <summary>The plan, with every item whose name the vault already has skipped when the person asked for that.</summary>
    private static ExportPlan Plan(ExportSource source, Vault vault, string? into, bool skip)
    {
        var plan = source.Plan(vault, into);

        return skip
            ? source.Plan(vault, plan.Into, plan.Rows.Where(row => row.Collision == ExportCollision.VaultItem).ToDictionary(row => row.Index, _ => CollisionDecision.Skip))
            : plan;
    }

    /// <summary>Prints why the plan is refused on stderr, if it is.</summary>
    /// <returns>Whether it is.</returns>
    private static bool Refused(ExportPlan plan, CliContext context)
    {
        if (plan.Refusal is not { } refusal)
        {
            return false;
        }

        var cross = ConsoleMarks.Carried(context.Stderr, "✗", "x");
        context.Stderr.WriteLine(context.ConsoleStyle.Paint(context.Stderr, Tone.Danger, $"  {cross} {Prose(refusal)}"));
        return true;
    }

    private static string Lost(ExportLoss loss) =>
        loss.Index < 0
            ? $"{Prose(loss.Part)} ({Prose(loss.Reason)})"
            : $"{EntryNameSanitizer.SanitizePath(loss.Item).Text}: {Prose(loss.Part)} ({Prose(loss.Reason)})";

    private static string Shown(EntryName name) =>
        EntryNameSanitizer.SanitizePath(name.GroupPath.Length == 0 ? name.Title : $"{name.GroupPath}/{name.Title}").Text;

    private static string Prose(string text) => DisplayTextSanitizer.Sanitize(text).Text;

    private static string Dot(CliContext context) => $" {context.ConsoleStyle.Glyph(context.Stderr, Mark.Dot)} ";

    private static string Count(int value, string one, string many) =>
        value == 1 ? $"1 {one}" : string.Create(CultureInfo.InvariantCulture, $"{value} {many}");
}
