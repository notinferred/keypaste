using Keypaste.Core.Policy;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// What a rule means, with no file, no vault and no approver anywhere near it.
/// </summary>
public sealed class PolicyRuleTests
{
    // internal, not private: .editorconfig applies the _camelCase field rule to private consts too.
    internal const string Valid = """
        [[allow]]
        client          = "claude-code"
        entries         = ["env/dev/**"]
        fields          = ["password"]
        max_ttl_seconds = 300
        max_per_hour    = 20
        """;

    [Fact]
    public void AWellFormedRule_ReadsBack()
    {
        var rule = Only(Valid);

        Assert.Equal(1, rule.Ordinal);
        Assert.Equal("allow#1", rule.Id);
        Assert.Equal("claude-code", rule.Client);
        Assert.Equal("env/dev/**", Assert.Single(rule.Scope.Globs));
        Assert.Equal("password", Assert.Single(rule.Fields));
        Assert.Equal(300, rule.MaximumTtlSeconds);
        Assert.Equal(20, rule.MaximumPerHour);
    }

    [Fact]
    public void MaximumPerHourIsTheOnlyOptionalKey()
    {
        var rule = Only(Valid.Replace("max_per_hour    = 20", string.Empty, StringComparison.Ordinal));

        Assert.Null(rule.MaximumPerHour);
    }

    /// <summary>
    /// The trap the specification for this feature walked into, asserted in <b>both</b> directions
    /// so the test documents the surprise rather than the wish.
    /// </summary>
    /// <remarks>
    /// Unless a pattern's last segment is exactly <c>**</c>, that segment is the <em>title</em>. So
    /// <c>env/dev*</c> — the obvious way to write "the dev environment" — means group exactly
    /// <c>env</c>, title starting <c>dev</c>. It matches nothing under <c>env/dev/</c> and it does
    /// match an entry sitting directly in <c>env</c> that the author never pictured. This is not a
    /// bug to fix (D-0021 fixed the matching domain in 2.1, deliberately); it is a fact
    /// <c>keypaste policy ls</c> has to make visible, and this is what proves it is still a fact.
    /// </remarks>
    [Fact]
    public void APolicyRuleWithATrailingStar_ConstrainsTheTitleNotTheGroup()
    {
        var rule = Only(Valid.Replace("env/dev/**", "env/dev*", StringComparison.Ordinal));

        Assert.True(rule.Matches("claude-code", new EntryName("env", "devops_ROOT_TOKEN"), "password"));
        Assert.False(rule.Matches("claude-code", new EntryName("env/dev", "STRIPE_KEY"), "password"));

        var (group, title) = PolicyText.Halves("env/dev*");
        Assert.Equal("env", group);
        Assert.Equal("dev*", title);
    }

    [Fact]
    public void ADoubleStarTail_MatchesTheGroupItselfAndEverythingUnderIt()
    {
        var rule = Only(Valid);

        Assert.True(rule.Matches("claude-code", new EntryName("env/dev", "STRIPE_KEY"), "password"));
        Assert.True(rule.Matches("claude-code", new EntryName("env/dev/eu", "STRIPE_KEY"), "password"));
        Assert.False(rule.Matches("claude-code", new EntryName("env", "STRIPE_KEY"), "password"));
    }

    /// <summary>
    /// The claim D-0021 exists to make: the policy file does not define a second matching domain, it
    /// constructs an <see cref="EntryExposure"/>. Asserted by verdict rather than by inspection,
    /// because "it calls the same method" is a fact about today's code and "it decides the same way"
    /// is a fact about the product.
    /// </summary>
    [Theory]
    [InlineData("env/**")]
    [InlineData("env/dev/**")]
    [InlineData("env/dev*")]
    [InlineData("env/*/STRIPE_KEY")]
    [InlineData("**")]
    [InlineData("personal/bank")]
    public void ARuleUsesTheSameMatcherAsTheExposure(string glob)
    {
        var rule = Only(Valid.Replace("env/dev/**", glob, StringComparison.Ordinal));
        Assert.True(EntryExposure.TryCreate([glob], out var exposure, out _));

        foreach (var name in Names())
        {
            Assert.Equal(exposure.Allows(name), rule.Matches("claude-code", name, "password"));
        }
    }

    /// <summary>
    /// A title that looks like a path cannot satisfy a group pattern — the property
    /// <see cref="EntryExposure"/> was built around, re-asserted against a rule so that losing it
    /// here would be caught here.
    /// </summary>
    [Fact]
    public void ATitleFullOfSlashes_CannotSatisfyAGroupPattern()
    {
        var rule = Only(Valid.Replace("env/dev/**", "env/prod/**", StringComparison.Ordinal));

        Assert.False(rule.Matches("claude-code", new EntryName("env/dev", "../../prod/ROOT_TOKEN"), "password"));
    }

    [Fact]
    public void APolicyRule_MatchesTheRawNameOrdinally()
    {
        var rule = Only(Valid);

        Assert.False(rule.Matches("claude-code", new EntryName("ENV/DEV", "STRIPE_KEY"), "password"));
        Assert.False(rule.Matches("CLAUDE-CODE", new EntryName("env/dev", "STRIPE_KEY"), "password"));
        Assert.False(rule.Matches("claude-code", new EntryName("env/dev", "STRIPE_KEY"), "Password"));
    }

    [Fact]
    public void AFieldOutsideTheRule_DoesNotMatch()
    {
        var rule = Only(Valid);

        Assert.True(rule.Matches("claude-code", new EntryName("env/dev", "K"), "password"));
        Assert.False(rule.Matches("claude-code", new EntryName("env/dev", "K"), "username"));
    }

    /// <summary>
    /// <c>"*"</c> means "any client the operator gave a name to", not "any client at all". A bridge
    /// started without <c>--client-label</c> matches nothing, because a standing grant to something
    /// nobody named is not something to hand out because a key was left blank.
    /// </summary>
    [Fact]
    public void AWildcardClient_DoesNotMatchABridgeWithNoLabel()
    {
        var rule = Only(Valid.Replace("\"claude-code\"", "\"*\"", StringComparison.Ordinal));

        Assert.True(rule.Matches("anything-at-all", new EntryName("env/dev", "K"), "password"));
        Assert.False(rule.Matches(null, new EntryName("env/dev", "K"), "password"));
        Assert.False(rule.Matches(string.Empty, new EntryName("env/dev", "K"), "password"));
    }

    [Fact]
    public void ANamedClient_DoesNotMatchABridgeWithNoLabel()
    {
        var rule = Only(Valid);

        Assert.False(rule.Matches(null, new EntryName("env/dev", "K"), "password"));
        Assert.False(rule.Matches("other-client", new EntryName("env/dev", "K"), "password"));
    }

    [Fact]
    public void TheFirstMatchingRuleWins_AndTheRestAreNotConsulted()
    {
        const string Text = """
            [[allow]]
            client          = "claude-code"
            entries         = ["env/dev/**"]
            fields          = ["password"]
            max_ttl_seconds = 60

            [[allow]]
            client          = "*"
            entries         = ["env/**"]
            fields          = ["password"]
            max_ttl_seconds = 300
            """;

        var document = Document(Text);

        Assert.True(document.TryMatch("claude-code", new EntryName("env/dev", "K"), "password", out var first));
        Assert.Equal(1, first.Ordinal);

        Assert.True(document.TryMatch("claude-code", new EntryName("env/test", "K"), "password", out var second));
        Assert.Equal(2, second.Ordinal);
    }

    /// <summary>
    /// Nothing defaults. Each of these is a way for a rule to become silently wider than what was
    /// written, and a typo in a key name would otherwise select one of them.
    /// </summary>
    [Theory]
    [InlineData("client          = \"claude-code\"")]
    [InlineData("entries         = [\"env/dev/**\"]")]
    [InlineData("fields          = [\"password\"]")]
    [InlineData("max_ttl_seconds = 300")]
    public void ARuleMissingAnyRequiredKey_IsARefusal(string line)
    {
        Refuses(Valid.Replace(line, string.Empty, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("[[deny]]", "an unknown section")]
    [InlineData("[[allow]]\nclientt = \"x\"", "an unknown key")]
    public void AnUnknownSectionOrKey_IsARefusal(string text, string what)
    {
        Assert.True(Toml.TryParse(text, out var syntax, out _), what);
        Assert.False(PolicyDocument.TryCreate(syntax, out var document, out var error), what);
        Assert.Null(document);
        Assert.StartsWith("line ", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// One bad rule invalidates the file, including the good rules before it. This is the property
    /// the whole stage rests on: a policy that is partly wrong says nothing, so everything prompts.
    /// </summary>
    [Fact]
    public void AMalformedRule_IsIgnoredWhole_NotUpToTheBadLine()
    {
        var text = Valid + "\n\n" + Valid + "\n\n[[allow]]\nclient = \"x\"\n";

        Assert.True(Toml.TryParse(text, out var syntax, out _));
        Assert.Equal(3, syntax.Tables.Count);

        Assert.False(PolicyDocument.TryCreate(syntax, out var document, out _));
        Assert.Null(document);
    }

    [Theory]
    [InlineData("max_ttl_seconds = 300", "max_ttl_seconds = 0", "zero")]
    [InlineData("max_ttl_seconds = 300", "max_ttl_seconds = 3601", "over the requestable ceiling")]
    [InlineData("max_per_hour    = 20", "max_per_hour    = 0", "a zero allowance")]
    [InlineData("max_per_hour    = 20", "max_per_hour    = 1001", "an allowance over the cap")]
    [InlineData("fields          = [\"password\"]", "fields          = []", "no fields")]
    [InlineData("fields          = [\"password\"]", "fields          = [\"totp\"]", "a field keypaste does not release")]
    [InlineData("fields          = [\"password\"]", "fields          = [\"Password\"]", "a field in the wrong case")]
    [InlineData("entries         = [\"env/dev/**\"]", "entries         = []", "no patterns")]
    [InlineData("client          = \"claude-code\"", "client          = \"\"", "an empty client label")]
    [InlineData("max_ttl_seconds = 300", "max_ttl_seconds = \"300\"", "a number written as a string")]
    [InlineData("client          = \"claude-code\"", "client          = 1", "a string written as a number")]
    [InlineData("fields          = [\"password\"]", "fields          = \"password\"", "an array written as a string")]
    public void AValueTheRuleLayerCannotUse_IsARefusal(string from, string to, string what)
    {
        Refuses(Valid.Replace(from, to, StringComparison.Ordinal), what);
    }

    /// <summary>
    /// <see cref="EntryExposure"/> rejects control characters and backslashes in a pattern, which is
    /// necessary and not sufficient: it accepts bidi overrides, zero-width characters and the
    /// Unicode tag block. A rule that renders as <c>env/dev/**</c> and means <c>env/**</c> would
    /// otherwise be writable, so anything the sanitizer would alter is refused outright.
    /// </summary>
    [Theory]
    [InlineData("env/‮dev/**", "a bidi override")]
    [InlineData("env/​dev/**", "a zero-width space")]
    [InlineData("env/\U000E0041dev/**", "a Unicode tag character")]
    [InlineData("env/­dev/**", "a soft hyphen")]
    public void APatternTheSanitizerWouldChange_IsRefused(string glob, string what)
    {
        Refuses(Valid.Replace("env/dev/**", glob, StringComparison.Ordinal), what);
    }

    [Theory]
    [InlineData("claude‮code", "a bidi override")]
    [InlineData("claude​code", "a zero-width space")]
    public void AClientLabelTheSanitizerWouldChange_IsRefused(string client, string what)
    {
        Refuses(Valid.Replace("claude-code", client, StringComparison.Ordinal), what);
    }

    [Fact]
    public void ACitationNamesTheRuleItsPatternsAndItsFields()
    {
        Assert.Equal("allow#1 (env/dev/**, password)", Only(Valid).Cite());
    }

    [Fact]
    public void ACitationIsCapped_SoItCannotCrowdAnAuditRecord()
    {
        var globs = Enumerable.Repeat("\"" + new string('a', 100) + "/**\"", 2);
        var text = Valid.Replace("[\"env/dev/**\"]", "[" + string.Join(", ", globs) + "]", StringComparison.Ordinal);

        var cite = Only(text).Cite();
        Assert.True(cite.Length > 100, "the citation under test must actually be long enough to need capping");
        Assert.Equal(PolicyRule.MaximumCitationLength, cite.Length);
    }

    [Fact]
    public void ARuleWithNoPatternsCannotBeBuiltAtAll_SoNoneAllowsNothing()
    {
        Assert.Empty(PolicyDocument.None.Rules);
        Assert.False(PolicyDocument.None.TryMatch("claude-code", new EntryName("env/dev", "K"), "password", out var rule));
        Assert.Null(rule);
    }

    [Fact]
    public void TryCreate_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => PolicyRule.TryCreate(null!, 1, out _, out _));
        Assert.Throws<ArgumentNullException>(() => PolicyDocument.TryCreate(null!, out _, out _));
    }

    [Fact]
    public void Matches_RejectsNull()
    {
        var rule = Only(Valid);

        Assert.Throws<ArgumentNullException>(() => rule.Matches("x", null!, "password"));
        Assert.Throws<ArgumentNullException>(() => rule.Matches("x", new EntryName("env", "K"), null!));
    }

    private static IEnumerable<EntryName> Names() =>
    [
        new EntryName("env", "STRIPE_KEY"),
        new EntryName("env", "devops_ROOT_TOKEN"),
        new EntryName("env", "dev"),
        new EntryName("env/dev", "STRIPE_KEY"),
        new EntryName("env/dev/eu", "STRIPE_KEY"),
        new EntryName("env/prod", "ROOT_TOKEN"),
        new EntryName("personal", "bank"),
        new EntryName(string.Empty, "loose"),
        new EntryName("env/dev", "../../prod/ROOT_TOKEN"),
    ];

    private static PolicyRule Only(string text) => Assert.Single(Document(text).Rules);

    private static PolicyDocument Document(string text)
    {
        Assert.True(Toml.TryParse(text, out var syntax, out var syntaxError), syntaxError);
        Assert.True(PolicyDocument.TryCreate(syntax, out var document, out var error), error);
        return document;
    }

    private static void Refuses(string text, string what = "")
    {
        Assert.True(Toml.TryParse(text, out var syntax, out var syntaxError), syntaxError);
        Assert.False(PolicyDocument.TryCreate(syntax, out var document, out var error), what);
        Assert.Null(document);
        Assert.StartsWith("line ", error, StringComparison.Ordinal);
    }
}
