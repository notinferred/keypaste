using System.CommandLine;
using Keypaste.Cli.Commands;
using Keypaste.Cli.Styling;
using Keypaste.Core;
using Keypaste.Infrastructure;

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
    /// <c>setup</c> found AI clients and every one refused, or could not be started or reached.
    /// </summary>
    /// <remarks>Its own code so a script can tell it from finding none, which is <see cref="ExitNotFound"/>.</remarks>
    internal const int ExitClientsRefused = 6;

    /// <summary><c>doctor</c> found something that needs fixing; a note alone does not count.</summary>
    internal const int ExitNeedsFixing = 7;

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

        switch (args)
        {
            case [] when context.StdoutIsTerminal:
                return StatusCommand.Execute(context);

            case []:
                WriteUsage(context.Stderr, context.ConsoleStyle);
                return ExitUsageError;

            case ["version" or "--version", ..]:
                context.Stdout.WriteLine(CoreInfo.Version);
                return ExitSuccess;

            case ["help" or "--help" or "-h", ..]:
                WriteUsage(context.Stdout, context.ConsoleStyle);
                return ExitSuccess;

            // `keypaste env help` asks for a group's help, as `keypaste help` asks for the top level's.
            case [var group, "help"] when _root.Subcommands.Any(verb => verb.Name == group && verb.Subcommands.Count > 0):
                WriteHelp(context.Stdout, Parse([group]), context.ConsoleStyle);
                return ExitSuccess;

            // The removed spellings are refused before anything parses, their help included (D-0470).
            case ["agent", ..]:
                return Renamed(context, "keypaste agent", "keypaste approve");

            case ["mcp", "serve", ..]:
                return Renamed(context, "keypaste mcp serve", "keypaste approve");

            case ["mcp", "setup", ..]:
                return Renamed(context, "keypaste mcp setup", "keypaste setup");

            case ["mcp", "policy", ..]:
                return Renamed(context, "keypaste mcp policy", "keypaste clients");

            case ["get", ..] when args.TakeWhile(arg => arg != "--").Contains("--show"):
                context.Stderr.WriteLine("keypaste get: --show is now --reveal");
                return ExitUsageError;
        }

        var line = Parse(args);

        if (line.CommandResult.Command == _root)
        {
            context.Stderr.WriteLine($"keypaste: unknown command '{args[0]}'");
            WriteUsage(context.Stderr, context.ConsoleStyle);
            return ExitUsageError;
        }

        if (Syntax.Problem(line, args) is { } problem)
        {
            var path = Syntax.Path(line);
            context.Stderr.WriteLine($"{path}: {problem}");
            context.Stderr.WriteLine($"see {path} --help");
            return ExitUsageError;
        }

        if (line.GetValue(Syntax.Help))
        {
            WriteHelp(context.Stdout, line, context.ConsoleStyle);
            return ExitSuccess;
        }

        if (line.CommandResult.Command is not CliVerb verb)
        {
            WriteHelp(context.Stderr, line, context.ConsoleStyle);
            return ExitUsageError;
        }

        return verb.Run(line, context);
    }

    /// <summary>Parses a whole command line, its verb first, without running anything.</summary>
    internal static ParseResult Parse(string[] args) => _root.Parse(args, Syntax.Parsing);

    /// <summary>Refuses a removed spelling, naming the one that replaced it, and runs nothing (D-0470).</summary>
    internal static int Renamed(CliContext context, string removed, string replacement)
    {
        context.Stderr.WriteLine($"keypaste: {removed} is now {replacement}");
        return ExitUsageError;
    }

    /// <summary>A verb's help: its name amber and its headings grey on a terminal, plain anywhere else.</summary>
    internal static void WriteHelp(TextWriter writer, ParseResult line, IConsoleStyle style) =>
        Syntax.WriteHelp(writer, line, text => style.Paint(writer, Tone.Accent, text), text => style.Paint(writer, Tone.Muted, text));

    /// <summary>
    /// Every other <c>keypaste mcp</c> is the bridge, which <see cref="Program.StartsBridge"/> sends there before this class
    /// is reached; this one is only its line in the help.
    /// </summary>
    private static readonly CliVerb _mcp = new("mcp", "the MCP server your AI tools start", (_, context) =>
    {
        context.Stderr.WriteLine("keypaste mcp: your AI tools start it; keypaste setup connects them");
        return ExitUsageError;
    });

    /// <summary>The verbs, grouped as a person looks for them, in the order they are printed.</summary>
    private static readonly (string Heading, Command[] Verbs)[] _groups =
    [
        ("SECRETS",
        [
            GetCommand.Command, ShowCommand.Command, SetCommand.Command, FieldCommand.Command, AttachmentCommand.Command,
            RotateCommand.Command, RunCommand.Command, EnvCommand.Command,
        ]),
        ("AGENTS",
        [
            SetupCommand.Command, _mcp, ApproveCommand.Command, ClientsCommand.Command, DoctorCommand.Command,
            GrantsCommand.Command, TokenCommand.Command, LogCommand.Command,
        ]),
        ("VAULT",
        [
            ImportCommand.Command, ShareCommand.Command, LockCommand.Command, InitCommand.Command, UseCommand.Command,
            AddCommand.Command, ListCommand.Command, SearchCommand.Command, RemoveCommand.Command, GenerateCommand.Command,
            AccessCommand.Command,
        ]),
    ];

    private static readonly Command _root = Root();

    private static Command Root()
    {
        var root = new Command("keypaste") { Syntax.Help };

        foreach (var verb in _groups.SelectMany(group => group.Verbs))
        {
            root.Subcommands.Add(verb);
        }

        return root;
    }

    /// <summary>The top-level help: verbs amber and headings grey on a terminal, plain anywhere else.</summary>
    internal static void WriteUsage(TextWriter writer, IConsoleStyle style)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(style);

        var column = _groups.SelectMany(group => group.Verbs).Max(verb => verb.Name.Length) + 2;
        string Muted(string text) => style.Paint(writer, Tone.Muted, text);

        writer.WriteLine($"keypaste {CoreInfo.Version}{Muted($" {style.Glyph(writer, Mark.Dot)} a simple, local password manager on your KeePass file")}");
        writer.WriteLine();
        writer.WriteLine(Muted("USAGE"));
        writer.WriteLine("  keypaste <command> [flags]");

        foreach (var (heading, verbs) in _groups)
        {
            writer.WriteLine();
            writer.WriteLine(Muted(heading));

            foreach (var verb in verbs)
            {
                var summary = (verb.Description ?? "").Split('\n')[0];
                writer.WriteLine($"  {style.Paint(writer, Tone.Accent, verb.Name)}{new string(' ', column - verb.Name.Length)}{summary}");
            }
        }

        writer.WriteLine();
        writer.WriteLine(Muted("FLAGS"));
        writer.WriteLine($"  --vault <path>    which vault to use, or set {VaultLocator.EnvironmentVariable}");
        writer.WriteLine($"  --keyfile <path>  the keyfile it needs too, or set {VaultLocator.KeyfileEnvironmentVariable}");
        writer.WriteLine("  --json            machine-readable output from ls, search, show, field ls,");
        writer.WriteLine("                    attachment ls, env ls, log, grants, doctor, token ls,");
        writer.WriteLine("                    share ls, clients");
        writer.WriteLine("  -h, --help        help for any command");
        writer.WriteLine();
        writer.WriteLine("  version prints the version.");
        writer.WriteLine();
        writer.WriteLine(Muted("EXIT CODES"));
        writer.WriteLine("  0 ok  1 usage  2 error  3 not found  4 wrong password  5 audit log tampered");
        writer.WriteLine("  once a `run` command starts, its exit code is keypaste's own.");
        writer.WriteLine();
        writer.WriteLine("passwords are never echoed. Press Escape at a prompt to cancel.");
    }
}
