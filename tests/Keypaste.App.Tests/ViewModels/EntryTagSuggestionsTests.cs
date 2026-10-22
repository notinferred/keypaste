using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// The item pane's tag box offers every environment the vault's items are tagged into, as its project tag, less the
/// item's own; a tag that puts no item in a project, an item in the recycle bin and a malformed project tag offer
/// nothing, and choosing a suggestion only fills the box, so Add tag still asks first (D-0415).
/// </summary>
public sealed class EntryTagSuggestionsTests : IDisposable
{
    private static readonly EntryName _token = new("api", "token");

    private readonly TempVault _fixture = new();
    private readonly AppVaultSession _session = new(new ManualClock(AppClock.Start));
    private readonly ClipboardCountdown _countdown = new(new FakeClipboard(), new ManualClock(AppClock.Start));
    private readonly EntriesViewModel _entries;

    public EntryTagSuggestionsTests()
    {
        using (var vault = Vault.Open(_fixture.Path_, TempVault.Password))
        {
            vault.AddEntry(new VaultEntry { GroupPath = "api", Title = "token", Password = "token-pw" });
            Tagged(vault, "Stripe", "env:billing", "finance");
            Tagged(vault, "Database", "env:billing:prod");
            Tagged(vault, "Shop", "env:shop:staging", "Env:billing:qa");
            Tagged(vault, "Old", "env:gone");
            Assert.NotEqual(DeletionOutcome.NothingMatched, vault.RemoveEntry(new EntryName("services", "Old")));
            Assert.True(vault.AddTag(_token, "env:billing"));
            vault.Save();
        }

        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_fixture.Path_, master.Value));
        }

        _entries = new EntriesViewModel(_session, _countdown);
    }

    public void Dispose()
    {
        _entries.Dispose();
        _countdown.Dispose();
        _session.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public void Every_environment_in_the_vault_is_offered_but_the_items_own()
    {
        var detail = Select("api/token");

        Assert.Equal(["env:billing:prod", "env:shop:staging"], detail.TagSuggestions);
        Assert.Equal(["env:billing", "env:shop:staging"], Select("services/Database").TagSuggestions);
    }

    [Fact]
    public void A_chosen_suggestion_is_asked_about_before_it_is_written_and_then_offered_no_more()
    {
        var detail = Select("api/token");

        detail.DraftTag = detail.TagSuggestions[0];
        detail.AddTagCommand.Execute(null);

        Assert.True(detail.IsConfirmingTagChange);
        Assert.DoesNotContain("env:billing:prod", _session.Unlocked!.Tags(_token)!);

        detail.ConfirmTagChangeCommand.Execute(null);

        Assert.Contains("env:billing:prod", _session.Unlocked.Tags(_token)!);
        Assert.Equal(["env:shop:staging"], detail.TagSuggestions);
    }

    private static void Tagged(Vault vault, string title, params string[] tags)
    {
        var name = new EntryName("services", title);
        vault.AddEntry(new VaultEntry { GroupPath = name.GroupPath, Title = title, Password = title + "-pw" });

        foreach (var tag in tags)
        {
            Assert.True(vault.AddTag(name, tag));
        }
    }

    private EntryDetailViewModel Select(string path)
    {
        _entries.Selected = _entries.Rows.Single(row => row.Path == path);
        return _entries.Detail!;
    }
}
