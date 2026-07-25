using Keypaste.Core;

namespace Keypaste.Cli.Commands;

/// <summary>Sets one variable: <c>keypaste env set &lt;project&gt; &lt;KEY&gt;[=value]</c>.</summary>
/// <remarks>
/// <para>
/// With a bare <c>KEY</c> the value is read the way every other secret is — hidden, or one line of
/// stdin when piped, after the master password. The <c>KEY=value</c> form is accepted for
/// scripting; it puts the value in <c>argv</c>, where it is visible in the process list and in
/// shell history, and that exposure is recorded in SECURITY.md and DECISIONS.md D-0014 rather than
/// warned about on every run.
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
            context.Stdout.WriteLine("usage: keypaste env set <project> <KEY>[=value]");
            return CliApp.ExitSuccess;
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

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            context.Stderr.WriteLine($"keypaste env set: {locateError}");
            return CliApp.ExitUsageError;
        }

        return VaultSession.Open(path, context, vault =>
        {
            string value;
            if (inlineValue is not null)
            {
                value = inlineValue;
            }
            else
            {
                // Prompted inside the session so the piped protocol stays one line per prompt, in
                // the order they are asked: master password first, then the value.
                using var secret = context.Prompt.ReadSecret($"Value for {key}: ");
                if (secret is null)
                {
                    context.Stderr.WriteLine("keypaste env set: no value given");
                    return CliApp.ExitUsageError;
                }

                value = new string(secret.Value);
            }

            var store = new EnvStore(vault);
            var outcome = store.TrySet(project, key, value, out var rejection);

            if (outcome == EnvSetOutcome.Rejected)
            {
                context.Stderr.WriteLine($"keypaste env set: {rejection}");
                return CliApp.ExitUsageError;
            }

            vault.Save();

            var entryPath = EnvConvention.EntryPath(project, key);
            context.Stderr.WriteLine(outcome == EnvSetOutcome.Created
                ? $"Set {entryPath}"
                : $"Updated {entryPath} (previous value kept in entry history)");

            return CliApp.ExitSuccess;
        });
    }
}
