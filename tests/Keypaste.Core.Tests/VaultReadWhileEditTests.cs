using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>A vault read on one thread while another edits it, as the window or an agent's approval reads while a connection's approved save or a scan runs elsewhere.</summary>
public sealed class VaultReadWhileEditTests : IDisposable
{
    private const string _master = "correct horse battery staple";

    private const string _tag = "env:billing:prod";

    private static readonly EntryName _seed = new("services", "Stripe");

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-read-while-edit-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task Entries_groups_and_tags_read_on_another_thread_while_this_one_adds_tags_and_removes_entries_never_fault()
    {
        using var vault = ClaimedVault.Create(Path.Combine(_directory, "v.kdbx"), _master);
        vault.AddEntry(new VaultEntry { GroupPath = _seed.GroupPath, Title = _seed.Title, Password = "s" });
        vault.AddTag(_seed, _tag);
        vault.Save();

        using var done = new CancellationTokenSource();
        var reading = Task.Run(() =>
        {
            while (!done.IsCancellationRequested)
            {
                _ = vault.ReadEntries();
                _ = vault.Search(string.Empty);
                _ = vault.ReadGroupPaths();
                _ = vault.ReadRecycled();
                Assert.NotNull(vault.Find(_seed));
                Assert.Equal([_tag], vault.Tags(_seed));
                _ = vault.ReadTags();
            }
        }, TestContext.Current.CancellationToken);

        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (var round = 0; clock.Elapsed < TimeSpan.FromSeconds(2) && !reading.IsCompleted; round++)
            {
                var churn = new EntryName("churn", "c" + round);
                vault.AddEntry(new VaultEntry { GroupPath = churn.GroupPath, Title = churn.Title, Password = "p" });
                vault.AddTag(churn, _tag);
                vault.RemoveEntry(churn);

                if (round % 50 == 0)
                {
                    vault.EmptyRecycleBin();
                }
            }
        }
        finally
        {
            await done.CancelAsync();
        }

        await reading;
    }
}
