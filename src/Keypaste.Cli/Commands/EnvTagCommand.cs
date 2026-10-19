using Keypaste.Cli.Styling;
using Keypaste.Core;
using Keypaste.Core.Editing;
using Keypaste.Core.Infrastructure;

namespace Keypaste.Cli.Commands;

/// <summary>
/// Puts an entry in a project's environment, or takes it out, through the entry's own KeePass tag:
/// <c>keypaste env tag|untag &lt;project&gt; &lt;entry&gt; [-p &lt;environment&gt;]</c>.
/// </summary>
/// <remarks>
/// The tag is <c>env:&lt;project&gt;</c> for <c>dev</c> and <c>env:&lt;project&gt;:&lt;environment&gt;</c>
/// otherwise (D-0370), which KeePassXC shows and edits as an ordinary tag. Each change is one edit
/// with one revision. Before it is written the verb names the environment it reaches, the fields
/// that join or leave, never a value, and any agent set to Automatic there that would then get the
/// entry without asking, and asks (D-0415).
/// </remarks>
internal static class EnvTagCommand
{
    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
        new("yes", TakesValue: false),
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
            context.Stdout.WriteLine($"usage: keypaste env {verb} <project> <item> [-p <environment>] [--yes]");
            return CliApp.ExitSuccess;
        }

        if (line.Operands.Count != 2)
        {
            return Fail(context, verb, "expected a project and an item");
        }

        var (project, entryPath) = (line.Operands[0], line.Operands[1]);
        var environment = line.Value(EnvCommand.ProfileOption.Name) ?? EnvProfileNames.Default;

        if (!ProjectTag.TryFor(project, environment, out _, out var tagError))
        {
            return Fail(context, verb, tagError);
        }

        var assumeYes = line.HasFlag("yes");

        // Same rule as `rm`: a piped run asks for the change explicitly rather than have a
        // confirmation answered by whatever the next line of stdin happens to be.
        if (!assumeYes && !context.Prompt.IsInteractive)
        {
            return Fail(context, verb, "--yes is required when stdin is not a terminal");
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            return Fail(context, verb, locateError);
        }

        var edit = new TagItem(project, environment, entryPath, tagging);
        var route = new OwnerRoute($"env {verb}", _ => edit, (done, asked) => Done(done, project, environment, asked));

        return VaultSession.OpenHeld(path, line, context, route, vault =>
        {
            var check = TerminalEdits.Check(vault, edit, EditLines.Scene(context));
            if (check.Refusal is { } refused)
            {
                return EditLines.Refuse(context, $"env {verb}", refused);
            }

            if (check.Unchanged is { } unchanged)
            {
                return Done(unchanged, project, environment, context);
            }

            foreach (var sentence in check.Preview!.TagSentences)
            {
                context.Stderr.WriteLine(sentence);
            }

            if (!assumeYes)
            {
                var answer = context.Prompt.ReadLine($"{(tagging ? "Tag" : "Untag")} {check.Preview.Item}? [y/N] ");
                if (!EditLines.Confirmed(answer))
                {
                    context.Stderr.WriteLine("Cancelled; nothing was written.");
                    return CliApp.ExitUsageError;
                }
            }

            var result = TerminalEdits.Apply(vault, edit, EditLines.Scene(context), shown: null);
            if (result.Refusal is { } late)
            {
                return EditLines.Refuse(context, $"env {verb}", late);
            }

            vault.Save();
            return Done(result.Done!, project, environment, context);
        });
    }

    /// <summary>Says which tags the item gained or lost, or that it was already where it was asked to be.</summary>
    internal static int Done(EditDone done, string project, string environment, CliContext context)
    {
        var style = context.ConsoleStyle;
        var where = $"{Shown(project)}/{environment}";
        var mark = style.Paint(context.Stderr, Tone.Ok, style.Glyph(context.Stderr, Mark.Done));

        context.Stderr.WriteLine(done.Outcome switch
        {
            EditOutcome.Unchanged when done.Action == EditAction.EnvTag => $"{Shown(done.Item)} is already in {where}; nothing was written",
            EditOutcome.Unchanged => $"{Shown(done.Item)} is not in {where}; nothing was written",
            EditOutcome.Tagged => $"  {mark} tagged {Shown(done.Item)} {done.Names[0]}",
            _ => $"  {mark} untagged {Shown(done.Item)} {string.Join(", ", done.Names)}",
        });

        return CliApp.ExitSuccess;
    }

    private static string Shown(string path) => EntryNameSanitizer.SanitizePath(path).Text;

    private static int Fail(CliContext context, string verb, string message)
    {
        context.Stderr.WriteLine($"keypaste env {verb}: {message}");
        return CliApp.ExitUsageError;
    }
}
