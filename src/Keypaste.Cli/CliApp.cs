using Keypaste.Cli.Commands;
using Keypaste.Cli.Styling;
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
    /// The audit log is not the file keypaste wrote.
    /// </summary>
    /// <remarks>
    /// Its own code because none of the others fits and because a script has to be able to branch on
    /// it: a tampered log is not a usage error, not a missing file, and not an internal failure, and
    /// conflating it with any of them would make "did anything touch my audit trail" unanswerable
    /// from a shell.
    /// </remarks>
    internal const int ExitTamperDetected = 5;

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
            WriteUsage(context.Stderr, context.ConsoleStyle);
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

            case "generate":
                return GenerateCommand.Execute(args, context);

            case "ls":
                return ListCommand.Execute(args, context);

            case "rm":
                return RemoveCommand.Execute(args, context);

            case "access":
                return AccessCommand.Execute(args, context);

            case "env":
                return EnvCommand.Execute(args, context);

            case "run":
                return RunCommand.Execute(args, context);

            case "token":
                return TokenCommand.Execute(args, context);

            case "agent":
                return AgentCommand.Execute(args, context);

            case "setup":
                return SetupCommand.Execute(args, context);

            case "policy":
                return PolicyCommand.Execute(args, context);

            case "log":
                return LogCommand.Execute(args, context);

            case "set":
                return SetCommand.Execute(args, context);

            case "grants":
                return GrantsCommand.Execute(args, context);

            case "lock":
                return LockCommand.Execute(args, context);

            case "mcp":
                return McpCommand.Execute(args, context);

            case "share":
                return ShareCommand.Execute(args, context);

            case "import":
                return ImportCommand.Execute(args, context);

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
                WriteUsage(context.Stdout, context.ConsoleStyle);
                return ExitSuccess;

            default:
                context.Stderr.WriteLine($"keypaste: unknown command '{command}'");
                WriteUsage(context.Stderr, context.ConsoleStyle);
                return ExitUsageError;
        }
    }

    /// <summary>The verbs, grouped as a person looks for them, in the order they are printed.</summary>
    private static readonly (string Heading, (string Verb, string Summary)[] Verbs)[] _groups =
    [
        ("SECRETS",
        [
            ("get", "copy a secret to the clipboard, or print it with --reveal"),
            ("set", "create or update a secret"),
            ("run", "run a command with secrets in its environment"),
            ("env", "import, export and diff .env profiles"),
        ]),
        ("AGENTS",
        [
            ("mcp", "approve agents' requests here, or connect MCP clients"),
            ("grants", "list or revoke time-boxed access"),
            ("token", "create scoped, inject-only tokens"),
            ("log", "show the hash-chained activity log"),
        ]),
        ("VAULT",
        [
            ("import", "copy in a .kdbx, or keep editing it in place"),
            ("share", "create an encrypted, expiring link"),
            ("lock", "lock now and pause all agents"),
            ("init", "create a new vault"),
            ("add", "add an entry"),
            ("ls", "list groups and entries"),
            ("rm", "remove an entry"),
            ("generate", "print a password or passphrase; it is not stored"),
            ("access", "change the master password or keyfile"),
            ("policy", "show the standing rules that skip the prompt"),
        ]),
    ];

    /// <summary>The top-level help: verbs amber and headings grey on a terminal, plain anywhere else.</summary>
    internal static void WriteUsage(TextWriter writer, IConsoleStyle style)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(style);

        string Muted(string text) => style.Paint(writer, Tone.Muted, text);

        writer.WriteLine($"keypaste {CoreInfo.Version}{Muted($" {style.Glyph(writer, Mark.Dot)} secrets for developers and their agents")}");
        writer.WriteLine();
        writer.WriteLine(Muted("USAGE"));
        writer.WriteLine("  keypaste <command> [flags]");

        foreach (var (heading, verbs) in _groups)
        {
            writer.WriteLine();
            writer.WriteLine(Muted(heading));

            foreach (var (verb, summary) in verbs)
            {
                writer.WriteLine($"  {style.Paint(writer, Tone.Accent, verb)}{new string(' ', 10 - verb.Length)}{summary}");
            }
        }

        writer.WriteLine();
        writer.WriteLine(Muted("FLAGS"));
        writer.WriteLine($"  --vault <path>    which vault to use, or set {VaultLocator.EnvironmentVariable}");
        writer.WriteLine($"  --keyfile <path>  the keyfile it needs too, or set {VaultLocator.KeyfileEnvironmentVariable}");
        writer.WriteLine("  --json            machine-readable output from ls, env ls, log, grants,");
        writer.WriteLine("                    token ls and share ls");
        writer.WriteLine("  -h, --help        help for any command");
        writer.WriteLine();
        writer.WriteLine("  agent is mcp serve, setup is mcp setup, version prints the version.");
        writer.WriteLine();
        writer.WriteLine(Muted("EXIT CODES"));
        writer.WriteLine("  0 ok  1 usage  2 error  3 not found  4 wrong password  5 audit log tampered");
        writer.WriteLine("  once a `run` command starts, its exit code is keypaste's own.");
        writer.WriteLine();
        writer.WriteLine("passwords are never echoed. Press Escape at a prompt to cancel.");
    }
}
