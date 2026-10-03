using Keypaste.Core;
using Keypaste.Core.Approval;

namespace Keypaste.Cli.Commands;

/// <summary>Removes one variable: <c>keypaste env rm &lt;project&gt; &lt;KEY&gt; [-p &lt;profile&gt;] [--entry &lt;entry&gt;]</c>.</summary>
/// <remarks>The field leaves the entry tagged into the environment, whose history keeps the value; no entry is deleted.</remarks>
internal static class EnvRemoveCommand
{
    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
        new("keyfile", TakesValue: true),
        new("yes", TakesValue: false),
        EnvCommand.ProfileOption,
        EnvCommand.EntryOption,
    ];

    internal static int Execute(string[] args, CliContext context)
    {
        if (!CommandLine.TryParse(args, 2, _options, out var line, out var error))
        {
            context.Stderr.WriteLine($"keypaste env rm: {error}");
            return CliApp.ExitUsageError;
        }

        if (line.WantsHelp)
        {
            context.Stdout.WriteLine("usage: keypaste env rm <project> <KEY> [-p <profile>] [--entry <entry>] [--yes]");
            return CliApp.ExitSuccess;
        }

        if (!EnvCommand.TryProfile(line, out var profile, out var profileError))
        {
            context.Stderr.WriteLine($"keypaste env rm: {profileError}");
            return CliApp.ExitUsageError;
        }

        if (line.Operands.Count != 2)
        {
            context.Stderr.WriteLine("keypaste env rm: expected a project and a variable");
            return CliApp.ExitUsageError;
        }

        var project = line.Operands[0];
        var key = line.Operands[1];
        var assumeYes = line.HasFlag("yes");

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

        return VaultSession.OpenHeld(path, line, context, vault =>
        {
            if (!EnvCommand.TryFindEntry(vault, line, out var entry, out var missing))
            {
                context.Stderr.WriteLine($"keypaste env rm: {missing}");
                return CliApp.ExitNotFound;
            }

            var listing = EnvResolution.List(vault, project, profile);

            if (listing.Outcome != EnvOutcome.Resolved)
            {
                context.Stderr.WriteLine(listing.Outcome == EnvOutcome.NoProject
                    ? $"keypaste env rm: no env set for '{project}'"
                    : $"keypaste env rm: '{project}' has no '{profile}' profile");
                return CliApp.ExitNotFound;
            }

            var holding = listing.Sources
                .Where(source => string.Equals(source.Key, key, StringComparison.Ordinal) && (entry is null || source.Entry == entry))
                .ToList();

            if (holding.Count == 0)
            {
                context.Stderr.WriteLine(string.Equals(profile, EnvProfileNames.Default, StringComparison.Ordinal)
                    ? $"keypaste env rm: '{project}' has no variable '{key}'"
                    : $"keypaste env rm: '{project}' has no variable '{key}' in its '{profile}' profile");
                return CliApp.ExitNotFound;
            }

            if (holding.Count > 1)
            {
                var entries = string.Join(", ", holding.Select(source => ApprovalPrompt.Shown(source.Entry)).Order(StringComparer.Ordinal));
                context.Stderr.WriteLine($"keypaste env rm: {key} is on more than one entry ({entries}); name the one to remove from with --entry");
                return CliApp.ExitInternalError;
            }

            var source = holding[0];
            var shown = ApprovalPrompt.Shown(source.Entry);

            if (!assumeYes)
            {
                var answer = context.Prompt.ReadLine($"Remove {key} from {shown}? Its value stays in the entry's history. [y/N] ");
                if (answer is null || !answer.Trim().StartsWith('y') && !answer.Trim().StartsWith('Y'))
                {
                    context.Stderr.WriteLine("Cancelled.");
                    return CliApp.ExitUsageError;
                }
            }

            // Nothing removed means nothing to save. Something wrote to the file between the
            // check above and here, and the honest answer is that this run did not do it.
            var removal = new EnvStore(vault).Remove(project, profile, key, source.Entry);

            switch (removal.Outcome)
            {
                case EnvRemoveOutcome.NothingMatched:
                    context.Stderr.WriteLine($"keypaste env rm: '{key}' was not removed; the vault is unchanged");
                    return CliApp.ExitNotFound;
                case EnvRemoveOutcome.Ambiguous:
                case EnvRemoveOutcome.Refused:
                    context.Stderr.WriteLine($"keypaste env rm: {EntryNameSanitizer.SanitizeProse(removal.Refusal, 1024).Text}");
                    return CliApp.ExitInternalError;
            }

            vault.Save();

            context.Stderr.WriteLine($"Removed {key} from {shown} (its value stays in the entry's history)");
            return CliApp.ExitSuccess;
        });
    }
}
