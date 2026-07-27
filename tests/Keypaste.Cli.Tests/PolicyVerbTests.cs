using Keypaste.Core.Audit;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// <c>keypaste policy ls</c> — the one place a person can check that the rules they wrote mean what
/// they think they mean.
/// </summary>
public sealed class PolicyVerbTests : IDisposable
{
    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    [Fact]
    public void ItRendersEachRuleWithItsClientFieldsLifetimeAndAllowance()
    {
        Assert.Equal(CliApp.ExitSuccess, Run(AgentPolicyTests.Valid + "\nmax_per_hour    = 20"));

        Assert.Contains("1 rule, from", _cli.Out, StringComparison.Ordinal);
        Assert.Contains("1. The client labelled \"claude-code\"", _cli.Out, StringComparison.Ordinal);
        Assert.Contains("may read the password of entries whose", _cli.Out, StringComparison.Ordinal);
        Assert.Contains("for up to 5 minutes, without asking you.", _cli.Out, StringComparison.Ordinal);
        Assert.Contains("At most 20 times an hour", _cli.Out, StringComparison.Ordinal);
    }

    /// <summary>
    /// The whole reason this command renders rather than echoes. <c>env/dev*</c> is the obvious way
    /// to write "the dev environment" and it means <em>group exactly <c>env</c>, title starting
    /// <c>dev</c></em> — so it matches entries the author never pictured and none of the ones they
    /// did. Printing their own line back would confirm the belief; printing the two halves tests it.
    /// </summary>
    [Fact]
    public void ItRendersWhatEachPatternParsedTo_NeverTheLineTheUserWrote()
    {
        Assert.Equal(
            CliApp.ExitSuccess,
            Run(AgentPolicyTests.Valid.Replace("env/dev/**", "env/dev*", StringComparison.Ordinal)));

        Assert.Contains("group path matches   env\n", _cli.Out.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("title matches        dev*", _cli.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void SeveralPatternsInOneRule_AreShownAsAlternatives()
    {
        Assert.Equal(
            CliApp.ExitSuccess,
            Run(AgentPolicyTests.Valid.Replace(
                "[\"env/dev/**\"]",
                "[\"env/dev/**\", \"env/test/**\"]",
                StringComparison.Ordinal)));

        Assert.Contains("...or whose", _cli.Out, StringComparison.Ordinal);
    }

    /// <summary>
    /// A bridge started without <c>--client-label</c> matches no rule at all, so <c>"*"</c> is not
    /// "any client" and must never be described as one.
    /// </summary>
    [Fact]
    public void AWildcardClient_IsShownAsAnyLabelledClient()
    {
        Assert.Equal(
            CliApp.ExitSuccess,
            Run(AgentPolicyTests.Valid.Replace("\"claude-code\"", "\"*\"", StringComparison.Ordinal)));

        Assert.Contains("Any labelled client", _cli.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("Any client", _cli.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void ARuleWithNoAllowance_SaysThereIsNoLimit()
    {
        Assert.Equal(CliApp.ExitSuccess, Run(AgentPolicyTests.Valid));

        Assert.Contains("No limit on how often.", _cli.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFooterStatesWhatNoSingleRuleCanSay()
    {
        Assert.Equal(CliApp.ExitSuccess, Run(AgentPolicyTests.Valid));

        Assert.Contains("first one that matches decides", _cli.Out, StringComparison.Ordinal);
        Assert.Contains("--client-label matches no rule", _cli.Out, StringComparison.Ordinal);
        Assert.Contains("cannot widen what a bridge was started with --expose", _cli.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void ItShowsTheDigestOfTheBytesItRead_SoItCanBeComparedWithARunningAgent()
    {
        Assert.Equal(CliApp.ExitSuccess, Run(AgentPolicyTests.Valid));

        Assert.Contains("sha256:", _cli.Out, StringComparison.Ordinal);
    }

    /// <summary>
    /// A broken authorization file goes to stderr and exits non-zero, so a script reading stdout
    /// gets an empty rule list — which is the truth about what is in force — and CI notices.
    /// </summary>
    [Fact]
    public void AMalformedFile_PrintsNothingToStdout_AndExitsNonZero()
    {
        Assert.Equal(CliApp.ExitUsageError, Run("[[allow]]\nclientt = \"x\"\n"));

        Assert.Empty(_cli.Out);
        Assert.Contains("is not usable", _cli.Err, StringComparison.Ordinal);
        Assert.Contains("line 2", _cli.Err, StringComparison.Ordinal);
        Assert.Contains("Every request is shown to you", _cli.Err, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing pre-authorized is keypaste's default and correct state. Reporting the safest possible
    /// configuration as a failure would be backwards, and would make an empty policy look broken in
    /// anybody's CI.
    /// </summary>
    [Theory]
    [InlineData(null, "No policy file at")]
    [InlineData("", "No rules in")]
    [InlineData("# nothing here\n", "No rules in")]
    public void WithNothingPreAuthorized_ItSaysSoOnStdoutAndSucceeds(string? policy, string expected)
    {
        Assert.Equal(CliApp.ExitSuccess, Run(policy));

        Assert.Contains(expected, _cli.Out, StringComparison.Ordinal);
        Assert.Contains("Every request is shown to you", _cli.Out, StringComparison.Ordinal);
        Assert.Empty(_cli.Err);
    }

    /// <summary>
    /// <c>--vault</c> is not in this command's option spec at all, so passing it is a usage error
    /// rather than a flag that looks accepted and does nothing. Reading a policy file resolves
    /// nothing and decrypts nothing, and a master password prompt in front of the command an
    /// operator reaches for when something already looks wrong would be the wrong trade.
    /// </summary>
    [Fact]
    public void ItTakesNoVault_AndSaysSoRatherThanIgnoringOne()
    {
        Write(AgentPolicyTests.Valid);

        Assert.Equal(CliApp.ExitUsageError, _cli.Run("policy", "ls", "--vault", _cli.VaultPath));
        Assert.Empty(_cli.Out);
        Assert.Contains("--vault", _cli.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnexpectedArgument_IsAUsageError()
    {
        Write(AgentPolicyTests.Valid);

        Assert.Equal(CliApp.ExitUsageError, _cli.Run("policy", "ls", "extra"));
        Assert.Contains("unexpected argument", _cli.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownSubcommand_IsAUsageErrorThatShowsTheUsage()
    {
        Assert.Equal(CliApp.ExitUsageError, _cli.Run("policy", "add"));

        Assert.Contains("unknown subcommand 'add'", _cli.Err, StringComparison.Ordinal);
        Assert.Contains("usage: keypaste policy ls", _cli.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void HelpGoesToStdoutAndSucceeds()
    {
        Assert.Equal(CliApp.ExitSuccess, _cli.Run("policy", "--help"));

        Assert.Contains("usage: keypaste policy ls", _cli.Out, StringComparison.Ordinal);
        Assert.Contains("keypaste reads and never writes", _cli.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void TheVerbIsListedInTheTopLevelUsage()
    {
        _cli.Run("help");

        Assert.Contains("policy ls", _cli.Out, StringComparison.Ordinal);
    }

    private int Run(string? policy)
    {
        var path = Write(policy);
        return _cli.Run("policy", "ls", "--policy", path);
    }

    private string Write(string? policy)
    {
        var path = Path.Combine(_cli.Directory, KeypasteHome.PolicyFileName);

        if (policy is null)
        {
            File.Delete(path);
        }
        else
        {
            File.WriteAllText(path, policy);
        }

        return path;
    }
}
