using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>A picker allows nothing until an item is picked, however long it has been up, and offers Always allow only for a picked item that has one (F.76).</summary>
public sealed class ItemPickViewModelTests
{
    private static ApprovalPrompt Picker() =>
        ApprovalPrompt.For("claude-code", new EntryName("services", "Mail"), "password", "send the report", "mail-bot") with
        {
            Candidates =
            [
                Candidate("alice", new AlwaysOffer(AccessEdit.AlwaysItem("0123456789ABCDEF0123456789ABCDEF"), "services/Mail"), OnceOnly.None),
                Candidate("bob", null, OnceOnly.ProtectedProfile),
            ],
        };

    private static ItemCandidate Candidate(string username, AlwaysOffer? always, OnceOnly onceOnly) => new()
    {
        Name = new EntryName("services", "Mail"),
        Item = username == "alice" ? "0123456789ABCDEF0123456789ABCDEF" : "FEDCBA9876543210FEDCBA9876543210",
        Title = "Mail",
        Group = "services",
        Username = username,
        Url = string.Empty,
        Always = always,
        OnceOnly = onceOnly,
    };

    [Fact]
    public void Nothing_is_picked_and_an_armed_picker_allows_nothing_until_an_item_is()
    {
        var picker = new ItemPickViewModel(Picker());
        picker.Arm();

        Assert.Null(picker.Picked);
        Assert.Null(picker.PickedIndex);
        Assert.False(picker.AllowOnceCommand.CanExecute(null));
        Assert.False(picker.AlwaysAllowCommand.CanExecute(null));
        Assert.True(picker.OffersAlways);
        Assert.Equal("Pick an item to allow it. Nothing is picked for you.", picker.AlwaysCaption);
        Assert.Equal(["services · alice", "services · bob"], picker.Rows.Select(row => row.Details));

        picker.Picked = picker.Rows[0];

        Assert.True(picker.AllowOnceCommand.CanExecute(null));
        Assert.True(picker.AlwaysAllowCommand.CanExecute(null));
        Assert.Contains("covers the item you picked", picker.AlwaysCaption, StringComparison.Ordinal);

        picker.Picked = picker.Rows[1];

        Assert.True(picker.AllowOnceCommand.CanExecute(null));
        Assert.False(picker.AlwaysAllowCommand.CanExecute(null));
        Assert.Equal("once only: protected environment", picker.Lifetime);
    }

    [Fact]
    public async Task The_pick_made_as_the_person_answered_is_the_one_kept()
    {
        var picker = new ItemPickViewModel(Picker());
        picker.Arm();
        picker.Picked = picker.Rows[1];

        picker.AllowOnceCommand.Execute(null);
        picker.Picked = picker.Rows[0];

        Assert.Equal(ApprovalAnswer.ApprovedOnce, await picker.Answer);
        Assert.Equal(1, picker.PickedIndex);
    }
}
