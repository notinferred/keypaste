using Keypaste.Core.Audit;

namespace Keypaste.Cli.Commands;

/// <summary>Dispatches the policy subcommands: <c>keypaste policy &lt;subcommand&gt;</c>.</summary>
/// <remarks>
/// <para>
/// There is exactly one, and the two that are missing are missing on purpose.
/// </para>
/// <para>
/// <b>No <c>policy add</c>.</b> keypaste must not be a writer of its own authorization file. A
/// command that edits it is a command an agent could talk somebody into running, and the whole
/// value of the file is that a person wrote every line of it in an editor they opened.
/// </para>
/// <para>
/// <b>No <c>policy test &lt;entry&gt;</c>, yet.</b> Showing which entries each rule matches
/// <em>today</em> is the best mitigation there is for a rule quietly covering more than its author
/// pictured (THREATS.md T-13) — and it needs the vault, which would put a master password prompt in
/// front of the one command an operator reaches for when something already looks wrong. It belongs
/// with <c>keypaste log</c> in Stage 2.4.
/// </para>
/// </remarks>
internal static class PolicyCommand
{
    internal static int Execute(string[] args, CliContext context)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(context);

        if (args.Length < 2)
        {
            WriteUsage(context.Stderr);
            return CliApp.ExitUsageError;
        }

        var subcommand = args[1];

        switch (subcommand)
        {
            case "ls":
                return PolicyListCommand.Execute(args, context);

            // Handled here rather than left to the subcommand parsers, matching `keypaste env`:
            // with no subcommand to dispatch on, `keypaste policy --help` would otherwise be
            // reported as an unknown one.
            case "help":
            case "--help":
            case "-h":
                WriteUsage(context.Stdout);
                return CliApp.ExitSuccess;

            default:
                context.Stderr.WriteLine($"keypaste policy: unknown subcommand '{subcommand}'");
                WriteUsage(context.Stderr);
                return CliApp.ExitUsageError;
        }
    }

    internal static void WriteUsage(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteLine("usage: keypaste policy ls [--policy <path>]");
        writer.WriteLine();
        writer.WriteLine("Shows the standing rules that let an agent read a credential without asking");
        writer.WriteLine("you. There are none unless you wrote some.");
        writer.WriteLine();
        writer.WriteLine($"Rules live in ~/{KeypasteHome.DirectoryName}/{KeypasteHome.PolicyFileName}, which keypaste reads and never writes.");
        writer.WriteLine("Anything wrong with that file means the whole of it is ignored and every");
        writer.WriteLine("request asks you. `keypaste agent` reads it once, at startup.");
    }
}
