using Keypaste.Cli.Execution;
using Keypaste.Core;

namespace Keypaste.Cli.Commands;

/// <summary>Where keypaste's own arguments end and the child's command begins.</summary>
/// <param name="Left">Everything up to, and excluding, the first bare <c>--</c>.</param>
/// <param name="Command">Everything after it, verbatim. Empty when there was no separator.</param>
/// <param name="HasSeparator">Whether a bare <c>--</c> was present at all.</param>
internal readonly record struct RunArguments(
    string[] Left,
    IReadOnlyList<string> Command,
    bool HasSeparator);

/// <summary>
/// Runs a command with a project's variables in its environment:
/// <c>keypaste run &lt;project&gt; -- &lt;command...&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two phases, in order. The first opens the vault and takes the project's variables out of it;
/// the second starts the child, with no vault alive anywhere in the process — see
/// <see cref="VaultSession.OpenThen{T}"/> for why that ordering is a function rather than a habit.
/// </para>
/// <para>
/// Nothing is written to disk at any point. The values exist in this process's memory and then in
/// the child's environment, which is where a program can read them from and is also the limit of
/// what keypaste can promise about them (SECURITY.md).
/// </para>
/// </remarks>
internal static class RunCommand
{
    private static readonly OptionSpec[] _options =
    [
        new("vault", TakesValue: true),
    ];

    internal static int Execute(string[] args, CliContext context)
    {
        var split = Split(args);

        if (!CommandLine.TryParse(split.Left, 1, _options, out var line, out var error))
        {
            return Fail(context, error);
        }

        // Checked before the separator, so `keypaste run --help` prints usage instead of
        // complaining that a command is missing.
        if (line.WantsHelp)
        {
            WriteUsage(context.Stdout);
            return CliApp.ExitSuccess;
        }

        if (!split.HasSeparator)
        {
            return Fail(context, "expected -- followed by a command, as in: keypaste run dev -- npm start");
        }

        if (split.Command.Count == 0)
        {
            return Fail(context, "no command given after --");
        }

        if (line.Operands.Count != 1)
        {
            return Fail(context, "expected exactly one project name");
        }

        var project = line.Operands[0];

        if (!VaultLocator.TryResolve(line, context.Environment, out var path, out var locateError))
        {
            return Fail(context, locateError);
        }

        return VaultSession.OpenThen(
            path,
            context,
            vault => Load(vault, project, context),
            environment => Start(split.Command, environment, context));
    }

    /// <summary>
    /// Splits at the first bare <c>--</c> after the verb, before any option parsing happens.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Done here rather than in <see cref="CommandLine"/> because <c>run</c> needs something the
    /// parser cannot express at any severity: the right-hand side must be exempt from option
    /// parsing <em>entirely</em>, so that <c>keypaste run p -- mytool --vault x</c> gives
    /// <c>mytool</c> its own <c>--vault</c>. A parser that returns "the operands after <c>--</c>"
    /// has already decided that flag was keypaste's.
    /// </para>
    /// <para>
    /// Only the first separator is a boundary; every later one belongs to the child verbatim,
    /// which is what makes <c>keypaste run p -- git log -- path</c> mean what it looks like.
    /// </para>
    /// </remarks>
    internal static RunArguments Split(string[] args)
    {
        for (var i = 1; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--", StringComparison.Ordinal))
            {
                return new RunArguments(args[..i], args[(i + 1)..], HasSeparator: true);
            }
        }

        return new RunArguments(args, [], HasSeparator: false);
    }

    /// <summary>Reads the project's variables and merges them over the current environment.</summary>
    private static (int Exit, IReadOnlyDictionary<string, string>? Loaded) Load(
        Vault vault,
        string project,
        CliContext context)
    {
        var store = new EnvStore(vault);

        if (!store.ProjectExists(project))
        {
            context.Stderr.WriteLine($"keypaste run: no env set for '{project}'");
            return (CliApp.ExitNotFound, null);
        }

        var variables = store.Read(project);

        if (!EnvironmentMerge.TryBuild(context.Environment.All(), variables, out var merged, out var error))
        {
            context.Stderr.WriteLine($"keypaste run: '{EnvConvention.GroupPath(project)}' {error}");
            return (CliApp.ExitInternalError, null);
        }

        if (EnvironmentMerge.OverridesPath(variables))
        {
            context.Stderr.WriteLine(
                $"warning: '{project}' defines PATH; the command itself is still resolved against yours");
        }

        return (CliApp.ExitSuccess, merged);
    }

    /// <summary>Starts the child and reports its exit code as keypaste's own.</summary>
    private static int Start(
        IReadOnlyList<string> command,
        IReadOnlyDictionary<string, string> environment,
        CliContext context)
    {
        // Everything keypaste has to say is said before the child owns the console, so a warning
        // does not land in the middle of the child's output.
        context.Stderr.Flush();

        var arguments = new string[command.Count - 1];
        for (var i = 1; i < command.Count; i++)
        {
            arguments[i - 1] = command[i];
        }

        var result = context.ProcessLauncher.Run(new ChildStart(command[0], arguments, environment));

        switch (result.Outcome)
        {
            case ChildOutcome.Exited:
                return result.ExitCode;

            case ChildOutcome.NotFound:
                context.Stderr.WriteLine($"keypaste run: {result.Error}");
                return CliApp.ExitCommandNotFound;

            case ChildOutcome.NotExecutable:
                context.Stderr.WriteLine($"keypaste run: {result.Error}");
                return CliApp.ExitCommandNotExecutable;

            default:
                context.Stderr.WriteLine($"keypaste run: {result.Error}");
                return CliApp.ExitInternalError;
        }
    }

    private static int Fail(CliContext context, string message)
    {
        context.Stderr.WriteLine($"keypaste run: {message}");
        return CliApp.ExitUsageError;
    }

    internal static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("usage: keypaste run <project> -- <command> [args...]");
        writer.WriteLine();
        writer.WriteLine("runs a command with the project's variables in its environment. nothing is");
        writer.WriteLine("written to disk, and the vault is closed before the command starts.");
        writer.WriteLine();
        writer.WriteLine("the -- is required: without it, 'keypaste run dev npm start' cannot be told");
        writer.WriteLine("apart from a project called 'npm'. everything after it belongs to the command.");
        writer.WriteLine();
        writer.WriteLine("once the command starts, its exit code is keypaste's. 127 means there is no");
        writer.WriteLine("such command and 126 means it is not executable, as in a shell.");
    }
}
