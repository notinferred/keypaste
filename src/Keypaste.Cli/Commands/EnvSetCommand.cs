using Keypaste.Core;
using Keypaste.Core.Approval;

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
    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
        EnvCommand.ProfileOption,
        EnvCommand.EntryOption,
        .. GenerateOption.Specs,
    ];

    internal static int Execute(string[] args, CliContext context)
    {
        if (!CommandLine.TryParse(args, 2, _options, out var line, out var error))
        {
            context.Stderr.WriteLine($"keypaste env set: {error}");
            return CliApp.ExitUsageError;
        }

        if (line.WantsHelp)
        {
            context.Stdout.WriteLine("usage: keypaste env set <project> <KEY>[=value] [-p <profile>] [--entry <entry>]");
            context.Stdout.WriteLine($"       {GenerateOption.Usage}");
            return CliApp.ExitSuccess;
        }

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

        if (line.Operands.Count != 2)
        {
            context.Stderr.WriteLine("keypaste env set: expected a project and a variable");
            return CliApp.ExitUsageError;
        }

        var project = line.Operands[0];
        var assignment = line.Operands[1];

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

        return VaultSession.OpenHeld(path, line, context, vault =>
        {
            if (!EnvCommand.TryFindEntry(vault, line, out var entry, out var missing))
            {
                context.Stderr.WriteLine($"keypaste env set: {missing}");
                return CliApp.ExitNotFound;
            }

            string value;
            if (inlineValue is not null)
            {
                value = inlineValue;

                // Said once, where it happened, without the value in it.
                context.Stderr.WriteLine(
                    "warning: the value came from the command line, where your shell records it");
            }
            else
            {
                // Prompted inside the session so the piped protocol stays one line per prompt, in
                // the order they are asked: master password first, then the value.
                using var secret = recipe is { } wanted
                    ? GenerateOption.Generate(wanted)
                    : context.Prompt.ReadSecret($"Value for {key}: ");

                if (secret is null)
                {
                    context.Stderr.WriteLine("keypaste env set: no value given");
                    return CliApp.ExitUsageError;
                }

                value = new string(secret.Value);
            }

            var plan = new EnvStore(vault).Set(project, profile, key, value, entry);

            if (plan.Refusal is { } refusal)
            {
                context.Stderr.WriteLine($"keypaste env set: {EntryNameSanitizer.SanitizeProse(refusal, 1024).Text}");
                return CliApp.ExitUsageError;
            }

            var written = plan.Keys[0];
            var where = ApprovalPrompt.Shown(written.Entry);
            var generated = recipe is { } used
                ? $" ({used.Describe("value")} generated)"
                : string.Empty;

            if (written.Change == EnvWriteChange.Unchanged)
            {
                context.Stderr.WriteLine($"{where} already holds that value for {key}; nothing was written");
                return CliApp.ExitSuccess;
            }

            vault.Save();

            context.Stderr.WriteLine(written switch
            {
                { Change: EnvWriteChange.New } when plan.CreatesHome => $"Set {key} on {where}, created and tagged {ProjectTagFor(project, profile)}{generated}",
                { Change: EnvWriteChange.New } => $"Set {key} on {where}{generated}",
                _ => $"Updated {key} on {where} (previous value kept in entry history){generated}",
            });

            return CliApp.ExitSuccess;
        });
    }

    private static string ProjectTagFor(string project, string environment) =>
        ProjectTag.TryFor(project, environment, out var tag, out _) ? tag : string.Empty;
}
