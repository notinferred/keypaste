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
    /// <c>OnceOnly</c> is an enum the approver sets from the entry's profile and the agent's settings,
    /// never from the request's text: it says why no timed grant is offered, and nothing else.
    /// <c>AlwaysFor</c> is the project environment the approver reads from the entry's own tag for a
    /// named agent, never from the request's text: it adds one choice that still asks for its own
    /// confirmation, and leaves the default a denial and the deadline where it was.
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
                "AlwaysFor",
                "Client",
                "Entry",
                "EntryWasAltered",
                "Field",
                "Label",
                "OnceOnly",
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
            // The path separator is the one exemption, and only in prose: an agent naming
            // env/demo/STRIPE_KEY in its sentence is the ordinary case, and flattening it fired the
            // altered-warning on every request. The other nine stay out of a reason entirely.
            Assert.False(
                rune.Value != '/'
                && EntryNameSanitizerTests.Structural.Contains(rune.ToString(), StringComparison.Ordinal),
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

    /// <summary>
    /// The label is written by whoever starts the bridge, which is not proof of anything either, so
    /// it is sanitized and capped like the name; a bridge with none has none rather than a stand-in.
    /// </summary>
    [Fact]
    public void TheClientLabel_IsSanitizedAndCapped_AndAbsentWhenNoneWasGiven()
    {
        var label = ApprovalPrompt.For(
            "claude-code", new EntryName("env/dev", "STRIPE_KEY"), "password", "deploy", 300, new string('l', 300) + "‮");

        Assert.Equal(ApprovalPrompt.MaximumClientLength, label.Label!.Length);
        Assert.Null(For().Label);
        Assert.Null(ApprovalPrompt.For("c", new EntryName("", "t"), "password", "r", 1, "").Label);
    }

    [Fact]
    public void AClientThatGaveNoName_IsSaidToBeUnnamed_NotCalledUnknown()
    {
        Assert.Equal("an unnamed agent", For(client: null).Client, StringComparer.Ordinal);
        Assert.Equal("an unnamed agent", For(client: "").Client, StringComparer.Ordinal);
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
    /// An Automatic release lasts what the agent asked for, never past the operator's ceiling.
    /// A person's timed grant is not bounded this way: they chose its length on screen.
    /// </summary>
    [Fact]
    public void AnAutomaticReleaseIsBoundedByTheRequestAndTheCeiling()
    {
        var limits = ApprovalLimits.Default with { MaximumTtlSeconds = 300 };

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

    /// <summary>A change asked for with no reason, as Always for and the app's own settings ask, shows none rather than a placeholder.</summary>
    [Fact]
    public void AChangeWithNoReason_ShowsNone_AndOneScrubbedAwayIsSaidToBe()
    {
        var change = Agents.AccessChange.Classify(Agents.EffectiveAccess.Defaults, Agents.AgentAccess.Defaults with { Run = true });

        var none = AccessChangePrompt.For("claude-code", change, AccessOrigin.Prompt, string.Empty, TimeZoneInfo.Utc);
        var scrubbed = AccessChangePrompt.For("claude-code", change, AccessOrigin.Agent, "\u202e\u200b", TimeZoneInfo.Utc);

        Assert.Equal((string.Empty, false, false), (none.Reason, none.ReasonWasAltered, none.ReasonWasTruncated));
        Assert.True(scrubbed.ReasonWasAltered);
        Assert.NotEmpty(scrubbed.Reason);
    }

    /// <summary>
    /// The selectors an agent asks to see are shown in the confirmation with each character that could
    /// hide or reorder text as an escape, so a pattern cannot pass for another or push what follows out
    /// of view.
    /// </summary>
    [Fact]
    public void AChangesGains_ShowEveryCharacterOfWhatItWouldSee()
    {
        var hidden = "x" + new string('\u2028', 20) + "**";
        Assert.True(EntryExposure.TryCreate(["tag:env:billing", hidden, "\u202Eprod/**"], out var see, out var error), error);
        var change = Agents.AccessChange.Classify(Agents.EffectiveAccess.Defaults, Agents.AgentAccess.Defaults with { See = see });

        var gain = Assert.Single(AccessChangePrompt.For("claude-code", change, AccessOrigin.Agent, "to deploy", TimeZoneInfo.Utc).Gains);

        Assert.Equal($"sees tag:env:billing, x{string.Concat(Enumerable.Repeat("\\u2028", 20))}**, \\u202Eprod/**", gain);
        Assert.DoesNotContain('\u2028', gain);
        Assert.DoesNotContain('\u202E', gain);
    }

    [Fact]
    public void ForRejectsNulls()
    {
        Assert.Throws<ArgumentNullException>(() => ApprovalPrompt.For("c", null!, "password", "r", 60));
        Assert.Throws<ArgumentNullException>(() => ApprovalPrompt.For("c", new EntryName("a", "b"), null!, "r", 60));
        Assert.Throws<ArgumentNullException>(() => ApprovalPrompt.For("c", new EntryName("a", "b"), "password", null!, 60));
    }
}
