using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Policy;

namespace Keypaste.Cli.Commands;

/// <summary><c>keypaste policy ls</c> — the standing rules, in plain English.</summary>
/// <remarks>
/// <para>
/// <b>It needs no vault, and <c>--vault</c> is not one of its options.</b> A policy file names
/// patterns, not entries; reading it resolves nothing and decrypts nothing. Asking for a master
/// password to read a plaintext configuration file would be theatre, and it would make the one
/// command an operator reaches for when something looks wrong the one command they cannot run in a
/// hurry. Leaving <c>--vault</c> out of the spec entirely — rather than accepting and ignoring it —
/// is what makes <c>keypaste policy ls --vault x</c> a usage error instead of a silent no-op.
/// </para>
/// <para>
/// <b>It never echoes the line the user wrote.</b> Every rule is rendered as the two patterns it
/// actually parsed to, because the glob syntax has a trap the user is likely to fall into and
/// repeating their own text back would confirm their belief instead of testing it. See
/// <see cref="PolicyText"/> for the trap.
/// </para>
/// </remarks>
internal static class PolicyListCommand
{
    internal const string PolicyOption = "policy";

    private static readonly OptionSpec[] _options = [new(PolicyOption, TakesValue: true)];

    internal static int Execute(string[] args, CliContext context)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(context);

        if (!CommandLine.TryParse(args, 2, _options, out var line, out var parseError))
        {
            context.Stderr.WriteLine($"keypaste policy ls: {parseError}");
            return CliApp.ExitUsageError;
        }

        if (line.WantsHelp)
        {
            PolicyCommand.WriteUsage(context.Stdout);
            return CliApp.ExitSuccess;
        }

        if (line.Operands.Count > 0)
        {
            context.Stderr.WriteLine($"keypaste policy ls: unexpected argument '{line.Operands[0]}'");
            return CliApp.ExitUsageError;
        }

        var load = PolicyLoader.Load(
            line.Value(PolicyOption)
            ?? KeypasteHome.PolicyPath(context.Environment.Get(KeypasteHome.EnvironmentVariable)));

        // A file that is not usable goes to stderr and nowhere else, so a script reading stdout gets
        // an empty rule list — which is the truth about what is in force. Exit 1, because a broken
        // authorization file the user wrote is a configuration error and CI should see it.
        if (load.Status == PolicyStatus.Rejected)
        {
            context.Stderr.WriteLine($"keypaste policy ls: {load.Path} is not usable");
            context.Stderr.WriteLine($"keypaste policy ls: {Detail(load)}");
            context.Stderr.WriteLine(
                "keypaste policy ls: no rule from this file is in force. Every request is shown to you.");

            return CliApp.ExitUsageError;
        }

        // Exit 0, not "not found": nothing pre-authorized is keypaste's default and correct state,
        // and reporting the safest possible configuration as a failure would be backwards.
        if (!load.HasRules)
        {
            context.Stdout.WriteLine(
                load.Status == PolicyStatus.Absent
                    ? $"No policy file at {load.Path}. Every request is shown to you."
                    : $"No rules in {load.Path}. Every request is shown to you.");

            context.Stdout.WriteLine();
            context.Stdout.WriteLine("Write one to pre-authorize narrow patterns. See docs/policy.md.");

            return CliApp.ExitSuccess;
        }

        Render(load, context);
        return CliApp.ExitSuccess;
    }

    private static void Render(PolicyLoad load, CliContext context)
    {
        var count = load.Rules.Rules.Count;

        context.Stdout.WriteLine($"{count} rule{(count == 1 ? string.Empty : "s")}, from {load.Path} [{load.Digest}]");
        context.Stdout.WriteLine();

        foreach (var rule in load.Rules.Rules)
        {
            foreach (var written in PolicyText.Describe(rule))
            {
                context.Stdout.WriteLine(written);
            }

            context.Stdout.WriteLine();
        }

        foreach (var written in PolicyText.Footer)
        {
            context.Stdout.WriteLine(written);
        }
    }

    /// <summary>
    /// The loader's own sentence, with the path stripped off the front so it is not printed twice.
    /// </summary>
    /// <remarks>
    /// It is sanitized on the way out even though the reader already refuses a rule containing
    /// anything the sanitizer would change: the message can quote a <em>key</em> or a section name
    /// from a file that never got as far as being a rule, and that text is arriving in a terminal.
    /// </remarks>
    private static string Detail(PolicyLoad load)
    {
        var marker = " is NOT in force - ";
        var at = load.Reason.IndexOf(marker, StringComparison.Ordinal);
        var detail = at < 0 ? load.Reason : load.Reason[(at + marker.Length)..];

        return EntryNameSanitizer.Sanitize(detail, 200).Text;
    }
}
