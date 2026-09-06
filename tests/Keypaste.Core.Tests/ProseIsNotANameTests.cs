using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// An agent's reason is prose a person reads, not a name that could impersonate a path.
/// </summary>
/// <remarks>
/// <para>
/// Found by running the thing rather than by reading it. Real Claude Code, asked for a credential,
/// sent the reason <c>"Claude Code needs env/demo/STRIPE_KEY to deploy the billing service to
/// staging, as you requested."</c> — and the approval dialog drew <c>"env demo STRIPE_KEY"</c> above
/// the line <c>"The reason above is not what the agent sent: it was scrubbed."</c>
/// </para>
/// <para>
/// Two defects in one. The reason was mangled, and the warning that exists to catch a hostile reason
/// fired on the most ordinary request there is. A warning that fires every time is not a warning: it
/// trains the person approving to skip the line that is supposed to stop them, which costs more than
/// the scrubbing ever saved.
/// </para>
/// <para>
/// The hostile payloads below are built from code points rather than typed, for the reason
/// <c>HostileNameRenderingTests</c> gives: a literal bidi override or control character in a source
/// file is invisible to review and travels badly through tooling.
/// </para>
/// </remarks>
public sealed class ProseIsNotANameTests
{
    /// <summary>The reason real Claude Code sent, kept verbatim.</summary>
    private const string _asClaudeWroteIt =
        "Claude Code needs env/demo/STRIPE_KEY to deploy the billing service to staging, as you requested.";

    private static readonly string _bidiOverride = ((char)0x202E).ToString();
    private static readonly string _zeroWidth = ((char)0x200B).ToString();
    private static readonly string _bell = ((char)0x0007).ToString();
    private static readonly string _lineSeparator = ((char)0x2028).ToString();

    [Fact]
    public void An_ordinary_reason_survives_its_slashes_and_is_not_marked_altered()
    {
        var shown = EntryNameSanitizer.SanitizeProse(_asClaudeWroteIt);

        Assert.Equal(_asClaudeWroteIt, shown.Text, StringComparer.Ordinal);
        Assert.False(shown.WasAltered, "an ordinary reason must not trip the altered warning");
    }

    /// <summary>
    /// The reason <see cref="EntryNameSanitizer.SanitizeProse"/> exists at all: everything that can
    /// misrepresent what is drawn on the screen still goes, and still says it went.
    /// </summary>
    [Fact]
    public void Anything_that_can_misrepresent_the_screen_is_still_scrubbed()
    {
        foreach (var payload in new[] { _bidiOverride, _zeroWidth, _bell, _lineSeparator })
        {
            var shown = EntryNameSanitizer.SanitizeProse("a" + payload + "b");

            Assert.Equal("a b", shown.Text, StringComparer.Ordinal);
            Assert.True(shown.WasAltered, "a scrubbed reason must still say so");
        }
    }

    /// <summary>
    /// Only the separator is exempt. A reason still carries no markup, no fence and no pipe, which
    /// is what <c>ApprovalPromptTests.AHostileReason_IsRenderedInert</c> holds.
    /// </summary>
    [Fact]
    public void Prose_keeps_the_separator_and_nothing_else_structural()
    {
        var shown = EntryNameSanitizer.SanitizeProse("a/b <c> `d` |e| [f] {g}");

        Assert.Contains('/', shown.Text);
        foreach (var c in "<>`|[]{}" + @"\")
        {
            Assert.DoesNotContain(c, shown.Text);
        }
    }

    /// <summary>
    /// A name keeps the old rule. A title carrying a slash can impersonate a group path, and that is
    /// the argument <see cref="EntryNameSanitizer.SanitizeProse"/> deliberately does not inherit.
    /// </summary>
    [Fact]
    public void A_name_still_loses_its_structural_characters()
    {
        var asName = EntryNameSanitizer.Sanitize("env/demo/STRIPE_KEY");

        Assert.Equal("env demo STRIPE_KEY", asName.Text, StringComparer.Ordinal);
        Assert.True(asName.WasAltered);
    }
}
