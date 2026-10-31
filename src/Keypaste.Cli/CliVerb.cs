using System.CommandLine;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;
using Keypaste.Infrastructure;

namespace Keypaste.Cli;

/// <summary>A verb <see cref="CliApp"/> runs once its line has parsed: the tree is declared once and parsing invokes nothing.</summary>
/// <param name="name">The word that names it.</param>
/// <param name="description">Its summary, the line the top-level help shows, then any overview after a blank line.</param>
/// <param name="run">What it does with its parsed line.</param>
/// <param name="tail">What its usage ends with past the flags, collected after the line's first <c>--</c>.</param>
internal sealed class CliVerb(string name, string description, Func<ParseResult, CliContext, int> run, string? tail = null)
    : Verb(name, description, tail)
{
    internal int Run(ParseResult line, CliContext context) => run(line, context);
}

/// <summary>The options most verbs share, declared once.</summary>
internal static class CliOptions
{
    internal static Option<string> Vault { get; } =
        Syntax.Value("--vault", "path", $"which vault to use, or set {VaultLocator.EnvironmentVariable}");

    internal static Option<string> Keyfile { get; } =
        Syntax.Value("--keyfile", "path", $"the keyfile it needs too, or set {VaultLocator.KeyfileEnvironmentVariable}");

    internal static Option<bool> Json { get; } = Syntax.Flag("--json", "machine-readable output");

    internal static Option<string> Approver { get; } =
        Syntax.Value("--approver", "name", $"the approver to reach, or set {ApproverEndpoint.EnvironmentVariable}");

    internal static Option<string> Profile { get; } =
        Syntax.Value("--profile", "environment", $"the environment, {EnvProfileNames.Default} by default", "-p");

    internal static Option<bool> Yes { get; } = Syntax.Flag("--yes", "go ahead without asking, which a piped run must say");

    /// <summary>Which log <c>log</c> and <c>log verify</c> read.</summary>
    internal static Option<string> AuditLog { get; } = Syntax.Value(
        "--audit-log",
        "path",
        $"which log to read, ~/{KeypasteHome.DirectoryName}/{KeypasteHome.AuditFileName} unless {KeypasteHome.EnvironmentVariable} moves it");

    internal static Option<string> Entry { get; } =
        Syntax.Value("--entry", "item", "the item to write on, rather than the environment's home item");
}
