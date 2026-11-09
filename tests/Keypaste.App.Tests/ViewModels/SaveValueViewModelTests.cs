using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core.Approval;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>A save prompt saves nothing until a value is entered, it has armed, and a replace is confirmed; Cancel saves nothing (F.79).</summary>
public sealed class SaveValueViewModelTests
{
    private readonly FakeClipboard _clipboard = new();
    private readonly ManualClock _clock = new(AppClock.Start);

    private static SavePrompt Prompt(bool replaces) =>
        SavePrompt.For("Claude Code", "claude-code", "work/stripe", "STRIPE_KEY", "the new key") with { Replaces = replaces };

    [Fact]
    public async Task Save_needs_a_value_and_the_arming_and_gives_back_what_was_entered()
    {
        using var prompt = new SaveValueViewModel(Prompt(replaces: false), _clipboard, _clock);

        prompt.Arm();
        Assert.False(prompt.AllowOnceCommand.CanExecute(null));

        foreach (var key in "sk_new")
        {
            prompt.Value.Type(key);
        }

        Assert.True(prompt.AllowOnceCommand.CanExecute(null));
        prompt.AllowOnceCommand.Execute(null);

        Assert.Equal(ApprovalAnswer.ApprovedOnce, await prompt.Answer);
        Assert.Equal("sk_new", prompt.Composed());
    }

    [Fact]
    public void A_replace_saves_only_once_the_person_confirms_it()
    {
        using var prompt = new SaveValueViewModel(Prompt(replaces: true), _clipboard, _clock);
        prompt.Arm();
        prompt.GenerateCommand.Execute(null);

        Assert.True(prompt.Value.HasValue);
        Assert.False(prompt.AllowOnceCommand.CanExecute(null));
        Assert.StartsWith("Replaces STRIPE_KEY at work/stripe.", prompt.Placement, StringComparison.Ordinal);

        prompt.ConfirmsReplace = true;

        Assert.True(prompt.AllowOnceCommand.CanExecute(null));
    }

    [Fact]
    public async Task Cancel_saves_nothing()
    {
        using var prompt = new SaveValueViewModel(Prompt(replaces: false), _clipboard, _clock);
        prompt.Value.Type('x');

        prompt.DenyCommand.Execute(null);

        Assert.Equal(ApprovalAnswer.Denied, await prompt.Answer);
    }
}
