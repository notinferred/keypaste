using System.CommandLine;
using Keypaste.Core.Approval;
using Keypaste.Core.Editing;
using Keypaste.Infrastructure;

namespace Keypaste.Cli.Commands;

/// <summary>Removes one variable: <c>keypaste env rm &lt;project&gt; &lt;KEY&gt; [-p &lt;profile&gt;] [--entry &lt;entry&gt;]</c>.</summary>
/// <remarks>The field leaves the entry tagged into the environment, whose history keeps the value; no entry is deleted.</remarks>
internal static class EnvRemoveCommand
{
    private static readonly Argument<string> _project = Syntax.Operand("project", "the project");
    private static readonly Argument<string> _key = Syntax.Operand("KEY", "the variable");

    internal static CliVerb Command { get; } = new("rm", "remove a variable", Execute)
    {
        _project, _key, CliOptions.Profile, CliOptions.Entry, CliOptions.Yes, CliOptions.Vault, CliOptions.Keyfile,
    };

    private static int Execute(ParseResult line, CliContext context)
    {
        if (!EnvCommand.TryProfile(line, out var profile, out var profileError))
        {
            context.Stderr.WriteLine($"keypaste env rm: {profileError}");
            return CliApp.ExitUsageError;
        }

        var project = line.GetValue(_project)!;
        var key = line.GetValue(_key)!;
        var assumeYes = line.GetValue(CliOptions.Yes);

        // Same rule as `keypaste rm`: a piped run must ask for the deletion explicitly rather than
        // have a confirmation answered by whatever the next line of stdin happens to be.
        if (!assumeYes && !context.Prompt.IsInteractive)
        {
            context.Stderr.WriteLine("keypaste env rm: --yes is required when stdin is not a terminal");
            return CliApp.ExitUsageError;
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            context.Stderr.WriteLine($"keypaste env rm: {locateError}");
            return CliApp.ExitUsageError;
        }

        var edit = new EnvRemoveKey(project, profile, key, line.GetValue(CliOptions.Entry));
        var route = new OwnerRoute("env rm", _ => edit, Done);

        return VaultSession.OpenHeld(path, line, context, route, vault =>
        {
            var check = TerminalEdits.Check(vault, edit, EditLines.Scene(context));
            if (check.Refusal is { } refused)
            {
                return EditLines.Refuse(context, "env rm", refused);
            }

            if (!assumeYes)
            {
                var answer = context.Prompt.ReadLine($"Remove {key} from {check.Preview!.Item}? Its value stays in the item's history. [y/N] ");
                if (!EditLines.Confirmed(answer))
                {
                    context.Stderr.WriteLine("Cancelled.");
                    return CliApp.ExitUsageError;
                }
            }

            var result = TerminalEdits.Apply(vault, edit, EditLines.Scene(context), shown: null);
            if (result.Refusal is { } late)
            {
                return EditLines.Refuse(context, "env rm", late);
            }

            vault.Save();
            return Done(result.Done!, context);
        });
    }

    /// <summary>Says which item the variable left, and that its value stays in that item's history.</summary>
    internal static int Done(EditDone done, CliContext context)
    {
        context.Stderr.WriteLine($"Removed {done.Names[0]} from {ApprovalPrompt.Shown(done.Entry!)} (its value stays in the item's history)");
        return CliApp.ExitSuccess;
    }
}
