using System.Globalization;
using Keypaste.Core;
using Xunit;

namespace Keypaste.Cli.Tests;

public sealed class CliAppTests
{
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
    public void EveryVerbInHelp_Dispatches()
    {
        var verbs = GroupedHelp
            .Split(Environment.NewLine)
            .SkipWhile(line => line != "SECRETS")
            .TakeWhile(line => line != "FLAGS")
            .Where(line => line.StartsWith("  ", StringComparison.Ordinal))
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0])
            .ToList();

        Assert.Equal(21, verbs.Count);

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
            "  get       copy a secret to the clipboard, or print it with --reveal",
            "  set       create or update a secret, or its custom fields",
            "  field     list or remove an entry's custom fields",
            "  rotate    replace a secret with a new generated one",
            "  run       run a command with secrets in its environment",
            "  env       import, export and diff .env profiles",
            "",
            "AGENTS",
            "  mcp       the MCP server your AI clients start; mcp serve approves here",
            "  grants    list or revoke time-boxed access",
            "  token     create scoped, inject-only tokens",
            "  log       show the hash-chained activity log",
            "",
            "VAULT",
            "  import    copy in a .kdbx, or keep editing it in place",
            "  share     create an encrypted, expiring link",
            "  lock      lock now and pause all agents",
            "  init      create a new vault",
            "  use       choose the vault used when no --vault is given",
            "  add       add an entry",
            "  ls        list groups and entries",
            "  rm        remove an entry",
            "  generate  print a passphrase (--words N); it is not stored",
            "  access    change the master password or keyfile",
            "  policy    show the standing rules that skip the prompt",
            "",
            "FLAGS",
            $"  --vault <path>    which vault to use, or set {VaultLocator.EnvironmentVariable}",
            $"  --keyfile <path>  the keyfile it needs too, or set {VaultLocator.KeyfileEnvironmentVariable}",
            "  --json            machine-readable output from ls, field ls, env ls, log,",
            "                    grants, token ls, share ls and mcp policy",
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
