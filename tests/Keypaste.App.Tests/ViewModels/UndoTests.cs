using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>Edit › Undo: each change it reverses returns the vault, as the file holds it, to its state before.</summary>
public sealed class UndoTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private readonly string _directory;
    private readonly string _vaultPath;
    private readonly AppVaultSession _session = new(new ManualClock(AppClock.Start));
    private readonly ClipboardCountdown _countdown;
    private readonly EntriesViewModel _entries;

    public UndoTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-undo-").FullName;
        _vaultPath = Path.Combine(_directory, "vault.kdbx");

        using (var vault = Vault.Create(_vaultPath, _master))
        {
            vault.AddEntry(new VaultEntry { Title = "production", GroupPath = "servers/live", Username = "root", Password = "p1" });
            vault.AddEntry(new VaultEntry { Title = "staging", GroupPath = "servers", Password = "p2" });
            vault.AddEntry(new VaultEntry { Title = "spare", GroupPath = "other", Password = "p3" });
            Assert.Equal(GroupOutcome.Created, vault.CreateGroup("servers", "empty", out _));
            vault.Save();
        }

        using (var master = TempVault.Secret(_master))
        {
            Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_vaultPath, master.Value));
        }

        _countdown = new ClipboardCountdown(new FakeClipboard(), new ManualClock(AppClock.Start));
        _entries = new EntriesViewModel(_session, _countdown);
    }

    public void Dispose()
    {
        _entries.Dispose();
        _countdown.Dispose();
        _session.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    public static TheoryData<string> Changes =>
    [
        "delete item", "rename item", "move item", "rename and move item", "duplicate item",
        "create group", "rename group", "move group", "delete group",
    ];

    [Theory]
    [MemberData(nameof(Changes))]
    public void Undo_returns_the_vault_to_its_state_before(string change)
    {
        var before = VaultState();

        Make(change);
        Assert.Null(_entries.Error);
        Assert.NotEqual(before, VaultState());
        Assert.True(_entries.CanUndo);

        _entries.UndoCommand.Press();

        Assert.Null(_entries.Error);
        Assert.False(_entries.CanUndo);
        Assert.Equal(before, VaultState());
    }

    [Fact]
    public void Undoing_a_delete_shows_the_item_again_in_the_pane()
    {
        Make("delete item");
        Assert.Null(_entries.Detail);

        _entries.UndoCommand.Press();

        Assert.NotNull(_entries.Selected);
        Assert.NotNull(_entries.Detail);
    }

    [Theory]
    [InlineData("delete item", true)]
    [InlineData("delete group", true)]
    [InlineData("rename item", false)]
    [InlineData("duplicate item", false)]
    [InlineData("create group", false)]
    public void The_Restore_link_is_offered_for_a_delete_alone(string change, bool offered)
    {
        Make(change);

        Assert.Equal(offered, _entries.OffersRestore);
    }

    [Fact]
    public void A_delete_that_goes_to_the_bin_does_not_ask()
    {
        Select("servers/staging");
        _entries.DeleteCommand.Press();

        Assert.False(_entries.IsConfirmingDelete);
        Assert.DoesNotContain(_entries.Rows, row => row.Path == "servers/staging");

        Group("other");
        _entries.BeginDeleteGroupCommand.Press();

        Assert.False(_entries.IsConfirmingGroupDelete);
        Assert.DoesNotContain(_entries.Groups, group => group.Path == "other");
    }

    [Fact]
    public void Undoing_a_duplicate_or_a_new_group_deletes_it_as_any_delete_does()
    {
        Make("duplicate item");
        _entries.UndoCommand.Press();

        Assert.Single(_session.Unlocked!.ReadRecycled());
    }

    [Fact]
    public void A_later_write_from_anywhere_ends_the_undo_and_the_vault_keeps_it()
    {
        Make("rename item");
        Assert.True(_entries.CanUndo);

        var write = _session.Write(vault => vault.RemoveEntry(new EntryName("other", "spare"), out _), _ => true);
        Assert.Equal(WriteOutcome.Saved, write.Outcome);
        Assert.False(_entries.CanUndo);

        _entries.UndoCommand.Press();

        using var reopened = Vault.Open(_vaultPath, _master);
        Assert.NotNull(reopened.Find("servers/renamed"));
    }

    [Fact]
    public void A_group_comes_back_with_its_empty_groups_and_only_its_own_restore_is_undone()
    {
        Make("delete group");

        Assert.Equal(GroupOutcome.NothingMatched, _session.Unlocked!.RestoreRecycledGroup("nowhere"));

        _entries.UndoCommand.Press();

        using var reopened = Vault.Open(_vaultPath, _master);
        Assert.Contains("servers/empty", reopened.ReadGroupPaths());
        Assert.Empty(reopened.ReadRecycled());
    }

    [Fact]
    public void A_group_is_not_restored_over_one_that_took_its_name()
    {
        var vault = _session.Unlocked!;
        Assert.Equal(GroupOutcome.Recycled, vault.DeleteGroup("other"));
        Assert.Equal(GroupOutcome.Created, vault.CreateGroup(string.Empty, "other", out _));

        Assert.Equal(GroupOutcome.DestinationOccupied, vault.RestoreRecycledGroup("other"));
    }

    private void Make(string change)
    {
        switch (change)
        {
            case "delete item":
                Select("servers/staging");
                _entries.DeleteCommand.Press();
                break;

            case "rename item":
                Organize("servers/staging", "renamed", "servers");
                break;

            case "move item":
                Organize("servers/staging", "staging", "other");
                break;

            case "rename and move item":
                Organize("servers/staging", "renamed", "other");
                break;

            case "duplicate item":
                Select("servers/staging");
                _entries.DuplicateCommand.Press();
                break;

            case "create group":
                Group("servers");
                _entries.BeginCreateGroupCommand.Press();
                _entries.DraftGroupName = "made";
                _entries.ConfirmCreateGroupCommand.Press();
                break;

            case "rename group":
                Group("servers/live");
                _entries.BeginRenameGroupCommand.Press();
                _entries.DraftGroupName = "hosts";
                _entries.ConfirmRenameGroupCommand.Press();
                break;

            case "move group":
                Group("servers/live");
                _entries.BeginMoveGroupCommand.Press();
                _entries.GroupMoveTarget = _entries.MoveTargets.Single(group => group.Path == "other");
                _entries.ConfirmMoveGroupCommand.Press();
                break;

            case "delete group":
                Group("servers");
                _entries.BeginDeleteGroupCommand.Press();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(change), change, "no such change");
        }
    }

    private void Organize(string path, string title, string group)
    {
        Select(path);
        _entries.OrganizeCommand.Press();
        _entries.DraftTitle = title;
        _entries.MoveTarget = _entries.MoveTargets.Single(node => node.Path == group);
        _entries.ConfirmOrganizeCommand.Press();
    }

    private void Select(string path) => _entries.Selected = _entries.Rows.Single(row => row.Path == path);

    private void Group(string path) => _entries.SelectedGroup = _entries.Groups.Single(group => group.Path == path);

    /// <summary>Every live item's place and values and every group, read back from the file.</summary>
    private string VaultState()
    {
        using var vault = Vault.Open(_vaultPath, _master);
        var items = vault.ReadEntries()
            .Select(entry => $"{entry.GroupPath}|{entry.Title}|{entry.Username}|{entry.Password}")
            .Order(StringComparer.Ordinal);

        return string.Join("\n", items.Concat(vault.ReadGroupPaths().Order(StringComparer.Ordinal)));
    }
}
