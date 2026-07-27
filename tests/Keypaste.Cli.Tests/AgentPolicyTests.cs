using Keypaste.Cli.Commands;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Policy;
using Xunit;

namespace Keypaste.Cli.Tests;

/// <summary>
/// What <c>keypaste agent</c> tells the person leaving it running.
/// </summary>
/// <remarks>
/// The banner is the operator's only signal about a feature that otherwise makes no noise. All six
/// states a policy file can be in reach an <em>agent</em> as the same thing — no rules, so every
/// request prompts — and they must be distinguishable <em>here</em>, because "I wrote a rule and it
/// is not working" and "I have no rules" need different next steps.
/// </remarks>
public sealed class AgentPolicyTests : IDisposable
{
    private readonly string _home =
        Path.Combine(Path.GetTempPath(), "keypaste-agent-policy-" + Guid.NewGuid().ToString("N")[..12]);

    public AgentPolicyTests() => Directory.CreateDirectory(_home);

    public void Dispose() => Directory.Delete(_home, recursive: true);

    [Fact]
    public void WithRulesInForce_ItNamesTheCountTheDigestAndWhereToSeeThem()
    {
        var (harness, text) = Announce(Valid);

        Assert.Contains("policy: 1 rule from", text, StringComparison.Ordinal);
        Assert.Contains("sha256:", text, StringComparison.Ordinal);
        Assert.Contains("keypaste policy ls", text, StringComparison.Ordinal);

        harness.Dispose();
    }

    /// <summary>
    /// The one claim that stops being true the moment a rule is in force. Printing "nothing is
    /// released without you saying yes" while a standing rule releases things silently is the kind
    /// of small untruth this product cannot afford.
    /// </summary>
    [Fact]
    public void WithRulesInForce_ItStopsClaimingNothingIsReleasedWithoutYou()
    {
        var (withRules, withText) = Announce(Valid);
        var (without, withoutText) = Announce(policy: null);

        Assert.Contains("unless a policy rule covers it", withText, StringComparison.Ordinal);
        Assert.DoesNotContain("unless a policy rule covers it", withoutText, StringComparison.Ordinal);
        Assert.Contains("nothing is released without you saying yes.", withoutText, StringComparison.Ordinal);

        withRules.Dispose();
        without.Dispose();
    }

    [Fact]
    public void WithNoFile_ItSaysEveryRequestIsShownToYou()
    {
        var (harness, text) = Announce(policy: null);

        Assert.Contains("policy: no file at", text, StringComparison.Ordinal);
        Assert.Contains("every request is shown to you", text, StringComparison.Ordinal);

        harness.Dispose();
    }

    [Theory]
    [InlineData("", "a zero-byte file")]
    [InlineData("# just a comment\n", "comments only")]
    public void WithAFileDeclaringNoRules_ItSaysSoWithoutCallingItBroken(string policy, string what)
    {
        var (harness, text) = Announce(policy);

        Assert.Contains("policy: no rules in", text, StringComparison.Ordinal);
        Assert.DoesNotContain("NOT in force", text, StringComparison.Ordinal);
        Assert.DoesNotContain("unless a policy rule covers it", text, StringComparison.Ordinal);

        harness.Dispose();
        Assert.NotEmpty(what);
    }

    /// <summary>
    /// A rejected file says so twice: what is wrong and where, then what it means and what to do.
    /// One line would leave the operator knowing their file is broken but not that everything is
    /// still safe.
    /// </summary>
    [Fact]
    public void WithAMalformedFile_ItSaysTheFileIsNotInForceAndNamesTheLine()
    {
        var (harness, text) = Announce("[[allow]]\nclientt = \"x\"\n");

        Assert.Contains("NOT in force", text, StringComparison.Ordinal);
        Assert.Contains("line 2", text, StringComparison.Ordinal);
        Assert.Contains("every request will be shown to you", text, StringComparison.Ordinal);
        Assert.Contains("Fix it and restart", text, StringComparison.Ordinal);
        Assert.DoesNotContain("unless a policy rule covers it", text, StringComparison.Ordinal);

        harness.Dispose();
    }

    [Fact]
    public void TheUsageNamesThePolicyFlagAndItsAllOrNothingRule()
    {
        using var writer = new StringWriter();
        AgentCommand.WriteUsage(writer);
        var text = writer.ToString();

        Assert.Contains("--policy <path>", text, StringComparison.Ordinal);
        Assert.Contains(KeypasteHome.PolicyFileName, text, StringComparison.Ordinal);
        Assert.Contains("without asking", text, StringComparison.Ordinal);
        Assert.Contains("the whole of it is ignored", text, StringComparison.Ordinal);
    }

    [Fact]
    public void APolicyPathThatIsNotAFile_IsAWarningAndNeverAStartupFailure()
    {
        // A planted `chmod 000`, a typo, a directory: none of them may stop a person reaching their
        // own vault. The release direction is already the safe one.
        var (harness, text) = Announce("[[deny]]\n");

        Assert.Contains("NOT in force", text, StringComparison.Ordinal);
        Assert.DoesNotContain("could not listen", text, StringComparison.Ordinal);

        harness.Dispose();
    }

    // internal, not private: .editorconfig applies the _camelCase field rule to private consts too.
    internal const string Valid = """
        [[allow]]
        client          = "claude-code"
        entries         = ["env/dev/**"]
        fields          = ["password"]
        max_ttl_seconds = 300
        """;

    private (CliHarness Harness, string Text) Announce(string? policy)
    {
        var path = Path.Combine(_home, KeypasteHome.PolicyFileName);

        if (policy is null)
        {
            File.Delete(path);
        }
        else
        {
            File.WriteAllText(path, policy);
        }

        var harness = new CliHarness();

        AgentCommand.Announce(
            harness.VaultPath,
            "keypaste-agent-test",
            ApprovalLimits.Default,
            PolicyLoader.Load(path),
            harness.NewContext());

        return (harness, harness.Err);
    }
}
