using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>A prompt's time bar is full and named with the whole time from its first tick, drains to empty, and shows the second a key waits to press an allow only while that allow waits for nothing else.</summary>
public sealed class PromptTimeBarTests
{
    private static ApprovalPrompt Request() =>
        ApprovalPrompt.For("claude-code", new EntryName("services", "Mail"), "password", "send the report", "mail-bot");

    [Fact]
    public void The_bar_starts_full_named_with_the_whole_time_and_drains_to_empty()
    {
        var prompt = new ApprovalViewModel(Request());

        prompt.Tick(TimeSpan.FromSeconds(45));
        Assert.Equal((1d, "Denies in 0:45"), (prompt.TimeLeft, prompt.DeniesIn));

        prompt.Tick(TimeSpan.FromSeconds(22.5));
        Assert.Equal((0.5, "Denies in 0:23"), (prompt.TimeLeft, prompt.DeniesIn));

        prompt.Tick(TimeSpan.FromSeconds(-1));
        Assert.Equal((0d, "Denies in 0:00"), (prompt.TimeLeft, prompt.DeniesIn));
    }

    [Fact]
    public void Arming_shows_until_the_prompt_arms_and_again_when_it_is_disarmed()
    {
        var prompt = new ApprovalViewModel(Request());

        Assert.True(prompt.IsArming);

        prompt.Arm();
        Assert.False(prompt.IsArming);

        prompt.Disarm();
        Assert.True(prompt.IsArming);
    }

    [Fact]
    public void A_picker_with_nothing_picked_is_not_arming_but_waiting_for_a_pick()
    {
        var name = new EntryName("services", "Mail");
        ItemCandidate Candidate(string item) => new() { Name = name, Item = item, Title = "Mail", Group = "services", Username = item, Url = string.Empty };
        var picker = new ItemPickViewModel(Request() with { Candidates = [Candidate("A"), Candidate("B")] });

        Assert.False(picker.IsArming);

        picker.Picked = picker.Rows[0];
        Assert.True(picker.IsArming);
    }
}
