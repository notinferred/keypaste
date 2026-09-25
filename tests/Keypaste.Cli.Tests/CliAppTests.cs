using System.Globalization;
using Keypaste.Core;
using Xunit;

namespace Keypaste.Cli.Tests;

public sealed class CliAppTests
{
    /// <summary>
    /// The wiring proof: the CLI's greeting must be produced by keypaste-core, not by
    /// the CLI itself. Fails to compile if the reference is dropped and fails at runtime
    /// if the CLI ever grows its own copy — docs/PRODUCT.md law 4.3 as an executable assertion.
    /// </summary>
    [Fact]
    public void Hello_WritesTheCoreGreetingToStdout_AndExitsZero()
    {
        using var stdout = new StringWriter(CultureInfo.InvariantCulture);
        using var stderr = new StringWriter(CultureInfo.InvariantCulture);

        var exitCode = CliApp.Run(["hello"], stdout, stderr);

        Assert.Equal(CliApp.ExitSuccess, exitCode);
        Assert.Equal(CoreInfo.Hello(), stdout.ToString().TrimEnd());
        Assert.Empty(stderr.ToString());
    }

    /// <summary>
    /// Deliberate change from Stage 0.1, where no arguments greeted you. That was scaffolding;
    /// a tool with verbs should say what they are. The <c>hello</c> verb itself survives above,
    /// because it is the docs/PRODUCT.md law 4.3 wiring proof.
    /// </summary>
    [Fact]
    public void NoArguments_PrintsHelpToStderr_Exit1()
    {
        using var harness = new CliHarness();

        Assert.Equal(CliApp.ExitUsageError, harness.Run());
        Assert.Empty(harness.Out);
        Assert.Equal(GroupedHelp, harness.Err);
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
        Assert.Contains("  \u001b[38;5;214mgrants" + Styling.SystemConsoleStyle.Reset + "    list or revoke", terminal.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void McpServe_IsAgent()
    {
        using var alias = new CliHarness();
        using var verb = new CliHarness();

        Assert.Equal(CliApp.ExitSuccess, alias.Run("mcp", "serve", "--help"));
        Assert.Equal(CliApp.ExitSuccess, verb.Run("agent", "--help"));
        Assert.Equal(verb.Out, alias.Out);
        Assert.Contains("usage: keypaste agent", alias.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void McpSetup_IsSetup()
    {
        using var alias = new CliHarness();
        using var verb = new CliHarness();

        Assert.Equal(CliApp.ExitSuccess, alias.Run("mcp", "setup", "--help"));
        Assert.Equal(CliApp.ExitSuccess, verb.Run("setup", "--help"));
        Assert.Equal(verb.Out, alias.Out);
        Assert.NotEmpty(alias.Out);
    }

    [Fact]
    public void Mcp_Alone_PrintsItsUsage()
    {
        using var harness = new CliHarness();

        Assert.Equal(CliApp.ExitUsageError, harness.Run("mcp"));
        Assert.Empty(harness.Out);
        Assert.StartsWith("usage: keypaste mcp <serve|setup|policy> [options]", harness.Err, StringComparison.Ordinal);
        Assert.Contains("the MCP server itself is keypaste-mcp", harness.Err, StringComparison.Ordinal);
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

        Assert.Equal(19, verbs.Count);

        foreach (var verb in verbs)
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
            $"keypaste {CoreInfo.Version} · secrets for developers and their agents",
            "",
            "USAGE",
            "  keypaste <command> [flags]",
            "",
            "SECRETS",
            "  get       copy a secret to the clipboard, or print it with --reveal",
            "  set       create or update a secret",
            "  rotate    replace a secret with a new generated one",
            "  run       run a command with secrets in its environment",
            "  env       import, export and diff .env profiles",
            "",
            "AGENTS",
            "  mcp       approve agents' requests here, or connect MCP clients",
            "  grants    list or revoke time-boxed access",
            "  token     create scoped, inject-only tokens",
            "  log       show the hash-chained activity log",
            "",
            "VAULT",
            "  import    copy in a .kdbx, or keep editing it in place",
            "  share     create an encrypted, expiring link",
            "  lock      lock now and pause all agents",
            "  init      create a new vault",
            "  add       add an entry",
            "  ls        list groups and entries",
            "  rm        remove an entry",
            "  generate  print a password or passphrase; it is not stored",
            "  access    change the master password or keyfile",
            "  policy    show the standing rules that skip the prompt",
            "",
            "FLAGS",
            $"  --vault <path>    which vault to use, or set {VaultLocator.EnvironmentVariable}",
            $"  --keyfile <path>  the keyfile it needs too, or set {VaultLocator.KeyfileEnvironmentVariable}",
            "  --json            machine-readable output from ls, env ls, log, grants,",
            "                    token ls, share ls and mcp policy",
            "  -h, --help        help for any command",
            "",
            "  agent is mcp serve, setup is mcp setup, version prints the version.",
            "",
            "EXIT CODES",
            "  0 ok  1 usage  2 error  3 not found  4 wrong password  5 audit log tampered",
            "  once a `run` command starts, its exit code is keypaste's own.",
            "",
            "passwords are never echoed. Press Escape at a prompt to cancel.",
            "",
        ]);

    [Fact]
    public void UnknownCommand_WritesToStderr_AndExitsNonZero()
    {
        using var stdout = new StringWriter(CultureInfo.InvariantCulture);
        using var stderr = new StringWriter(CultureInfo.InvariantCulture);

        var exitCode = CliApp.Run(["nope"], stdout, stderr);

        Assert.NotEqual(CliApp.ExitSuccess, exitCode);
        Assert.Empty(stdout.ToString());
        Assert.Contains("nope", stderr.ToString(), StringComparison.Ordinal);
    }
}
