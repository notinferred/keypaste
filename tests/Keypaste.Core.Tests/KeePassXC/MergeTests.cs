using Keypaste.Core.Tokens;
using Xunit;

namespace Keypaste.Core.Tests.KeePassXC;

/// <summary>keypaste merges a save real KeePassXC made to the file it holds, and KeePassXC then reads every change both kept.</summary>
/// <remarks>
/// The merge's rules are <c>VaultMergeTests</c>' and <c>VaultMergeSaveTests</c>', whose other program is KeePassLib; here it is KeePassXC
/// itself. A merge keeps the newer of two versions by its stamp, which has whole seconds, so a test that orders two edits waits past one.
/// </remarks>
public sealed class MergeTests
{
    private static readonly EntryName _stripe = new("work", "stripe");
    private static readonly EntryName _github = new("work", "github");
    private static readonly TimeSpan _pastAStamp = TimeSpan.FromMilliseconds(1100);

    [Fact]
    public void KeePassXCs_save_to_one_item_and_the_apps_edit_of_another_are_both_kept()
    {
        using var file = Seeded();
        using var open = file.Open();
        Update(open, _stripe, "mine");

        Edit(file, "work/github", "-u", "kpxc-user");
        var merged = Merge(open);

        Assert.True(merged.Report!.TookFromOpenCopy);
        AssertReads(file, "work/stripe", "Password", "mine");
        AssertReads(file, "work/github", "UserName", "kpxc-user");
    }

    [Fact]
    public void One_item_changed_by_both_keeps_the_newer_current_and_the_older_in_its_history_in_either_order()
    {
        using (var file = Seeded())
        using (var open = file.Open())
        {
            EditPassword(file, "work/stripe", "kpxc-older");
            Thread.Sleep(_pastAStamp);
            Update(open, _stripe, "app-newer");

            var merged = Merge(open);

            Assert.Contains(merged.Report!.Conflicts, conflict => conflict.Item == _stripe && conflict.OpenCopyCurrent);
            AssertReads(file, "work/stripe", "Password", "app-newer");
            Assert.Contains("kpxc-older", HistoryValues(file, "stripe"));
        }

        using (var file = Seeded())
        using (var open = file.Open())
        {
            Update(open, _stripe, "app-older");
            Thread.Sleep(_pastAStamp);
            EditPassword(file, "work/stripe", "kpxc-newer");

            var merged = Merge(open);

            Assert.Contains(merged.Report!.Conflicts, conflict => conflict.Item == _stripe && !conflict.OpenCopyCurrent);
            AssertReads(file, "work/stripe", "Password", "kpxc-newer");
            Assert.Contains("app-older", HistoryValues(file, "stripe"));
        }
    }

    [Fact]
    public void The_app_and_KeePassXC_changing_different_fields_of_one_item_keep_both_changes()
    {
        using var file = Seeded();
        using var open = file.Open();
        Update(open, _stripe, "app-password");
        Thread.Sleep(_pastAStamp);
        Edit(file, "work/stripe", "--url", "https://kpxc.example");

        Merge(open);

        Assert.Equal(["app-password", "https://kpxc.example"], KeePassXcCli.Show(file.Path_, "work/stripe", "Password", "URL"));
    }

    [Fact]
    public void An_item_KeePassXC_purged_goes_unless_the_app_changed_it_and_one_KeePassXC_recycled_keeps_the_apps_edit_in_the_bin()
    {
        using var file = new KeePassXcVault();
        using (var vault = file.Create())
        {
            foreach (var title in new[] { "untouched", "edited", "recycled" })
            {
                vault.AddEntry(new VaultEntry { GroupPath = "work", Title = title, Password = $"{title}-0" });
            }

            vault.Save();
        }

        using var open = file.Open();
        Update(open, new EntryName("work", "edited"), "edited-by-app");
        Thread.Sleep(_pastAStamp);
        foreach (var title in new[] { "untouched", "edited" })
        {
            KeePassXcCli.Run("rm", file.Path_, $"work/{title}");
            KeePassXcCli.Run("rm", file.Path_, $"Recycle Bin/{title}");
        }

        // KeePassXC recycles first, so the app's edit after it is the newer version.
        KeePassXcCli.Run("rm", file.Path_, "work/recycled");
        Thread.Sleep(_pastAStamp);
        Update(open, new EntryName("work", "recycled"), "recycled-by-app");

        var merged = Merge(open);

        var tree = KeePassXcCli.Run("ls", file.Path_, "-R", "-f").Split('\n');
        Assert.DoesNotContain(tree, line => line.EndsWith("/untouched", StringComparison.Ordinal));
        Assert.Contains(merged.Report!.Kept, kept => kept.Item.Title == "edited" && kept.DeletedInFile);
        Assert.Contains(tree, line => line.EndsWith("/edited", StringComparison.Ordinal));
        Assert.Equal(["recycled-by-app"], KeePassXcCli.Show(file.Path_, "Recycle Bin/recycled", "Password"));
    }

    [Fact]
    public void An_attachment_KeePassXC_added_survives_the_merge_byte_for_byte()
    {
        using var file = Seeded();
        var statement = Path.Combine(file.Directory_, "statement.bin");
        File.WriteAllBytes(statement, System.Security.Cryptography.RandomNumberGenerator.GetBytes(4096));
        using var open = file.Open();
        Update(open, _stripe, "mine");

        KeePassXcCli.Run("attachment-import", file.Path_, "work/github", "statement.bin", statement);
        Merge(open);

        var exported = Path.Combine(file.Directory_, "statement.out");
        KeePassXcCli.Run("attachment-export", file.Path_, "work/github", "statement.bin", exported);
        Assert.Equal(File.ReadAllBytes(statement), File.ReadAllBytes(exported));
        AssertReads(file, "work/stripe", "Password", "mine");
    }

    [Fact]
    public void Items_KeePassXC_only_restamped_while_saving_another_gain_no_history()
    {
        using var file = Seeded();
        using var open = file.Open();
        var historyBefore = HistoryValues(file, "github").Count;
        Update(open, _stripe, "mine");

        Edit(file, "work/stripe", "--notes", "kpxc-notes");
        var merged = Merge(open);

        Assert.DoesNotContain(merged.Report!.Conflicts, conflict => conflict.Item == _github);
        Assert.Equal(historyBefore, HistoryValues(file, "github").Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_revoked_token_stays_revoked_when_KeePassXC_merges_a_copy_still_holding_it_either_way_round(bool intoTheFile)
    {
        using var file = new KeePassXcVault();
        var older = Path.Combine(file.Directory_, "older.kdbx");
        var now = DateTimeOffset.UtcNow;
        string? token;
        using (var vault = file.Create())
        {
            Assert.True(TokenScope.TryParseList("read:a/dev/*", out var scopes, out var error), error);
            Assert.True(new TokenStore(vault).TryCreate("ci", scopes, TimeSpan.FromDays(30), false, now, out _, out token, out error), error);
            vault.Save();
            File.Copy(file.Path_, older);
            Thread.Sleep(_pastAStamp);
            Assert.True(new TokenStore(vault).Revoke("ci", now));
            vault.Save();
        }

        var (target, source) = intoTheFile ? (file.Path_, older) : (older, file.Path_);
        Assert.Equal(0, KeePassXcCli.Try(KeePassXcCli.Password, ["merge", "-q", "-s", target, source]).Exit);

        using var merged = Vault.Open(target, KeePassXcCli.Password);
        Assert.Equal(TokenCheck.Unknown, new TokenStore(merged).Verify(token!, now, out _));
        Assert.Empty(new TokenStore(merged).List());
    }

    private static KeePassXcVault Seeded()
    {
        var file = new KeePassXcVault();
        using var vault = file.Create();
        vault.AddEntry(new VaultEntry { GroupPath = "work", Title = "stripe", Username = "user", Password = "stripe-0" });
        vault.AddEntry(new VaultEntry { GroupPath = "work", Title = "github", Username = "user", Password = "github-0" });
        vault.Save();
        return file;
    }

    private static void Update(Vault vault, EntryName name, string password) =>
        Assert.True(vault.UpdateEntry(name, new VaultEntry { GroupPath = name.GroupPath, Title = name.Title, Username = "user", Password = password }));

    private static void Edit(KeePassXcVault file, string entry, params string[] change) => KeePassXcCli.Run("edit", file.Path_, [.. change, entry]);

    // keepassxc-cli reads the vault's password, then the new one twice.
    private static void EditPassword(KeePassXcVault file, string entry, string password) =>
        Assert.Equal(0, KeePassXcCli.Try($"{KeePassXcCli.Password}\n{password}\n{password}", ["edit", "-q", "-p", file.Path_, entry]).Exit);

    /// <summary>Merges KeePassXC's save into the open copy and saves the result, which KeePassXC must still open as KDBX 4.</summary>
    private static VaultMergeSave Merge(Vault open)
    {
        var merged = open.MergeAnotherSave();
        merged.Copy?.Dispose();
        Assert.Equal((VaultMergeSaveOutcome.Saved, string.Empty), (merged.Outcome, merged.Reason));
        return merged;
    }

    private static void AssertReads(KeePassXcVault file, string entry, string attribute, string value)
    {
        file.AssertKdbx4();
        Assert.Equal([value], KeePassXcCli.Show(file.Path_, entry, attribute));
    }

    private static HashSet<string> HistoryValues(KeePassXcVault file, string title)
    {
        var entry = KeePassXcCli.Export(file.Path_).Descendants("Entry").First(e => KeePassXcVault.StringValue(e, "Title") == title);
        return entry.Element("History")?.Descendants("Value").Select(v => v.Value).ToHashSet() ?? [];
    }
}
