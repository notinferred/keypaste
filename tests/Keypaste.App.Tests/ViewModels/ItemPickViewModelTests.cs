using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>A picker allows nothing until an item is picked, however long it has been up, and offers Always allow only for a picked item that has one.</summary>
public sealed class ItemPickViewModelTests
{
    private static ApprovalPrompt Picker() =>
        ApprovalPrompt.For("claude-code", new EntryName("services", "Mail"), "password", "send the report", "mail-bot") with
        {
            Candidates = [Candidate("alice"), Candidate("bob")],
        };

    private static ItemCandidate Candidate(string username) => new()
    {
        Name = new EntryName("services", "Mail"),
        Item = username == "alice" ? "0123456789ABCDEF0123456789ABCDEF" : "FEDCBA9876543210FEDCBA9876543210",
        Title = "Mail",
        Group = "services",
        Username = username,
        Url = string.Empty,
    };

    [Fact]
    public void Nothing_is_picked_and_an_armed_picker_allows_nothing_until_an_item_is()
    {
        var picker = new ItemPickViewModel(Picker());
        picker.Arm();
        picker.IsInFront = true;

        Assert.Null(picker.Picked);
        Assert.Null(picker.PickedIndex);
        Assert.False(picker.AllowOnceCommand.CanExecute(null));
        Assert.False(picker.AlwaysAllowCommand.CanExecute(null));
        Assert.Equal(["services · alice", "services · bob"], picker.Rows.Select(row => row.Details));
        Assert.Equal(("claude-code", "Mail", "password"), (picker.RequesterAgent, picker.Item, picker.Field));

        picker.Return();
        Assert.False(picker.IsAnswered);

        picker.Picked = picker.Rows[0];

        Assert.True(picker.AllowOnceCommand.CanExecute(null));
        Assert.Equal(picker.OffersAlways, picker.AlwaysAllowCommand.CanExecute(null));
    }

    [Fact]
    public void A_protected_item_is_marked_and_says_so_once_picked()
    {
        var picker = new ItemPickViewModel(Picker() with { Candidates = [Candidate("alice") with { Protected = true }, Candidate("bob")] });

        Assert.Equal([true, false], picker.Rows.Select(row => row.Protected));
        Assert.EndsWith(", protected", picker.Rows[0].Spoken, StringComparison.Ordinal);
        Assert.True(picker.AnyProtected);
        Assert.False(picker.PickedIsProtected);

        picker.Picked = picker.Rows[0];
        Assert.True(picker.PickedIsProtected);

        picker.Picked = picker.Rows[1];
        Assert.False(picker.PickedIsProtected);
    }

    [Fact]
    public void A_standard_field_is_asked_for_in_words_and_a_key_by_its_name()
    {
        var words = new ItemPickViewModel(Picker());
        var key = new ItemPickViewModel(ApprovalPrompt.For("claude-code", new EntryName("services", "Mail"), "SMTP_TOKEN", "send", "mail-bot") with
        {
            Candidates = [Candidate("alice"), Candidate("bob")],
        });

        Assert.Equal((" the password for Mail", string.Empty), (words.Asked, words.Key));
        Assert.Equal((" ", "SMTP_TOKEN"), (key.Asked, key.Key));
    }

    [Fact]
    public async Task The_pick_made_as_the_person_answered_is_the_one_kept()
    {
        var picker = new ItemPickViewModel(Picker());
        picker.Arm();
        picker.IsInFront = true;
        picker.Picked = picker.Rows[1];

        picker.AllowOnceCommand.Press();
        picker.Picked = picker.Rows[0];

        Assert.Equal(ApprovalAnswer.ApprovedOnce, await picker.Answer);
        Assert.Equal(1, picker.PickedIndex);
    }
}
