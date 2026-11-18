using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>An agent's change to an item is allowed only once the prompt has armed, never always, and Deny changes nothing (T-57).</summary>
public sealed class ItemChangeViewModelTests
{
    private static ItemChangePrompt Prompt(ItemChangeKind kind) =>
        ItemChangePrompt.For("Claude Code", "claude-code", new EntryName("work", "github"), new ItemChange(kind, null), "the key was revoked");

    [Fact]
    public async Task Allow_needs_the_arming_and_offers_no_always()
    {
        var prompt = new ItemChangeViewModel(Prompt(ItemChangeKind.MoveToTrash));

        Assert.False(prompt.AllowOnceCommand.CanExecute(null));
        Assert.False(prompt.OffersAlways);
        Assert.Equal(("Move work/github to Trash", "Allow"), (prompt.Change, prompt.AllowLabel));
        Assert.Equal(("Let Claude Code move github to Trash?", "github"), (prompt.Question, prompt.Item));

        prompt.Arm();
        prompt.AllowOnceCommand.Execute(null);

        Assert.Equal(ApprovalAnswer.ApprovedOnce, await prompt.Answer);
    }

    [Fact]
    public async Task A_delete_for_good_says_so_and_Deny_changes_nothing()
    {
        var prompt = new ItemChangeViewModel(Prompt(ItemChangeKind.Delete) with { Created = new DateOnly(2026, 11, 15) });

        Assert.True(prompt.DeletesForGood);
        Assert.Equal("Delete for good", prompt.AllowLabel);
        Assert.Equal("Delete work/github for good. An agent created it on 2026-11-15", prompt.Change);
        Assert.Equal("Let Claude Code delete github for good?", prompt.Question);
        Assert.Equal("An agent created it on 2026-11-15. Trash will not hold it, and nothing can bring it back.", prompt.Explainer);
        Assert.False(prompt.AnswersReturn);

        prompt.DenyCommand.Execute(null);

        Assert.Equal(ApprovalAnswer.Denied, await prompt.Answer);
    }
}
