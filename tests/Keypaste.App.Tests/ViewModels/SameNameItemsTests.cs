using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>Two items with one title in one group each open and act as themselves, checked on the file after every write.</summary>
public sealed class SameNameItemsTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private readonly string _directory;
    private readonly string _vaultPath;

    public SameNameItemsTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-same-name-").FullName;
        _vaultPath = Path.Combine(_directory, "vault.kdbx");

        using var vault = Vault.Create(_vaultPath, _master);
        vault.AddEntry(new VaultEntry { Title = "Mail", Username = "alice", Password = "alice-password", GroupPath = "services" });
        vault.AddEntry(new VaultEntry { Title = "Mail", Username = "bob", Password = "bob-password", GroupPath = "services" });
        vault.AddEntry(new VaultEntry { Title = "Forum", Username = "carol", Password = "carol-password", GroupPath = "services" });
        vault.AddEntry(new VaultEntry { Title = "Mail", Username = "dan", Password = "dan-password", GroupPath = "archive" });
        vault.AddEntry(new VaultEntry { Title = "Note", Username = "erin", Password = "erin-password", GroupPath = "other" });
        vault.Save();
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Theory]
    [InlineData("alice")]
    [InlineData("bob")]
    public void Each_opens_as_itself_and_reveals_its_own_password(string username)
    {
        using var context = new Context(_vaultPath);

        var detail = context.Open(username);

        Assert.Null(context.Entries.Error);
        Assert.Equal(username, detail.DisplayUsername);
        Assert.Equal(username + "-password", detail.Reveal());
        Assert.Equal(username, context.Entries.Selected!.Username);
    }

    [Theory]
    [InlineData("alice")]
    [InlineData("bob")]
    public async Task Each_copies_its_own_values(string username)
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open(username);

        await detail.CopyPasswordCommand.PressAsync();
        Assert.Equal(username + "-password", context.Clipboard.Content);

        await detail.CopyUsernameCommand.PressAsync();
        Assert.Equal(username, context.Clipboard.Content);
    }

    [Fact]
    public void Choosing_the_other_of_the_pair_moves_the_pane_and_the_highlight()
    {
        using var context = new Context(_vaultPath);

        context.Open("alice");
        context.Entries.Reload();
        Assert.Equal("alice", context.Entries.Selected!.Username);

        var detail = context.Open("bob");

        Assert.Equal("bob-password", detail.Reveal());
        Assert.Equal("bob", context.Entries.Selected!.Username);
    }

    [Theory]
    [InlineData("alice", "bob")]
    [InlineData("bob", "alice")]
    public void An_edit_writes_to_the_item_it_opened(string username, string other)
    {
        using (var context = new Context(_vaultPath))
        {
            var detail = context.Open(username);

            detail.EditCommand.Press();
            detail.DraftNotes = "edited";
            detail.DraftUrl = "https://edited.test";
            Type(detail.NewPassword, "replaced");
            detail.SaveCommand.Press();

            Assert.Null(context.Entries.Error);
            Assert.Equal("edited", context.Open(username).DisplayNotes);
        }

        Assert.Equal("replaced", Saved(username).Password);
        Assert.Equal("edited", Saved(username).Notes);
        Assert.Equal(other + "-password", Saved(other).Password);
        Assert.Empty(Saved(other).Notes);
        Assert.Empty(History(other));
    }

    [Theory]
    [InlineData("alice", "bob")]
    [InlineData("bob", "alice")]
    public void A_rotation_changes_the_item_it_opened(string username, string other)
    {
        using (var context = new Context(_vaultPath))
        {
            var detail = context.Open(username);

            detail.RotateCommand.Press();
            detail.ConfirmRotateCommand.Press();

            Assert.Null(context.Entries.Error);
        }

        Assert.NotEqual(username + "-password", Saved(username).Password);
        Assert.Equal(other + "-password", Saved(other).Password);
    }

    [Theory]
    [InlineData("alice", "bob")]
    [InlineData("bob", "alice")]
    public void Fields_and_tags_go_on_the_item_they_were_added_to(string username, string other)
    {
        using (var context = new Context(_vaultPath))
        {
            var detail = context.Open(username);

            detail.BeginAddFieldCommand.Press();
            detail.DraftFieldName = "PIN";
            Type(detail.NewFieldValue, "1234");
            detail.ConfirmAddFieldCommand.Press();
            detail.DraftTag = "personal";
            detail.AddTagCommand.Press();

            Assert.Null(context.Entries.Error);
            Assert.Equal(["PIN"], detail.Fields.Select(field => field.Name));
            Assert.Equal(["personal"], detail.Tags.Select(tag => tag.Tag));
        }

        using var reopened = Vault.Open(_vaultPath, _master);

        Assert.Equal("1234", reopened.ReadField(Named(reopened, username), "PIN"));
        Assert.Equal(["personal"], reopened.Tags(Named(reopened, username))!);
        Assert.Empty(reopened.Fields(Named(reopened, other))!);
        Assert.Empty(reopened.Tags(Named(reopened, other))!);
    }

    [Theory]
    [InlineData("alice", "bob")]
    [InlineData("bob", "alice")]
    public void An_attachment_goes_on_the_item_it_was_dropped_on(string username, string other)
    {
        var file = Path.Combine(_directory, "key.pem");
        File.WriteAllBytes(file, [1, 2, 3]);

        using (var context = new Context(_vaultPath))
        {
            var detail = context.Open(username);

            detail.Attachments.AddDropped(file);

            Assert.Null(context.Entries.Error);
            Assert.Equal(["key.pem"], detail.Attachments.Rows.Select(row => row.Name));
        }

        using var reopened = Vault.Open(_vaultPath, _master);

        Assert.Single(reopened.Attachments(Named(reopened, username))!);
        Assert.Empty(reopened.Attachments(Named(reopened, other))!);
    }

    [Theory]
    [InlineData("alice", "bob")]
    [InlineData("bob", "alice")]
    public void History_reads_and_restores_the_item_it_opened(string username, string other)
    {
        using (var context = new Context(_vaultPath))
        {
            var detail = context.Open(username);

            detail.EditCommand.Press();
            Type(detail.NewPassword, "replaced");
            detail.SaveCommand.Press();
            detail = context.Open(username);

            detail.History.ToggleCommand.Press();
            var revision = Assert.Single(detail.History.Rows);

            Assert.Equal(username + "-password", revision.Reveal());
            revision.Conceal();

            detail.History.Selected = revision;
            detail.History.RestoreCommand.Press();

            Assert.Null(context.Entries.Error);
            Assert.Equal(username + "-password", context.Entries.Detail!.Reveal());
            Assert.Equal(username, context.Entries.Selected!.Username);
        }

        Assert.Equal(username + "-password", Saved(username).Password);
        Assert.Equal(other + "-password", Saved(other).Password);
        Assert.Empty(History(other));
    }

    [Theory]
    [InlineData("alice", "bob")]
    [InlineData("bob", "alice")]
    public void A_rename_takes_the_item_it_opened_and_ends_the_pair(string username, string other)
    {
        using (var context = new Context(_vaultPath))
        {
            context.Open(username);
            Assert.All(context.Entries.Rows.Where(row => row.GroupPath == "services" && row.Title == "Mail"), row => Assert.True(row.SharesItsName));

            context.Entries.OrganizeCommand.Press();
            context.Entries.DraftTitle = "Mail 2";
            context.Entries.ConfirmOrganizeCommand.Press();

            Assert.Null(context.Entries.Error);
            Assert.Equal("Mail 2", context.Entries.Selected!.Title);
            Assert.Equal(username, context.Entries.Selected.Username);
            Assert.Equal(username + "-password", context.Entries.Detail!.Reveal());
            Assert.DoesNotContain(context.Entries.Rows, row => row.SharesItsName);

            // Core will not give a second item the name back, so the screen offers no way back that could only fail.
            Assert.False(context.Entries.CanUndo);
        }

        using var reopened = Vault.Open(_vaultPath, _master);

        Assert.Equal(username + "-password", reopened.Find(new EntryName("services", "Mail 2"))!.Password);
        Assert.Equal(other + "-password", reopened.Find(new EntryName("services", "Mail"))!.Password);
    }

    [Fact]
    public void A_rename_that_leaves_no_pair_behind_still_offers_undo()
    {
        using var context = new Context(_vaultPath);

        context.Open("carol");
        context.Entries.OrganizeCommand.Press();
        context.Entries.DraftTitle = "Boards";
        context.Entries.ConfirmOrganizeCommand.Press();

        Assert.True(context.Entries.CanUndo);

        context.Entries.UndoCommand.Press();

        Assert.Null(context.Entries.Error);
        Assert.Equal("Forum", context.Entries.Selected!.Title);
    }

    [Theory]
    [InlineData("alice", "bob")]
    [InlineData("bob", "alice")]
    public void A_drag_moves_the_item_it_picked_up(string username, string other)
    {
        using (var context = new Context(_vaultPath))
        {
            var row = context.Entries.Rows.Single(candidate => candidate.Username == username);

            Assert.True(context.Entries.CanMove(row, "other"));

            context.Entries.MoveToGroup(row, "other");

            Assert.Null(context.Entries.Error);
        }

        using var reopened = Vault.Open(_vaultPath, _master);

        Assert.Equal(username + "-password", reopened.Find(new EntryName("other", "Mail"))!.Password);
        Assert.Equal(other + "-password", reopened.Find(new EntryName("services", "Mail"))!.Password);
    }

    [Theory]
    [InlineData("alice", "bob")]
    [InlineData("bob", "alice")]
    public void A_delete_takes_the_item_it_opened_and_undo_brings_that_one_back(string username, string other)
    {
        using var context = new Context(_vaultPath);

        context.Open(username);
        context.Entries.DeleteCommand.Press();

        Assert.Null(context.Entries.Error);
        Assert.Contains($"services/Mail ({username})", context.Entries.Notice, StringComparison.Ordinal);
        Assert.Equal(other, context.Entries.Rows.Single(row => row.GroupPath == "services" && row.Title == "Mail").Username);
        Assert.Equal(other + "-password", Saved(other).Password);

        context.Entries.UndoCommand.Press();

        Assert.Null(context.Entries.Error);
        Assert.Equal(username, context.Entries.Selected!.Username);
        Assert.Equal("Mail (restored)", context.Entries.Selected.Title);
        Assert.Equal(username + "-password", context.Entries.Detail!.Reveal());
        Assert.Equal("Restored as Mail (restored), because another item here is called Mail.", context.Entries.Notice);
        Assert.Equal(other + "-password", Saved(other).Password);
    }

    [Theory]
    [InlineData("alice", "bob")]
    [InlineData("bob", "alice")]
    public void Trash_restores_each_as_itself(string first, string second)
    {
        using var context = new Context(_vaultPath);
        var passwords = context.Session.Unlocked!.ReadEntries().ToDictionary(entry => entry.Uuid!, entry => entry.Password, StringComparer.Ordinal);

        context.Open(first);
        context.Entries.DeleteCommand.Press();
        context.Open(second);
        context.Entries.DeleteCommand.Press();
        Assert.Null(context.Entries.Error);

        using var trash = new TrashViewModel(context.Session);
        var one = trash.Rows[0].Id;
        var two = trash.Rows[1].Id;

        trash.Selected = trash.Rows.Single(row => row.Id == one);
        trash.RestoreCommand.Press();
        Assert.Null(trash.Error);

        trash.Selected = trash.Rows.Single(row => row.Id == two);
        trash.RestoreCommand.Press();

        // The name is taken now, so Trash offers a free one rather than making a pair core refuses to make.
        Assert.True(trash.OffersRestoreAs);
        trash.RestoreAsCommand.Press();

        var vault = context.Session.Unlocked!;

        Assert.Equal(passwords[one.UuidHex], vault.Find(new EntryName("services", "Mail"))!.Password);
        Assert.Equal(passwords[two.UuidHex], vault.Find(new EntryName("services", "Mail (restored)"))!.Password);
    }

    [Theory]
    [InlineData("alice", "bob")]
    [InlineData("bob", "alice")]
    public void With_no_trash_the_question_names_which_and_only_that_one_goes(string username, string other)
    {
        using (var vault = Vault.Open(_vaultPath, _master))
        {
            vault.SetRecyclesDeletedEntries(false);
            vault.Save();
        }

        using (var context = new Context(_vaultPath))
        {
            context.Open(username);
            context.Entries.DeleteCommand.Press();

            Assert.Equal($"Delete services/Mail ({username})? There is no undo.", context.Entries.DeletePrompt);

            context.Entries.ConfirmDeleteCommand.Press();

            Assert.Null(context.Entries.Error);
        }

        using var reopened = Vault.Open(_vaultPath, _master);
        Assert.Equal(other, reopened.Find(new EntryName("services", "Mail"))!.Username);
    }

    [Theory]
    [InlineData("alice")]
    [InlineData("bob")]
    public void A_duplicate_copies_the_item_it_opened(string username)
    {
        using var context = new Context(_vaultPath);

        context.Open(username);
        context.Entries.DuplicateCommand.Press();

        Assert.Null(context.Entries.Error);
        Assert.Equal("Mail (copy)", context.Entries.Selected!.Title);
        Assert.Equal(username + "-password", context.Entries.Detail!.Reveal());

        context.Entries.UndoCommand.Press();

        Assert.Equal(username, context.Entries.Selected!.Username);
        Assert.Equal(3, context.Session.Unlocked!.ReadEntries().Count(entry => entry.Title == "Mail"));
    }

    [Fact]
    public void A_search_lists_the_item_that_matched_and_not_the_other()
    {
        using var context = new Context(_vaultPath);

        context.Entries.Search = "alice";

        var row = Assert.Single(context.Entries.Rows);
        Assert.Equal("alice", row.Username);
        Assert.Equal("username", row.Why);
    }

    [Fact]
    public void Sorting_by_title_keeps_the_pair_in_a_settled_order()
    {
        using var context = new Context(_vaultPath);

        context.Entries.Table.Press(TableStates.Title);
        var ascending = context.Entries.Rows.Where(row => row.GroupPath == "services" && row.Title == "Mail").Select(row => row.Username);

        Assert.Equal(["alice", "bob"], ascending);

        context.Entries.Table.Press(TableStates.Title);
        var descending = context.Entries.Rows.Where(row => row.GroupPath == "services" && row.Title == "Mail").Select(row => row.Username);

        Assert.Equal(["alice", "bob"], descending);
    }

    [Fact]
    public void Only_a_title_another_row_in_its_group_has_counts_as_shared()
    {
        using var context = new Context(_vaultPath);

        var shared = context.Entries.Rows.Where(row => row.SharesItsName).Select(row => row.Username).Order();

        Assert.Equal(["alice", "bob"], shared);
    }

    [Fact]
    public void A_reference_is_offered_only_for_a_name_one_item_answers_to()
    {
        using var context = new Context(_vaultPath);

        Assert.Null(context.Open("alice").Reference);
        Assert.False(context.Entries.Detail!.HasReference);
        Assert.NotNull(context.Open("dan").Reference);
    }

    [Fact]
    public void The_pane_follows_its_item_when_another_write_renames_it()
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open("alice");
        var write = context.Session.Write(vault => vault.Relocate(detail.Name, new EntryName("services", "Webmail"), out _) == OrganizeOutcome.Renamed, renamed => renamed);

        Assert.Equal(WriteOutcome.Saved, write.Outcome);

        context.Entries.Reload();

        Assert.Equal("Webmail", context.Entries.Selected!.Title);
        Assert.Equal("Webmail", context.Entries.Detail!.Title);
        Assert.Equal("alice-password", context.Entries.Detail.Reveal());
    }

    [Fact]
    public void A_second_item_of_one_name_arriving_leaves_the_open_pane_on_its_own()
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open("dan");

        context.Session.Unlocked!.AddEntry(new VaultEntry { Title = "Mail", Username = "eve", Password = "eve-password", GroupPath = "archive" });

        Assert.Equal("dan-password", detail.Reveal());
    }

    [Theory]
    [InlineData("alice")]
    [InlineData("bob")]
    public async Task A_recommendation_opens_the_item_it_found(string username)
    {
        var home = Directory.CreateDirectory(Path.Combine(_directory, "home")).FullName;
        using var session = new AppVaultSession(new ManualClock(AppClock.Start));
        using (var master = TempVault.Secret(_master))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(_vaultPath, master.Value));
        }

        var uuid = session.Unlocked!.ReadEntries().Single(entry => entry.Username == username).Uuid;
        using var shell = new ShellViewModel(session, home, authority: null, clock: new ManualClock(AppClock.Start), post: null);
        await shell.Recommendations.Checking;
        var row = shell.Recommendations.Passwords.First(candidate => candidate.Finding.EntryUuid == uuid);

        shell.Recommendations.Open(row);

        var items = Assert.IsType<EntriesViewModel>(shell.Content);
        Assert.Equal(username, items.Selected!.Username);
        Assert.Equal(username + "-password", items.Detail!.Reveal());
    }

    [Theory]
    [InlineData("alice", "bob")]
    [InlineData("bob", "alice")]
    public async Task A_key_in_the_notes_moves_on_the_item_that_holds_it(string username, string other)
    {
        const string Key = "OPENAI_API_KEY=sk-abcdefghijklmnopqrstuvwxyz0123456789";

        using (var vault = Vault.Open(_vaultPath, _master))
        {
            vault.UpdateEntry(Named(vault, username), vault.Find(Named(vault, username))! with { Notes = Key });
            vault.Save();
        }

        var home = Directory.CreateDirectory(Path.Combine(_directory, "home")).FullName;

        using (var session = new AppVaultSession(new ManualClock(AppClock.Start)))
        {
            using (var master = TempVault.Secret(_master))
            {
                Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(_vaultPath, master.Value));
            }

            using var shell = new ShellViewModel(session, home, authority: null, clock: new ManualClock(AppClock.Start), post: null);
            await shell.Recommendations.Checking;

            shell.Recommendations.SelectAllCommand.Press();
            shell.Recommendations.MoveSelectedCommand.Press();
        }

        using var reopened = Vault.Open(_vaultPath, _master);

        Assert.Equal(["OPENAI_API_KEY"], reopened.Fields(Named(reopened, username))!.Select(field => field.Name));
        Assert.Empty(reopened.Fields(Named(reopened, other))!);
    }

    [Fact]
    public void The_share_dialog_says_which_of_two_items_it_shares()
    {
        var home = Directory.CreateDirectory(Path.Combine(_directory, "home")).FullName;
        using var session = new AppVaultSession(new ManualClock(AppClock.Start));
        using (var master = TempVault.Secret(_master))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(_vaultPath, master.Value));
        }

        using var shell = new ShellViewModel(session, home, authority: null, clock: new ManualClock(AppClock.Start), post: null);
        var items = Assert.IsType<EntriesViewModel>(shell.Content);
        items.Selected = items.Rows.Single(row => row.Username == "bob");

        shell.ShareCommand.Press(items.Detail!.Name);

        Assert.Equal("services/Mail (bob)", shell.Share!.SelectedWhat);
    }

    [Fact]
    public void A_file_holding_one_uuid_twice_opens_searches_and_moves_each_as_itself()
    {
        ForeignEdit.CloneKeepingItsUuid(_vaultPath, _master, "Forum", "Twin", "twin-password");

        using (var context = new Context(_vaultPath))
        {
            var forum = context.Entries.Rows.Single(row => row.Title == "Forum");
            var twin = context.Entries.Rows.Single(row => row.Title == "Twin");

            context.Entries.Selected = twin;
            Assert.Equal("Twin", context.Entries.Selected!.Title);
            Assert.Equal("twin-password", context.Entries.Detail!.Reveal());

            context.Entries.Selected = forum;
            Assert.Equal("Forum", context.Entries.Selected!.Title);
            Assert.Equal("carol-password", context.Entries.Detail!.Reveal());

            context.Entries.Search = "carol";
            Assert.Equal(["Forum", "Twin"], context.Entries.Rows.Select(row => row.Title).Order());
            context.Entries.Search = string.Empty;

            context.Entries.MoveToGroup(context.Entries.Rows.Single(row => row.Title == "Twin"), "other");
            Assert.Null(context.Entries.Error);
        }

        using var reopened = Vault.Open(_vaultPath, _master);
        Assert.Equal("carol-password", reopened.Find(new EntryName("services", "Forum"))!.Password);
        Assert.Equal("twin-password", reopened.Find(new EntryName("other", "Twin"))!.Password);
        Assert.Null(reopened.Find(new EntryName("services", "Twin")));
    }

    [Theory]
    [InlineData("alice", "bob")]
    [InlineData("bob", "alice")]
    public void Each_serves_its_own_TOTP_code(string username, string other)
    {
        var seeds = new Dictionary<string, string> { ["alice"] = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", ["bob"] = "MFRGGZDFMZTWQ2LKMFRGGZDFMZTWQ2LK" };

        using (var vault = Vault.Open(_vaultPath, _master))
        {
            foreach (var (who, seed) in seeds)
            {
                Assert.True(vault.SetFields(Named(vault, who), [new FieldWrite("TimeOtp-Secret", seed)]));
            }

            vault.Save();
        }

        using var context = new Context(_vaultPath);

        Assert.Equal(CodeOfALoneItem(seeds[username]), context.Open(username).Totp!.Reveal());
        Assert.Equal(CodeOfALoneItem(seeds[other]), context.Open(other).Totp!.Reveal());
    }

    [Theory]
    [InlineData("", "", "https://a.test", "https://b.test", "https://a.test", "https://b.test")]
    [InlineData("same", "same", "https://a.test", "https://b.test", "https://a.test", "https://b.test")]
    [InlineData("", "bob", "https://a.test", "https://a.test", "", "bob")]
    [InlineData("same", "same", "https://a.test", "https://a.test", "", "")]
    public void A_tell_is_what_only_that_item_has(string firstName, string secondName, string firstUrl, string secondUrl, string firstTell, string secondTell)
    {
        var path = PairWith(firstName, firstUrl, secondName, secondUrl);
        using var context = new Context(path);

        var rows = context.Entries.Rows.Where(row => row.Title == "Pair").OrderBy(row => row.Name.Uuid == FirstUuid(path) ? 0 : 1).ToList();

        Assert.Equal(firstTell, rows[0].SharedNameTell);
        Assert.Equal(secondTell, rows[1].SharedNameTell);
    }

    [Fact]
    public void Where_nothing_tells_them_apart_the_pane_says_so_and_offers_a_rename()
    {
        var path = PairWith(string.Empty, string.Empty, string.Empty, string.Empty, recycles: false);
        using var context = new Context(path);

        context.Entries.Selected = context.Entries.Rows.First(row => row.Title == "Pair");
        var detail = context.Entries.Detail!;

        Assert.True(detail.NothingTellsItApart);
        Assert.True(context.Entries.OrganizeCommand.CanExecute(null));

        context.Entries.DeleteCommand.Press();
        Assert.Equal("Delete the open item, services/Pair? Another item has this name. There is no undo.", context.Entries.DeletePrompt);
        context.Entries.CancelDeleteCommand.Press();

        Assert.Equal("services/Pair", context.Entries.Selected!.DisplayPath);
    }

    [Fact]
    public void A_pair_a_username_tells_apart_does_not_say_nothing_does()
    {
        using var context = new Context(_vaultPath);

        var detail = context.Open("alice");

        Assert.True(detail.SharesItsName);
        Assert.False(detail.NothingTellsItApart);
        Assert.False(context.Open("carol").SharesItsName);
    }

    [Fact]
    public void A_pane_learns_when_another_write_ends_its_pair()
    {
        using var context = new Context(_vaultPath);
        var detail = context.Open("alice");
        Assert.False(detail.HasReference);

        Assert.True(context.Session.Write(
            vault =>
            {
                vault.Relocate(Named(vault, "bob"), new EntryName("services", "Other mail"), out _);
                return true;
            },
            _ => true).Problem("rename bob") is null);
        context.Entries.Reload();

        Assert.False(detail.SharesItsName);
        Assert.True(detail.HasReference);
    }

    [Fact]
    public void The_share_dialog_with_nothing_to_tell_says_it_is_the_open_item()
    {
        var path = PairWith(string.Empty, string.Empty, string.Empty, string.Empty);
        var home = Directory.CreateDirectory(Path.Combine(_directory, "home")).FullName;
        using var session = new AppVaultSession(new ManualClock(AppClock.Start));
        using (var master = TempVault.Secret(_master))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(path, master.Value));
        }

        using var shell = new ShellViewModel(session, home, authority: null, clock: new ManualClock(AppClock.Start), post: null);
        var items = Assert.IsType<EntriesViewModel>(shell.Content);
        items.Selected = items.Rows.First(row => row.Title == "Pair");

        shell.ShareCommand.Press(items.Detail!.Name);

        Assert.Equal("services/Pair (the open item; another has this name)", shell.Share!.SelectedWhat);
    }

    [Theory]
    [InlineData("alice", "bob")]
    [InlineData("bob", "alice")]
    public async Task A_recommendation_names_its_item_and_dismissing_it_leaves_the_other(string username, string other)
    {
        const string Key = "OPENAI_API_KEY=sk-abcdefghijklmnopqrstuvwxyz0123456789";

        using (var vault = Vault.Open(_vaultPath, _master))
        {
            foreach (var who in new[] { "alice", "bob" })
            {
                vault.UpdateEntry(Named(vault, who), vault.Find(Named(vault, who))! with { Notes = Key });
            }

            vault.Save();
        }

        var home = Directory.CreateDirectory(Path.Combine(_directory, "home")).FullName;
        using var session = new AppVaultSession(new ManualClock(AppClock.Start));
        using (var master = TempVault.Secret(_master))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(_vaultPath, master.Value));
        }

        using var shell = new ShellViewModel(session, home, authority: null, clock: new ManualClock(AppClock.Start), post: null);
        await shell.Recommendations.Checking;

        RecommendationRow Row(string who) => shell.Recommendations.Rows.Single(row => row.Entry == $"services/Mail ({who})");

        Row(username).DismissCommand.Press();

        Assert.True(Row(username).IsDismissed);
        Assert.False(Row(other).IsDismissed);

        Row(other).MoveCommand.Press();

        Assert.Contains($"services/Mail ({other})", shell.Recommendations.Message, StringComparison.Ordinal);
        Assert.Empty(session.Unlocked!.Fields(Named(session.Unlocked, username))!);
        Assert.Single(session.Unlocked.Fields(Named(session.Unlocked, other))!);
    }

    /// <summary>The code a vault with no pair serves for this seed, at the clock every context starts on.</summary>
    private string CodeOfALoneItem(string seed)
    {
        var path = Path.Combine(_directory, $"lone-{seed}.kdbx");

        using (var vault = Vault.Create(path, _master))
        {
            var lone = new EntryName("services", "Lone");
            vault.AddEntry(new VaultEntry { Title = lone.Title, GroupPath = lone.GroupPath, Username = "lone" });
            Assert.True(vault.SetFields(lone, [new FieldWrite("TimeOtp-Secret", seed)]));
            vault.Save();
        }

        using var context = new Context(path);
        return context.Open("lone").Totp!.Reveal()!;
    }

    /// <summary>A vault of its own holding one pair in <c>services</c> as given, of which <see cref="FirstUuid"/> is the first.</summary>
    private string PairWith(string firstName, string firstUrl, string secondName, string secondUrl, bool recycles = true)
    {
        var path = Path.Combine(_directory, "pair.kdbx");
        using var vault = Vault.Create(path, _master);
        vault.AddEntry(new VaultEntry { Title = "Pair", Username = firstName, Url = firstUrl, Password = "first-password", GroupPath = "services" });
        vault.AddEntry(new VaultEntry { Title = "Pair", Username = secondName, Url = secondUrl, Password = "second-password", GroupPath = "services" });
        vault.SetRecyclesDeletedEntries(recycles);
        vault.Save();
        return path;
    }

    private static string FirstUuid(string path)
    {
        using var vault = Vault.Open(path, _master);
        return vault.ReadEntries().Single(entry => entry.Password == "first-password").Uuid!;
    }

    private static void Type(SecretField field, string text)
    {
        foreach (var character in text)
        {
            field.Type(character);
        }
    }

    private VaultEntry Saved(string username)
    {
        using var vault = Vault.Open(_vaultPath, _master);
        return vault.ReadEntries().Single(entry => entry.Username == username);
    }

    private IReadOnlyList<EntryRevision> History(string username)
    {
        using var vault = Vault.Open(_vaultPath, _master);
        return vault.ReadHistory(Named(vault, username))!;
    }

    private static EntryName Named(Vault vault, string username)
    {
        var entry = vault.ReadEntries().Single(candidate => candidate.Username == username);
        return new EntryName(entry.GroupPath, entry.Title) { Uuid = entry.Uuid };
    }

    private sealed class Context : IDisposable
    {
        internal Context(string vaultPath)
        {
            Session = new AppVaultSession(new ManualClock(AppClock.Start));

            using (var master = TempVault.Secret(_master))
            {
                Assert.Equal(UnlockOutcome.Opened, Session.TryUnlock(vaultPath, master.Value));
            }

            Clipboard = new FakeClipboard();
            Countdown = new ClipboardCountdown(Clipboard, new ManualClock(AppClock.Start));
            Entries = new EntriesViewModel(Session, Countdown);
        }

        internal AppVaultSession Session { get; }

        internal ClipboardCountdown Countdown { get; }

        internal EntriesViewModel Entries { get; }

        internal FakeClipboard Clipboard { get; }

        internal EntryDetailViewModel Open(string username)
        {
            Entries.Selected = Entries.Rows.Single(row => row.Username == username);
            return Entries.Detail!;
        }

        public void Dispose()
        {
            Entries.Dispose();
            Countdown.Dispose();
            Session.Dispose();
        }
    }
}
