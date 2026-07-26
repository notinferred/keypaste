using Keypaste.Core.Internal;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// What happens when writing the vault file goes wrong.
/// </summary>
/// <remarks>
/// Saving goes through a file transaction — write a temporary file, replace the original — and on
/// Windows that replace competes with every process that watches the filesystem. Defender and the
/// search indexer open a newly written file to scan it, and the replace fails for a few
/// milliseconds. This showed up as intermittent "Could not save" failures across unrelated tests
/// on CI, which is what <see cref="KeePassInterop.SaveAttempts"/> exists to absorb.
/// </remarks>
public sealed class VaultSaveTests : IDisposable
{
    internal const string MasterPassword = "save-tests-master-pw";

    private readonly string _directory;

    public VaultSaveTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-save-tests-").FullName;
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>
    /// A save that cannot succeed still reports, and reports <em>why</em>. Without the cause the
    /// user gets "Could not save 'vault.kdbx'." and no way to tell a full disk from a file open in
    /// KeePassXC — and on CI it cost two round trips to learn what was actually failing.
    /// </summary>
    [Fact]
    public void ASaveThatCannotSucceed_ReportsTheReasonItFailed()
    {
        var path = Path.Combine(_directory, "locked.kdbx");

        using var vault = Vault.Create(path, MasterPassword);
        vault.AddEntry(new VaultEntry { Title = "T", Password = "p" });
        vault.Save();

        // Held open with no sharing at all, so the transaction's replace cannot possibly land.
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var error = Assert.Throws<VaultException>(vault.Save);

        Assert.Contains("locked.kdbx", error.Message, StringComparison.Ordinal);
        Assert.NotNull(error.InnerException);

        // The message says more than the path: it carries whatever the operating system said.
        Assert.True(
            error.Message.Length > $"Could not save '{path}': ".Length,
            $"the failure named no cause: {error.Message}");
    }

    /// <summary>
    /// Retrying must not turn a permanent failure into a hang. Four attempts with a linear
    /// backoff is well under a second, so a save that will never work still fails promptly.
    /// </summary>
    [Fact]
    public void ASaveThatCannotSucceed_GivesUpQuickly()
    {
        var path = Path.Combine(_directory, "give-up.kdbx");

        using var vault = Vault.Create(path, MasterPassword);
        vault.Save();

        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var started = Environment.TickCount64;
        Assert.Throws<VaultException>(vault.Save);
        var elapsed = Environment.TickCount64 - started;

        Assert.True(elapsed < 5_000, $"a doomed save took {elapsed}ms; retrying must stay bounded");
    }

    /// <summary>
    /// The retry is not allowed to be a no-op loop: if the delay or the attempt count were
    /// dropped to nothing, the transient window on Windows would stop being absorbed and the
    /// intermittent CI failures would come back with no test noticing.
    /// </summary>
    [Fact]
    public void TheRetryHasRoomToAbsorbATransientFailure()
    {
        Assert.True(KeePassInterop.SaveAttempts >= 3, "one retry is not enough to ride out a scanner");
        Assert.True(KeePassInterop.SaveRetryDelayMilliseconds >= 25, "an immediate retry hits the same lock");
    }

    /// <summary>A save that works still works, and the file is readable afterwards.</summary>
    [Fact]
    public void AnOrdinarySaveIsUnaffected()
    {
        var path = Path.Combine(_directory, "ordinary.kdbx");

        using (var vault = Vault.Create(path, MasterPassword))
        {
            vault.AddEntry(new VaultEntry { Title = "T", Password = "p", GroupPath = "env/dev" });
            vault.Save();
            vault.Save();
        }

        using var reopened = Vault.Open(path, MasterPassword);
        Assert.Equal("p", reopened.Find("env/dev/T")?.Password, StringComparer.Ordinal);
    }
}
