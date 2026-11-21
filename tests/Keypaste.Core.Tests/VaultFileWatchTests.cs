using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>How often the file watch's poll reads the vault's whole file.</summary>
/// <remarks>
/// The vault is opened through a link from a folder of its own, so the folder watch, which watches the link's folder, never sees
/// the file change and every comparison here is the poll's.
/// </remarks>
public sealed class VaultFileWatchTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-file-watch-").FullName;
    private readonly ManualClock _clock = new();

    private string FilePath => Path.Combine(_directory, "file", "vault.kdbx");

    public VaultFileWatchTests()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "file"));
        Directory.CreateDirectory(Path.Combine(_directory, "link"));

        using var created = ClaimedVault.Create(FilePath, VaultHistoryTests.MasterPassword);
        created.AddEntry(new VaultEntry { GroupPath = "env/app", Title = "KEY", Password = "value" });
        created.Save();
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task AWriteKeepingTheSizeAndTime_IsMergedByTheMinutesWholeRead()
    {
        using var watched = Watched();
        using var watch = new VaultFileWatch(watched);
        _clock.Advance(TimeSpan.FromSeconds(1));

        VaultSaveStateTests.RewriteKeepingSizeAndTime(FilePath);
        _clock.Advance(VaultFileWatch.Poll);
        await watch.Merging;
        Assert.Equal(0, watched.Merges);

        _clock.Advance(VaultFileWatch.FullPoll - VaultFileWatch.Poll);
        await watch.Merging;
        Assert.True(watched.Merges > 0);
    }

    [Fact]
    public async Task AWriteMovingTheTime_IsMergedAtTheNextPoll()
    {
        using var watched = Watched();
        using var watch = new VaultFileWatch(watched);
        _clock.Advance(TimeSpan.FromSeconds(1));

        VaultSaveStateTests.RewriteKeepingSizeAndTime(FilePath);
        File.SetLastWriteTimeUtc(FilePath, File.GetLastWriteTimeUtc(FilePath).AddSeconds(2));
        _clock.Advance(VaultFileWatch.Poll);
        await watch.Merging;

        Assert.True(watched.Merges > 0);
    }

    private FakeWatched Watched()
    {
        var link = VaultSaveTests.Link(Path.Combine(_directory, "link", "vault.kdbx"), FilePath);

        // The fake owns the vault it watches and disposes it.
#pragma warning disable CA2000
        return new FakeWatched(ClaimedVault.Open(link, VaultHistoryTests.MasterPassword), _clock);
#pragma warning restore CA2000
    }

    /// <summary>An approver whose merge only counts that it was asked for.</summary>
    private sealed class FakeWatched(Vault open, TimeProvider clock) : IWatchedVault, IDisposable
    {
        private int _merges;

        public TimeProvider Clock => clock;

        public Vault? Open => open;

        public ConflictedCopies ConflictedCopies { get; } = new();

        public bool MergesAnotherSave => true;

        internal int Merges => Volatile.Read(ref _merges);

        public event EventHandler? Opened
        {
            add { }
            remove { }
        }

        public event EventHandler? Locked
        {
            add { }
            remove { }
        }

        public void MergeAnotherSave() => Interlocked.Increment(ref _merges);

        public void Dispose() => open.Dispose();
    }
}
