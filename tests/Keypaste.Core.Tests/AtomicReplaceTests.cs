using KeePassLib.Serialization;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// A save that fails at its last step leaves the vault it was replacing at the vault's path.
/// </summary>
/// <remarks>
/// Upstream KeePassLib deleted the vault and then renamed the new file to its name, so a failure or
/// a crash between the two left nothing at the path. keypaste moves the new file over the vault in one
/// rename (KEYPASTE_ATOMIC_REPLACE). Windows commits through Transactional NTFS where it works, which
/// these tests do not reach.
/// </remarks>
public sealed class AtomicReplaceTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-atomic-replace-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_commit_whose_move_fails_leaves_the_vault_in_place()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows commits through Transactional NTFS where it can, which this test does not reach.");
        }

        var path = Path.Combine(_directory, "vault.kdbx");
        File.WriteAllBytes(path, [1, 2, 3]);

        using (var transaction = new FileTransactionEx(IOConnectionInfo.FromPath(path), true))
        {
            using (var written = transaction.OpenWrite())
            {
                written.Write([4, 5, 6]);
            }

            // Nothing is left to move, so the commit's last step fails.
            File.Delete(path + ".tmp");

            Assert.ThrowsAny<IOException>(transaction.CommitWrite);
        }

        Assert.Equal([1, 2, 3], File.ReadAllBytes(path));
    }

    [Fact]
    public void A_commit_replaces_the_vault_with_what_was_written()
    {
        var path = Path.Combine(_directory, "vault.kdbx");
        File.WriteAllBytes(path, [1, 2, 3]);

        using (var transaction = new FileTransactionEx(IOConnectionInfo.FromPath(path), true))
        {
            using (var written = transaction.OpenWrite())
            {
                written.Write([4, 5, 6]);
            }

            transaction.CommitWrite();
        }

        Assert.Equal([4, 5, 6], File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".tmp"));
    }
}
