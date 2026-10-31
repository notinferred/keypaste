using System.CommandLine;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Editing;
using Keypaste.Infrastructure;

namespace Keypaste.Cli.Commands;

/// <summary>Sets one variable: <c>keypaste env set &lt;project&gt; &lt;KEY&gt;[=value] [-p &lt;profile&gt;] [--entry &lt;entry&gt;]</c>.</summary>
/// <remarks>
/// <para>
/// An existing key is written where it lives; a new one becomes a protected field of the entry
/// <c>--entry</c> names or of the environment's home entry, which is created tagged on first use
/// (D-0413).
/// </para>
/// <para>
/// With a bare <c>KEY</c> the value is read the way every other secret is — hidden, or one line of
/// stdin when piped, after the master password. The <c>KEY=value</c> form is accepted for
/// scripting; it puts the value in <c>argv</c>, where it is visible in the process list and in
/// shell history, and it says so once on stderr. The warning is one line and names no value: it
/// exists to be read the first time someone types this, not to be scrolled past.
/// </para>
/// <para>
/// The value is never echoed back, on either form.
/// </para>
/// </remarks>
internal static class EnvSetCommand
{
    private static readonly Argument<string> _project = Syntax.Operand("project", "the project");
    private static readonly Argument<string> _assignment = Syntax.Operand("KEY[=value]", "the variable, and its value unless you are asked for it");

    internal static CliVerb Command { get; } = new("set", "set a variable, prompting for the value", Execute)
    {
        _project, _assignment, CliOptions.Profile, CliOptions.Entry, GenerateOption.Options, CliOptions.Vault, CliOptions.Keyfile,
    };

    private static int Execute(ParseResult line, CliContext context)
    {
        if (!GenerateOption.TryRead(line, out var recipe, out var recipeError))
        {
            context.Stderr.WriteLine($"keypaste env set: {recipeError}");
            return CliApp.ExitUsageError;
        }

        if (!EnvCommand.TryProfile(line, out var profile, out var profileError))
        {
            context.Stderr.WriteLine($"keypaste env set: {profileError}");
            return CliApp.ExitUsageError;
        }

        var project = line.GetValue(_project)!;
        var assignment = line.GetValue(_assignment)!;

        // Split on the first '=' only, so a value containing one survives intact.
        var equals = assignment.IndexOf('=');
        var key = equals < 0 ? assignment : assignment[..equals];
        var inlineValue = equals < 0 ? null : assignment[(equals + 1)..];

        if (key.Length == 0)
        {
            context.Stderr.WriteLine("keypaste env set: the variable name cannot be empty");
            return CliApp.ExitUsageError;
        }

        if (recipe is not null && inlineValue is not null)
        {
            context.Stderr.WriteLine(
                "keypaste env set: give a value or --generate, not both");
            return CliApp.ExitUsageError;
        }

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            context.Stderr.WriteLine($"keypaste env set: {locateError}");
            return CliApp.ExitUsageError;
        }

        var edit = new EnvSetKey(project, profile, key, line.GetValue(CliOptions.Entry), null);

        EnvSetKey? Read(CliContext asked)
        {
            if (inlineValue is not null)
            {
                // Said once, where it happened, without the value in it.
                asked.Stderr.WriteLine(
                    "warning: the value came from the command line, where your shell records it");
                return edit with { Value = NewSecret.Of(inlineValue) };
            }

            if (recipe is { } wanted)
            {
                return edit with { Value = NewSecret.Generated(wanted) };
            }

            // Prompted after the vault is reached, so the piped protocol stays one line per prompt, in
            // the order they are asked: the master password first, then the value.
            using var secret = asked.Prompt.ReadSecret($"Value for {key}: ");

            if (secret is null)
            {
                asked.Stderr.WriteLine("keypaste env set: no value given");
                return null;
            }

            return edit with { Value = NewSecret.Of(new string(secret.Value)) };
        }

        var route = new OwnerRoute("env set", Read, (done, asked) => Done(done, project, profile, recipe, asked));

        return VaultSession.OpenHeld(path, line, context, route, vault =>
        {
            var check = TerminalEdits.Check(vault, edit, EditLines.Scene(context));
            if (check.Refusal is { } refused)
            {
                return EditLines.Refuse(context, "env set", refused);
            }

            if (Read(context) is not { } full)
            {
                return CliApp.ExitUsageError;
            }

            var result = TerminalEdits.Apply(vault, TerminalEdits.Generate(full), EditLines.Scene(context), shown: null);
            if (result.Refusal is { } late)
            {
                return EditLines.Refuse(context, "env set", late);
            }

            if (result.Done!.Outcome != EditOutcome.Unchanged)
            {
                vault.Save();
            }

            return Done(result.Done, project, profile, recipe, context);
        });
    }

    /// <summary>Says which item the variable is on, and whether that item was created, never the value.</summary>
    internal static int Done(EditDone done, string project, string profile, SecretRecipe? recipe, CliContext context)
    {
        var written = done.Keys[0];
        var where = ApprovalPrompt.Shown(written.Entry);
        var generated = recipe is { } used
            ? $" ({used.Describe("value")} generated)"
            : string.Empty;

        context.Stderr.WriteLine(written.Change switch
        {
            EnvWriteChange.Unchanged => $"{where} already holds that value for {written.Key}; nothing was written",
            EnvWriteChange.New when done.CreatesHome => $"Set {written.Key} on {where}, created and tagged {ProjectTagFor(project, profile)}{generated}",
            EnvWriteChange.New => $"Set {written.Key} on {where}{generated}",
            _ => $"Updated {written.Key} on {where} (previous value kept in item history){generated}",
        });

        return CliApp.ExitSuccess;
    }

    private static string ProjectTagFor(string project, string environment) =>
        ProjectTag.TryFor(project, environment, out var tag, out _) ? tag : string.Empty;
}
