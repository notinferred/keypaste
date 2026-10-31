using System.Globalization;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Xunit;

namespace Keypaste.Cli.Tests;

public sealed class CliAppTests
{
    [Fact]
    public void NoArguments_WithStdoutRedirected_PrintsHelpToStderr_Exit1()
    {
        using var harness = new CliHarness();

        Assert.Equal(CliApp.ExitUsageError, harness.Run());
        Assert.Empty(harness.Out);
        Assert.Equal(GroupedHelp, harness.Err);
    }

    [Fact]
    public void NoArguments_OnATerminal_PrintsWhereThingsStand_Exit0()
    {
        using var harness = new CliHarness { StdoutIsTerminal = true };

        Assert.Equal(CliApp.ExitSuccess, harness.Run());
        Assert.StartsWith($"keypaste {CoreInfo.Version}{Environment.NewLine}  vault     ", harness.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("USAGE", harness.Out, StringComparison.Ordinal);
        Assert.Empty(harness.Err);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("help")]
    [InlineData("-h")]
    public void Help_GoesToStdout_AndExitsZero(string asked)
    {
        using var harness = new CliHarness();

        Assert.Equal(CliApp.ExitSuccess, harness.Run(asked));
        Assert.Equal(GroupedHelp, harness.Out);
        Assert.Empty(harness.Err);
    }

    [Fact]
    public void Help_IsExactlyTheGroupedText()
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);

        CliApp.WriteUsage(writer, new FakeConsoleStyle());

        Assert.Equal(GroupedHelp, writer.ToString());
    }

    [Fact]
    public void Help_FitsEightyColumns()
    {
        foreach (var line in GroupedHelp.Split(Environment.NewLine))
        {
            Assert.True(line.Length <= 80, $"{line.Length} columns: {line}");
        }
    }

    [Fact]
    public void Help_HasNoEscapes_WhenRedirected()
    {
        using var redirected = new StringWriter(CultureInfo.InvariantCulture);
        using var terminal = new StringWriter(CultureInfo.InvariantCulture);
        var environment = new FakeEnvironment(new Dictionary<string, string>(StringComparer.Ordinal));

        CliApp.WriteUsage(redirected, new Styling.SystemConsoleStyle(environment, redirected, true, TextWriter.Null, true, new Styling.UnixVirtualTerminal()));
        CliApp.WriteUsage(terminal, new Styling.SystemConsoleStyle(environment, terminal, false, TextWriter.Null, true, new Styling.UnixVirtualTerminal()));

        Assert.Equal(GroupedHelp, redirected.ToString());
        Assert.DoesNotContain(ConsoleStyleTests.Escape, redirected.ToString(), StringComparison.Ordinal);
        Assert.Contains("  \u001b[38;5;214mgrants" + Styling.SystemConsoleStyle.Reset + "      list or revoke", terminal.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("approve", "approve agents' requests in this terminal")]
    [InlineData("clients", "show or change what each agent can see and do")]
    public void ApproveAndClients_AreVerbsOfTheirOwn(string verb, string usage)
    {
        using var harness = new CliHarness();

        Assert.Equal(CliApp.ExitSuccess, harness.Run(verb, "--help"));
        Assert.StartsWith(usage, harness.Out, StringComparison.Ordinal);
        Assert.Empty(harness.Err);
    }

    /// <summary>
    /// A removed spelling is refused on one line naming the one that replaced it, and runs nothing, its help
    /// included: no password is asked for, nothing is printed, copied, started or written (D-0470).
    /// </summary>
    [Theory]
    [InlineData("keypaste: keypaste agent is now keypaste approve", "agent")]
    [InlineData("keypaste: keypaste agent is now keypaste approve", "agent", "--help")]
    [InlineData("keypaste: keypaste mcp serve is now keypaste approve", "mcp", "serve")]
    [InlineData("keypaste: keypaste mcp serve is now keypaste approve", "mcp", "serve", "--help")]
    [InlineData("keypaste: keypaste mcp setup is now keypaste setup", "mcp", "setup", "--client", "claude-code")]
    [InlineData("keypaste: keypaste mcp setup is now keypaste setup", "mcp", "setup", "--help")]
    [InlineData("keypaste: keypaste mcp policy is now keypaste clients", "mcp", "policy", "cursor", "--mode", "ask")]
    [InlineData("keypaste: keypaste mcp policy is now keypaste clients", "mcp", "policy", "--help")]
    [InlineData("keypaste get: --show is now --reveal", "get", "solo", "--show")]
    [InlineData("keypaste get: --show is now --reveal", "get", "solo", "--reveal", "--show")]
    [InlineData("keypaste get: --show is now --reveal", "get", "--show", "--help")]
    public void ARemovedSpelling_IsRefused_NamingItsReplacement_AndRunsNothing(string refusal, params string[] args)
    {
        using var harness = new CliHarness();
        harness.SeedVault("removed-spelling-master", ("solo", "solo-secret"));
        var asked = harness.Prompt.PromptsSeen.Count;

        harness.AssertExit(CliApp.ExitUsageError, harness.Run([.. args, "--vault", harness.VaultPath]));

        Assert.Equal(refusal + Environment.NewLine, harness.Err);
        Assert.Empty(harness.Out);
        Assert.Equal(asked, harness.Prompt.PromptsSeen.Count);
        Assert.Equal(0, harness.Clipboard.SetCount);
        Assert.Empty(harness.ProcessRunner.Calls);
        Assert.Empty(harness.ProcessLauncher.Started);
        Assert.False(File.Exists(KeypasteHome.ClientsPath(harness.Environment[KeypasteHome.EnvironmentVariable])));
    }

    /// <summary>
    /// What the parser would otherwise accept is a usage error that runs nothing (D-0678): an undeclared option an operand
    /// could take, a flag given a value or twice, and a first argument in brackets, which it reads as a directive.
    /// </summary>
    [Theory]
    [InlineData("keypaste get: unknown option '--bogus'", "get", "solo", "--bogus")]
    [InlineData("keypaste get: option '--reveal' does not take a value", "get", "solo", "--reveal=yes")]
    [InlineData("keypaste get: option '--reveal' given more than once", "get", "solo", "--reveal", "--reveal")]
    [InlineData("keypaste get: unknown command '[diagram]'", "[diagram]", "get", "solo")]
    public void WhatTheParserWouldTolerate_IsRefused_AndRunsNothing(string refusal, params string[] args)
    {
        using var harness = new CliHarness();
        harness.SeedVault("tolerated-master", ("solo", "solo-secret"));
        var asked = harness.Prompt.PromptsSeen.Count;

        harness.AssertExit(CliApp.ExitUsageError, harness.Run([.. args, "--vault", harness.VaultPath]));

        Assert.Equal(refusal + Environment.NewLine + "see keypaste get --help" + Environment.NewLine, harness.Err);
        Assert.Empty(harness.Out);
        Assert.Equal(asked, harness.Prompt.PromptsSeen.Count);
        Assert.Equal(0, harness.Clipboard.SetCount);
    }

    /// <summary>
    /// An argument starting with <c>@</c> names no response file and one like <c>-pdev</c> is no bundle, so neither adds
    /// an option the person did not type, such as a <c>--reveal</c> (D-0678).
    /// </summary>
    [Fact]
    public void AnAtSignOrABundle_IsAnArgumentAsWritten()
    {
        using var harness = new CliHarness();
        harness.SeedVault("written-master", ("solo", "solo-secret"));
        var responses = Path.Combine(harness.Directory, "reveal.rsp");
        File.WriteAllText(responses, "--reveal");

        harness.AssertExit(CliApp.ExitUsageError, harness.Run("get", "solo", "@" + responses, "--vault", harness.VaultPath));
        Assert.Empty(harness.Out);
        Assert.Contains("only totp may follow", harness.Err, StringComparison.Ordinal);

        var bundled = CliApp.Parse(["run", "-pdev", "--", "true"]);
        Assert.Equal(["-pdev"], Commands.RunCommand.Projects(bundled));
        Assert.Null(bundled.GetValue(CliOptions.Profile));
    }

    [Fact]
    public void EveryVerbInHelp_Dispatches()
    {
        var verbs = GroupedHelp
            .Split(Environment.NewLine)
            .SkipWhile(line => line != "SECRETS")
            .TakeWhile(line => line != "FLAGS")
            .Where(line => line.StartsWith("  ", StringComparison.Ordinal))
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0])
            .ToList();

        Assert.Equal(27, verbs.Count);

        foreach (var verb in verbs.Where(verb => !Program.StartsBridge([verb, "--help"])))
        {
            using var harness = new CliHarness();

            Assert.Equal(CliApp.ExitSuccess, harness.Run(verb, "--help"));
            Assert.DoesNotContain("unknown command", harness.Err, StringComparison.Ordinal);
            Assert.NotEmpty(harness.Out);
        }
    }

    private static string GroupedHelp => string.Join(
        Environment.NewLine,
        [
            $"keypaste {CoreInfo.Version} · a simple, local password manager on your KeePass file",
            "",
            "USAGE",
            "  keypaste <command> [flags]",
            "",
            "SECRETS",
            "  get         copy a secret to the clipboard, or print it with --reveal",
            "  show        show an item's fields and tags; values only with --reveal",
            "  set         create or update a secret, or its custom fields",
            "  field       list, rename or remove an item's custom fields",
            "  attachment  list, add, replace, remove or save an item's attachments",
            "  rotate      replace a secret with a new generated one",
            "  run         run a command with secrets in its environment",
            "  env         import, export and diff .env environments",
            "",
            "AGENTS",
            "  setup       connect this computer's AI tools to your vault",
            "  mcp         the MCP server your AI tools start",
            "  approve     approve agents' requests in this terminal",
            "  clients     show or change what each agent can see and do",
            "  doctor      check the vault, the AI tools and the app, and say what to fix",
            "  grants      list or revoke time-boxed access",
            "  token       create scoped, inject-only tokens",
            "  log         show the hash-chained activity log",
            "",
            "VAULT",
            "  import      copy in an export or a KeePass database, or edit it in place",
            "  share       create an encrypted, expiring link",
            "  lock        lock now and pause all agents",
            "  init        create a new vault",
            "  use         choose the vault used when no --vault is given",
            "  add         add an item",
            "  ls          list groups and items",
            "  search      find items by title, group, username or URL",
            "  rm          remove an item",
            "  generate    print a passphrase (--words N); it is not stored",
            "  access      change the master password or keyfile",
            "",
            "FLAGS",
            $"  --vault <path>    which vault to use, or set {VaultLocator.EnvironmentVariable}",
            $"  --keyfile <path>  the keyfile it needs too, or set {VaultLocator.KeyfileEnvironmentVariable}",
            "  --json            machine-readable output from ls, search, show, field ls,",
            "                    attachment ls, env ls, log, grants, doctor, token ls,",
            "                    share ls, clients",
            "  -h, --help        help for any command",
            "",
            "  version prints the version.",
            "",
            "EXIT CODES",
            "  0 ok  1 usage  2 error  3 not found  4 wrong password  5 audit log tampered",
            "  once a `run` command starts, its exit code is keypaste's own.",
            "",
            "passwords are never echoed. Press Escape at a prompt to cancel.",
            "",
        ]);

    [Fact]
    public void UnknownCommand_WritesToStderr_AndIsAUsageError()
    {
        using var stdout = new StringWriter(CultureInfo.InvariantCulture);
        using var stderr = new StringWriter(CultureInfo.InvariantCulture);

        var exitCode = CliApp.Run(["nope"], stdout, stderr);

        Assert.Equal(CliApp.ExitUsageError, exitCode);
        Assert.Empty(stdout.ToString());
        Assert.Contains("nope", stderr.ToString(), StringComparison.Ordinal);
    }
}
