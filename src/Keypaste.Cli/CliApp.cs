using Keypaste.Cli.Commands;
using Keypaste.Core;

namespace Keypaste.Cli;

/// <summary>
/// The CLI's real entry point. Takes its output streams as parameters so the whole
/// surface is testable in-process, without spawning a child process or mutating the
/// process-global <see cref="Console"/> writers.
/// </summary>
/// <remarks>
/// Output contract, fixed here so every later command inherits it: data goes to stdout,
/// everything else — prompts, progress, errors — to stderr. Exit codes distinguish the
/// failures a script would actually branch on.
/// </remarks>
internal static class CliApp
{
    internal const int ExitSuccess = 0;
    internal const int ExitUsageError = 1;
    internal const int ExitInternalError = 2;

    /// <summary>The vault or entry named does not exist.</summary>
    internal const int ExitNotFound = 3;

    /// <summary>The master password was wrong, or none was supplied.</summary>
    internal const int ExitAuthFailed = 4;

    /// <summary>
    /// The command <c>run</c> was given exists but could not be executed. The shell convention,
    /// used because scripts already branch on it.
    /// </summary>
    internal const int ExitCommandNotExecutable = 126;

    /// <summary>There is no such command. The shell convention, as above.</summary>
    internal const int ExitCommandNotFound = 127;

    internal static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        return Run(args, CliContext.CreateDefault(stdout, stderr));
    }

    internal static int Run(string[] args, CliContext context)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(context);

        if (args.Length == 0)
        {
            WriteUsage(context.Stderr);
            return ExitUsageError;
        }

        var command = args[0];

        switch (command)
        {
            case "init":
                return InitCommand.Execute(args, context);

            case "add":
                return AddCommand.Execute(args, context);

            case "get":
                return GetCommand.Execute(args, context);

            case "ls":
                return ListCommand.Execute(args, context);

            case "rm":
                return RemoveCommand.Execute(args, context);

            case "env":
                return EnvCommand.Execute(args, context);

            case "run":
                return RunCommand.Execute(args, context);

            case "agent":
                return AgentCommand.Execute(args, context);

            case "policy":
                return PolicyCommand.Execute(args, context);

            case "hello":
                context.Stdout.WriteLine(CoreInfo.Hello());
                return ExitSuccess;

            case "version":
            case "--version":
                context.Stdout.WriteLine(CoreInfo.Version);
                return ExitSuccess;

            case "help":
            case "--help":
            case "-h":
                WriteUsage(context.Stdout);
                return ExitSuccess;

            default:
                context.Stderr.WriteLine($"keypaste: unknown command '{command}'");
                WriteUsage(context.Stderr);
                return ExitUsageError;
        }
    }

    internal static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("usage: keypaste <command> [options]");
        writer.WriteLine();
        writer.WriteLine("commands:");
        writer.WriteLine("  init <vault.kdbx>   create a new vault");
        writer.WriteLine("  add <entry>         add an entry");
        writer.WriteLine("  get <entry>         copy a password to the clipboard, or --show it");
        writer.WriteLine("  ls                  list groups and entries");
        writer.WriteLine("  rm <entry>          remove an entry");
        writer.WriteLine("  env <subcommand>    manage a project's environment variables");
        writer.WriteLine("  run <project> --    run a command with those variables injected");
        writer.WriteLine("  agent               unlock the vault and approve AI agents' requests");
        writer.WriteLine("  policy ls           show the standing rules that skip the approval prompt");
        writer.WriteLine("  version             print the core version");
        writer.WriteLine();
        writer.WriteLine("the vault:");
        writer.WriteLine($"  --vault <path>      which vault to use, or set {VaultLocator.EnvironmentVariable}");
        writer.WriteLine();
        writer.WriteLine("exit codes:");
        writer.WriteLine("  0 ok  1 usage  2 error  3 not found  4 wrong password");
        writer.WriteLine("  once a `run` command starts, its exit code is keypaste's own.");
        writer.WriteLine();
        writer.WriteLine("passwords are never echoed. Press Escape at a prompt to cancel.");
    }
}
