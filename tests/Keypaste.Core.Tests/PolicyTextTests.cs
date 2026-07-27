using Keypaste.Core.Policy;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// What a person is shown about a rule, which is the only defence against writing one that means
/// something other than what they meant.
/// </summary>
public sealed class PolicyTextTests
{
    /// <summary>
    /// The split is lossless: nothing is dropped or invented on the way to the screen, so the two
    /// halves shown are the whole pattern. Paired with
    /// <c>PolicyRuleTests.APolicyRuleWithATrailingStar_ConstrainsTheTitleNotTheGroup</c>, which
    /// asserts the same split against what the matcher actually does with it.
    /// </summary>
    [Theory]
    [InlineData("env/**", "env/**", "anything")]
    [InlineData("**", "**", "anything")]
    [InlineData("env/dev/**", "env/dev/**", "anything")]
    [InlineData("env/dev*", "env", "dev*")]
    [InlineData("env/*/STRIPE_KEY", "env/*", "STRIPE_KEY")]
    [InlineData("personal/bank", "personal", "bank")]
    [InlineData("loose", "(the top level)", "loose")]
    public void ThePatternIsShownAsTheTwoHalvesItIsMatchedAs(string glob, string group, string title)
    {
        var halves = PolicyText.Halves(glob);

        Assert.Equal(group, halves.Group);
        Assert.Equal(title, halves.Title);

        var rebuilt = string.Equals(halves.Title, "anything", StringComparison.Ordinal) ? halves.Group
            : string.Equals(halves.Group, "(the top level)", StringComparison.Ordinal) ? halves.Title
            : halves.Group + "/" + halves.Title;

        Assert.Equal(glob, rebuilt);
    }

    /// <summary>
    /// The distinction the whole client-keying decision rests on. A bridge started without
    /// <c>--client-label</c> matches no rule, so <c>"*"</c> is not "any client" and must never be
    /// described as one.
    /// </summary>
    [Fact]
    public void AWildcardClient_IsDescribedAsAnyLabelledClient_NeverAsAnyClient()
    {
        var text = PolicyText.Who(Rule("\"*\"", "300", "20"));

        Assert.Equal("Any labelled client", text);
        Assert.Contains("labelled", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ANamedClient_IsQuoted()
    {
        Assert.Equal("The client labelled \"claude-code\"", PolicyText.Who(Rule("\"claude-code\"", "300", "20")));
    }

    /// <summary>
    /// An unlimited rule says so out loud. A blank where a number would go reads as a number nobody
    /// happened to mention, and "no limit on how often" is what the person actually signed.
    /// </summary>
    [Fact]
    public void ARuleWithNoAllowance_SaysThereIsNoLimit()
    {
        Assert.Equal("No limit on how often.", PolicyText.Limit(Rule("\"c\"", "300", null)));
    }

    [Fact]
    public void ARuleWithAnAllowance_SaysWhatHappensWhenItIsSpent()
    {
        var text = PolicyText.Limit(Rule("\"c\"", "300", "20"));

        Assert.Contains("At most 20 times an hour", text, StringComparison.Ordinal);
        Assert.Contains("refused", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, "1 second")]
    [InlineData(45, "45 seconds")]
    [InlineData(60, "1 minute")]
    [InlineData(90, "1 minute 30 seconds")]
    [InlineData(300, "5 minutes")]
    [InlineData(3600, "1 hour")]
    public void ADurationIsWrittenTheWayAPersonWouldSayIt(int seconds, string expected) =>
        Assert.Equal(expected, PolicyText.Duration(seconds));

    [Fact]
    public void FieldsAreJoinedWithOr()
    {
        Assert.Equal("password", PolicyText.Fields(Fields("[\"password\"]")));
        Assert.Equal("username or password", PolicyText.Fields(Fields("[\"username\", \"password\"]")));
        Assert.Equal(
            "username, url or password",
            PolicyText.Fields(Fields("[\"username\", \"url\", \"password\"]")));
    }

    [Fact]
    public void ARuleIsDescribedWithoutEchoingTheLineItWasWrittenOn()
    {
        var lines = PolicyText.Describe(Rule("\"claude-code\"", "300", "20"));
        var text = string.Join('\n', lines);

        Assert.StartsWith("1. The client labelled \"claude-code\"", text, StringComparison.Ordinal);
        Assert.Contains("group path matches   env/dev/**", text, StringComparison.Ordinal);
        Assert.Contains("title matches        anything", text, StringComparison.Ordinal);
        Assert.Contains("for up to 5 minutes, without asking you.", text, StringComparison.Ordinal);
        Assert.Contains("At most 20 times an hour", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The footer states the four things about a rule set that cannot be read off any single rule,
    /// and each is a thing an operator would otherwise have to guess.
    /// </summary>
    [Fact]
    public void TheFooterNamesWhatNoSingleRuleCanSay()
    {
        var text = string.Join('\n', PolicyText.Footer);

        Assert.Contains("first one that matches decides", text, StringComparison.Ordinal);
        Assert.Contains("shown to you as usual", text, StringComparison.Ordinal);
        Assert.Contains("--client-label matches no rule", text, StringComparison.Ordinal);
        Assert.Contains("cannot widen what a bridge was started with --expose", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ItRejectsNullAndNonsense()
    {
        Assert.Throws<ArgumentNullException>(() => PolicyText.Describe(null!));
        Assert.Throws<ArgumentNullException>(() => PolicyText.Who(null!));
        Assert.Throws<ArgumentNullException>(() => PolicyText.Fields(null!));
        Assert.Throws<ArgumentNullException>(() => PolicyText.Limit(null!));
        Assert.Throws<ArgumentNullException>(() => PolicyText.Halves(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => PolicyText.Duration(0));
    }

    private static PolicyRule Fields(string fields) => Rule("\"c\"", "300", "20", fields);

    private static PolicyRule Rule(
        string client,
        string ttl,
        string? perHour,
        string fields = "[\"password\"]")
    {
        var text = $"""
            [[allow]]
            client          = {client}
            entries         = ["env/dev/**"]
            fields          = {fields}
            max_ttl_seconds = {ttl}
            """;

        if (perHour is not null)
        {
            text += $"\nmax_per_hour    = {perHour}";
        }

        Assert.True(Toml.TryParse(text, out var syntax, out var syntaxError), syntaxError);
        Assert.True(PolicyDocument.TryCreate(syntax, out var document, out var error), error);
        return Assert.Single(document.Rules);
    }
}
