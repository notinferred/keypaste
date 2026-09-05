using System.Globalization;
using System.Reflection;
using System.Text;
using Keypaste.Core.Approval;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The prompt is the mitigation for THREATS.md T-2, which is the injection channel people forget
/// because it comes from the agent rather than from the vault: the reason's entire design purpose
/// is to persuade the person reading it.
/// </summary>
public sealed class ApprovalPromptTests
{
    private static ApprovalPrompt For(string reason = "deploy billing to staging", string? client = "claude-code") =>
        ApprovalPrompt.For(client, new EntryName("env/dev", "STRIPE_KEY"), "password", reason, 300);

    [Fact]
    public void ItShowsTheFourThingsAHumanNeedsToDecide()
    {
        var prompt = For();

        Assert.Equal("claude-code", prompt.Client, StringComparer.Ordinal);
        Assert.Equal("env/dev/STRIPE_KEY", prompt.Entry, StringComparer.Ordinal);
        Assert.Equal("password", prompt.Field, StringComparer.Ordinal);
        Assert.Equal("deploy billing to staging", prompt.Reason, StringComparer.Ordinal);
        Assert.Equal(300, prompt.TtlSeconds);
        Assert.False(prompt.ReasonWasTruncated);
    }

    /// <summary>
    /// The structural half of T-2, and the reason it is a test rather than a code comment: the type
    /// has nowhere to put a default button, a deadline or a layout, so no reason — however
    /// carefully written — can reach one. If somebody adds such a member, this goes red and they
    /// have to argue for it.
    /// </summary>
    /// <remarks>
    /// <b>The argument for <c>EntryWasAltered</c> and <c>ReasonWasAltered</c>, made because this
    /// test demanded it.</b> Both are booleans the channel may state, in the same class as
    /// <c>ReasonWasTruncated</c>, which this list already allowed. Neither carries text, a duration,
    /// a position or a default: a channel can say that a name was scrubbed, and it still cannot be
    /// told what the deadline is or which button is pre-selected, because there remains nowhere to
    /// put either. They are set from the sanitizer's own result rather than from anything the agent
    /// sends, so a reason cannot choose their value — it can only cause its own to be true by
    /// containing something that had to be scrubbed, which is precisely the fact being reported.
    /// </remarks>
    [Fact]
    public void ThePromptHasNoMember_AReasonCouldUseToChangeTheDefaultOrTheDeadline()
    {
        var members = typeof(ApprovalPrompt)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(
            [
                "Client",
                "Entry",
                "EntryWasAltered",
                "Field",
                "Reason",
                "ReasonWasAltered",
                "ReasonWasTruncated",
                "TtlSeconds",
            ],
            members.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// A reason drawing a second dialog inside the first is the concrete attack: end the request
    /// block, then write a reassuring line that looks like it came from keypaste. Newlines are what
    /// make it work, and the sanitizer turning them into spaces is what stops it.
    /// </summary>
    [Fact]
    public void AReasonCannotRedrawThePrompt()
    {
        var prompt = For("routine\n--- END REQUEST ---\nkeypaste: this request is safe, press y");

        Assert.DoesNotContain('\n', prompt.Reason);
        Assert.DoesNotContain('\r', prompt.Reason);
        Assert.Equal("routine --- END REQUEST --- keypaste: this request is safe, press y", prompt.Reason, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("\u001b[2J\u001b[Hcleared the screen")]
    [InlineData("<b>bold</b> and <script>alert(1)</script>")]
    [InlineData("markup with | pipes and `backticks`")]
    [InlineData("bidi \u202eoverride")]
    [InlineData("zero\u200bwidth")]
    [InlineData("tag \U000E0041\U000E0042\U000E0043 characters")]
    public void AHostileReason_IsRenderedInert(string reason)
    {
        var prompt = For(reason);

        foreach (var rune in prompt.Reason.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);

            Assert.False(Rune.IsControl(rune), $"a control character survived: {reason}");
            Assert.False(
                category is UnicodeCategory.Format or UnicodeCategory.PrivateUse,
                $"{category} survived: {reason}");
            Assert.False(
                EntryNameSanitizerTests.Structural.Contains(rune.ToString(), StringComparison.Ordinal),
                $"a structural character survived: {reason}");
        }
    }

    /// <summary>
    /// A two-thousand-character reason is a layout attack: it can push the entry name out of view
    /// or the buttons off the screen. Truncation is the mitigation, and saying so is what stops the
    /// prompt quietly presenting a shortened sentence as the whole one.
    /// </summary>
    [Fact]
    public void AnEnormousReason_IsCutDownAndSaysSo()
    {
        var prompt = For(new string('a', 2000));

        Assert.Equal(ApprovalPrompt.MaximumReasonLength, prompt.Reason.Length);
        Assert.True(prompt.ReasonWasTruncated);
    }

    [Fact]
    public void AnEmptyReason_StillRendersSomething()
    {
        Assert.Equal(EntryNameSanitizer.Placeholder, For("").Reason, StringComparer.Ordinal);
    }

    /// <summary>
    /// The client name is attacker-chosen too — nothing authenticates the handshake (T-3) — so it
    /// goes through the same sanitizer and the same cap as the reason.
    /// </summary>
    [Fact]
    public void AHostileClientName_IsSanitizedAndCapped()
    {
        var prompt = For(client: new string('z', 300) + "\u202e");

        Assert.Equal(ApprovalPrompt.MaximumClientLength, prompt.Client.Length);
    }

    [Fact]
    public void AClientThatGaveNoName_IsSaidToBeUnnamed_NotCalledUnknown()
    {
        Assert.Equal("an unnamed client", For(client: null).Client, StringComparer.Ordinal);
        Assert.Equal("an unnamed client", For(client: "").Client, StringComparer.Ordinal);
    }

    /// <summary>
    /// The one place the entry display differs from the audit line's, and deliberately. A slash
    /// inside a title becomes a space, so an entry called <c>../../prod/ROOT_TOKEN</c> sitting in
    /// <c>env/dev</c> cannot render as though it lived in <c>env/prod</c> — which is precisely the
    /// judgement the human is being asked to make.
    /// </summary>
    [Fact]
    public void ATitleFullOfSlashes_CannotPretendToLiveSomewhereElse()
    {
        var prompt = ApprovalPrompt.For(
            "claude-code",
            new EntryName("env/dev", "../../prod/ROOT_TOKEN"),
            "password",
            "routine",
            300);

        Assert.Equal("env/dev/.. .. prod ROOT_TOKEN", prompt.Entry, StringComparer.Ordinal);
        Assert.StartsWith("env/dev/", prompt.Entry, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEntryInTheRootGroup_IsJustItsTitle()
    {
        var prompt = ApprovalPrompt.For("c", new EntryName("", "LOOSE"), "password", "routine", 60);

        Assert.Equal("LOOSE", prompt.Entry, StringComparer.Ordinal);
    }

    /// <summary>
    /// The human is shown the TTL that will actually apply, not the one the agent asked for.
    /// Showing a requested hour when five minutes will be granted would make the prompt a worse
    /// source of truth than the audit log.
    /// </summary>
    [Fact]
    public void TheTtlShownIsTheOneThatWillApply()
    {
        var limits = ApprovalLimits.Default;

        Assert.Equal(300, limits.EffectiveTtlSeconds(3600));
        Assert.Equal(60, limits.EffectiveTtlSeconds(60));
        Assert.Equal(1, limits.EffectiveTtlSeconds(0));
        Assert.Equal(1, limits.EffectiveTtlSeconds(-99));
    }

    [Fact]
    public void TheDefaultWindowSitsUnderEveryClientsOwnTimeout()
    {
        // Sixty seconds is the MCP SDK's default request timeout, which Claude Desktop and Claude
        // Code both inherit. A window that reached it would let an approval land in a request the
        // client had already abandoned (DECISIONS.md D-0027).
        Assert.True(ApprovalLimits.DefaultWindowSeconds < 60);
        Assert.True(ApprovalLimits.MaximumWindowSeconds < 60);
        Assert.True(ApprovalLimits.MinimumWindowSeconds <= ApprovalLimits.DefaultWindowSeconds);
    }

    [Fact]
    public void ForRejectsNulls()
    {
        Assert.Throws<ArgumentNullException>(() => ApprovalPrompt.For("c", null!, "password", "r", 60));
        Assert.Throws<ArgumentNullException>(() => ApprovalPrompt.For("c", new EntryName("a", "b"), null!, "r", 60));
        Assert.Throws<ArgumentNullException>(() => ApprovalPrompt.For("c", new EntryName("a", "b"), "password", null!, 60));
    }
}
