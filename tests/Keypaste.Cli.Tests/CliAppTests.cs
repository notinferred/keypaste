using System.Globalization;
using Keypaste.Core;
using Keypaste.Core.Audit;
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

    [Theory]
    [InlineData("approve", "usage: keypaste approve ")]
    [InlineData("clients", "usage: keypaste clients ")]
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

        Assert.Equal(26, verbs.Count);

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
            "  show      show an item's fields and tags; values only with --reveal",
            "  set       create or update a secret, or its custom fields",
            "  field     list or remove an item's custom fields",
            "  rotate    replace a secret with a new generated one",
            "  run       run a command with secrets in its environment",
            "  env       import, export and diff .env environments",
            "",
            "AGENTS",
            "  setup     connect this computer's AI tools to your vault",
            "  mcp       the MCP server your AI tools start",
            "  approve   approve agents' requests in this terminal",
            "  clients   show or change what each agent can see and do",
            "  doctor    check the vault, the AI tools and the app, and say what to fix",
            "  grants    list or revoke time-boxed access",
            "  token     create scoped, inject-only tokens",
            "  log       show the hash-chained activity log",
            "",
            "VAULT",
            "  import    copy in a KeePass database, or keep editing it in place",
            "  share     create an encrypted, expiring link",
            "  lock      lock now and pause all agents",
            "  init      create a new vault",
            "  use       choose the vault used when no --vault is given",
            "  add       add an item",
            "  ls        list groups and items",
            "  search    find items by title, group, username or URL",
            "  rm        remove an item",
            "  generate  print a passphrase (--words N); it is not stored",
            "  access    change the master password or keyfile",
            "",
            "FLAGS",
            $"  --vault <path>    which vault to use, or set {VaultLocator.EnvironmentVariable}",
            $"  --keyfile <path>  the keyfile it needs too, or set {VaultLocator.KeyfileEnvironmentVariable}",
            "  --json            machine-readable output from ls, search, show, field ls,",
            "                    env ls, log, grants, doctor, token ls, share ls, clients",
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
