using System.Reflection;
using Keypaste.Core.Approval;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// The owner's launch passes: each is the app's Run confirmation for one request, spent the first time
/// anything presents it, gone after two minutes or a lock, and holding no value (D-0452).
/// </summary>
public sealed class LaunchPassesTests
{
    private const string _session = "session-one";

    private static readonly LaunchClaim _claim = new(
        "billing",
        "dev",
        "/work/billing",
        ["/bin/sh", "-c", "eval \"$1\"; exec \"${SHELL:-/bin/sh}\"", "keypaste", "npm start"],
        new EnvPreview("billing", ["DATABASE_URL"])
        {
            Sources = [new EnvSource("DATABASE_URL", new EntryName("env/billing", ".env"), "DATABASE_URL")],
        });

    [Fact]
    public void A_pass_is_taken_once_and_names_its_claim()
    {
        using var passes = new LaunchPasses(new ManualClock());
        var pass = passes.Issue(_claim, _session);

        Assert.NotNull(pass);
        Assert.True(LaunchPasses.IsWellFormed(pass));
        Assert.True(passes.TryTake(pass, _session, out var claim));
        Assert.Same(_claim, claim);
        Assert.False(passes.TryTake(pass, _session, out claim));
        Assert.Null(claim);
    }

    [Fact]
    public void Each_confirmation_gets_its_own_pass()
    {
        using var passes = new LaunchPasses(new ManualClock());
        var first = passes.Issue(_claim, _session);
        var second = passes.Issue(_claim, _session);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
        Assert.True(passes.TryTake(second, _session, out _));
        Assert.True(passes.TryTake(first, _session, out _));
    }

    [Theory]
    [InlineData("both")]
    [InlineData("wall")]
    [InlineData("monotonic")]
    public void Two_minutes_on_either_clock_end_it(string which)
    {
        var clock = new ManualClock();
        using var passes = new LaunchPasses(clock);
        var pass = passes.Issue(_claim, _session);
        Assert.NotNull(pass);

        switch (which)
        {
            case "both":
                clock.Advance(LaunchPasses.Lifetime);
                break;
            case "wall":
                clock.AdvanceWallOnly(LaunchPasses.Lifetime);
                break;
            default:
                clock.AdvanceMonotonicOnly(LaunchPasses.Lifetime);
                break;
        }

        Assert.False(passes.TryTake(pass, _session, out _));
    }

    [Fact]
    public void Just_before_its_end_it_still_answers()
    {
        var clock = new ManualClock();
        using var passes = new LaunchPasses(clock);
        var pass = passes.Issue(_claim, _session);
        Assert.NotNull(pass);

        clock.Advance(LaunchPasses.Lifetime - TimeSpan.FromSeconds(1));

        Assert.True(passes.TryTake(pass, _session, out _));
    }

    /// <summary>Spent by any presentation, so a pass presented under the wrong session is not there to try again.</summary>
    [Fact]
    public void A_presentation_under_another_session_spends_it()
    {
        using var passes = new LaunchPasses(new ManualClock());
        var pass = passes.Issue(_claim, _session);
        Assert.NotNull(pass);

        Assert.False(passes.TryTake(pass, "session-two", out _));
        Assert.False(passes.TryTake(pass, _session, out _));
    }

    [Fact]
    public void A_withdrawn_or_never_issued_pass_is_refused()
    {
        using var passes = new LaunchPasses(new ManualClock());
        var withdrawn = passes.Issue(_claim, _session);
        var kept = passes.Issue(_claim, _session);
        Assert.NotNull(withdrawn);
        Assert.NotNull(kept);

        passes.Withdraw(withdrawn);

        Assert.False(passes.TryTake(withdrawn, _session, out _));
        Assert.False(passes.TryTake(new string('A', LaunchPasses.Length), _session, out _));
        Assert.True(passes.TryTake(kept, _session, out _));
    }

    [Fact]
    public void A_lock_forgets_every_pass_and_issues_no_more()
    {
        using var lifetime = new SessionLifetime(_session);
        using var passes = new LaunchPasses(new ManualClock());
        lifetime.Own(passes);
        var pass = passes.Issue(_claim, _session);
        Assert.NotNull(pass);

        lifetime.End();

        Assert.False(passes.TryTake(pass, _session, out _));
        Assert.Null(passes.Issue(_claim, _session));
    }

    [Theory]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", true)]
    [InlineData("azAZ09-_azAZ09-_azAZ09-_azAZ09-_azAZ09-_az0", true)]
    [InlineData("", false)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+", false)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", false)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA ", false)]
    public void A_pass_is_43_base64url_characters(string text, bool wellFormed) =>
        Assert.Equal(wellFormed, LaunchPasses.IsWellFormed(text));

    /// <summary>
    /// The claim is what the card showed, names and where each comes from, so the store holds no value
    /// between the confirmation and the run however long it waits.
    /// </summary>
    [Fact]
    public void A_claim_has_no_member_a_value_could_travel_in()
    {
        static string[] Members(Type type) =>
            [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name).Order(StringComparer.Ordinal)];

        Assert.Equal(["Command", "Directory", "Preview", "Profile", "Project"], Members(typeof(LaunchClaim)));
        Assert.Equal(["Keys", "Profile", "Project", "RequiresLiveApproval", "Sources"], Members(typeof(EnvPreview)));
        Assert.Equal(["Entry", "Field", "Key"], Members(typeof(EnvSource)));
    }
}
