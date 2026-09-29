using Keypaste.Cli.Styling;
using Keypaste.Core;

namespace Keypaste.Cli.Commands;

/// <summary>
/// Puts an entry in a project's environment, or takes it out, through the entry's own KeePass tag:
/// <c>keypaste env tag|untag &lt;project&gt; &lt;entry&gt; [-p &lt;environment&gt;]</c>.
/// </summary>
/// <remarks>
/// The tag is <c>env:&lt;project&gt;</c> for <c>dev</c> and <c>env:&lt;project&gt;:&lt;environment&gt;</c>
/// otherwise (D-0370), which KeePassXC shows and edits as an ordinary tag. Each change is one edit
/// with one revision, and it names the fields that join or leave, never a value.
/// </remarks>
internal static class EnvTagCommand
{
    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
        EnvCommand.ProfileOption,
    ];

    internal static int Execute(string[] args, CliContext context, bool tagging)
    {
        var verb = tagging ? "tag" : "untag";

        if (!CommandLine.TryParse(args, 2, _options, out var line, out var error))
        {
            return Fail(context, verb, error);
        }

        if (line.WantsHelp)
        {
            context.Stdout.WriteLine($"usage: keypaste env {verb} <project> <entry> [-p <environment>]");
            return CliApp.ExitSuccess;
        }

        if (line.Operands.Count != 2)
        {
            return Fail(context, verb, "expected a project and an entry");
        }

        var (project, entryPath) = (line.Operands[0], line.Operands[1]);
        var environment = line.Value(EnvCommand.ProfileOption.Name) ?? EnvProfileNames.Default;

        if (!ProjectTag.TryFor(project, environment, out var tag, out var tagError))
        {
            return Fail(context, verb, tagError);
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            return Fail(context, verb, locateError);
        }

        return VaultSession.OpenHeld(path, line, context, vault =>
        {
            if (vault.Find(entryPath) is not { } entry)
            {
                context.Stderr.WriteLine($"keypaste env {verb}: no entry '{entryPath}'");
                return CliApp.ExitNotFound;
            }

            var name = EntryName.Of(entry);

            if (ReservedGroups.IsReserved(name.GroupPath))
            {
                context.Stderr.WriteLine($"keypaste env {verb}: {Shown(entry.Path)} is in keypaste's own group; it belongs to no project");
                return CliApp.ExitUsageError;
            }

            var matching = (vault.Tags(name) ?? [])
                .Where(held => ProjectTag.Read(held) is { Kind: ProjectTagKind.Member } read
                    && string.Equals(read.Project, project, StringComparison.Ordinal)
                    && string.Equals(read.Environment, environment, StringComparison.Ordinal))
                .ToList();

            var style = context.ConsoleStyle;
            var where = $"{Shown(project)}/{environment}";

            if (tagging ? matching.Count > 0 : matching.Count == 0)
            {
                context.Stderr.WriteLine(tagging
                    ? $"{Shown(entry.Path)} is already in {where}; nothing was written"
                    : $"{Shown(entry.Path)} is not in {where}; nothing was written");
                return CliApp.ExitSuccess;
            }

            _ = tagging ? vault.AddTag(name, tag) : vault.RemoveTags(name, matching);
            vault.Save();

            var fields = (vault.Fields(name) ?? [])
                .Where(field => EnvConvention.IsEnvNamedField(field.Name))
                .Select(field => EntryNameSanitizer.Sanitize(field.Name).Text)
                .ToList();

            var done = style.Paint(context.Stderr, Tone.Ok, style.Glyph(context.Stderr, Mark.Done));
            context.Stderr.WriteLine(tagging
                ? $"  {done} tagged {Shown(entry.Path)} {tag}"
                : $"  {done} untagged {Shown(entry.Path)} {string.Join(", ", matching)}");
            context.Stderr.WriteLine(fields.Count == 0
                ? $"    no env-named field {(tagging ? "joins" : "leaves")} {where}"
                : $"    {(tagging ? "joins" : "leaves")} {where}: {string.Join(", ", fields)}");

            return CliApp.ExitSuccess;
        });
    }

    private static string Shown(string path) => EntryNameSanitizer.SanitizePath(path).Text;

    private static int Fail(CliContext context, string verb, string message)
    {
        context.Stderr.WriteLine($"keypaste env {verb}: {message}");
        return CliApp.ExitUsageError;
    }
}
