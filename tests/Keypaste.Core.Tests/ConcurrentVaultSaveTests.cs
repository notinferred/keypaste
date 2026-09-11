using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Vaults saved at the same time all get saved (DECISIONS.md D-0017).
/// </summary>
/// <remarks>
/// <para>
/// On Windows a save goes through KeePassLib's Transactional NTFS path, whose temporary file lives
/// in the one shared <c>%TEMP%</c> directory, and concurrent saves there fail each other with
/// <b>"The function attempted to use a name that is reserved for use by another transaction"</b> —
/// thrown where the temporary file is opened, which is before the move that TxF has a fallback for.
/// <b>Why they reach one name is not established</b>: the account this class used to give — a
/// directory enlisted in one transaction refusing operations from outside it — was refuted, and
/// F.6 owns the open diagnosis (D-0114).
/// </para>
/// <para>
/// keypaste is several processes by design — CLI, MCP server, approver, desktop app — so two saves
/// at once is an ordinary arrangement rather than a test artifact.
/// </para>
/// <para>
/// <b>This class does not reach that failure's path, and says so rather than reading as cover for
/// it.</b> Each saver calls <see cref="Vault.Create"/> on a path that does not exist yet, and
/// <c>FileTransactionEx</c>'s constructor sets <c>bTransacted</c> false whenever the base file is
/// absent, to keep the new file's ACL — so these saves run <b>zero transactions</b> and cannot
/// contend for a transactional name. They are also eight different vaults, where what keypaste
/// ships is several processes saving one. It is not a guard against the retry budget being cut
/// back either: at <c>SaveAttempts = 1</c> this class stays green and three <c>VaultSaveTests</c>
/// go red, which is where that guard actually lives. What it holds is the weaker claim in its
/// summary — eight vaults written into one directory at once all get written.
/// </para>
/// <para>
/// <b>V-F.6 owns replacing it</b> with a reproduction that enters the transacted path and is red
/// on Windows with the budget at 1. Until that exists, the concurrent-save failure is observed
/// only on CI, in runs 34303291945 and 34403613553, and no test here would catch its return.
/// <see cref="VaultSaveUnderATransactedNameTests"/> does enter the transacted path, but on the
/// move rather than on the temporary file, and from one thread — it is F.7's refusal, not this
/// contention, and it would not catch this one's return either.
/// </para>
/// </remarks>
public sealed class ConcurrentVaultSaveTests : IDisposable
{
    // internal, not private: .editorconfig applies the _camelCase field rule to private consts too.
    internal const int Savers = 8;

    private readonly string _directory;

    public ConcurrentVaultSaveTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-concurrent-save-").FullName;
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void SavesThatOverlap_AllSucceed()
    {
        var paths = Enumerable.Range(0, Savers)
            .Select(i => Path.Combine(_directory, $"vault-{i}.kdbx"))
            .ToArray();

        using var ready = new Barrier(Savers);

        var failures = paths
            .AsParallel()
            .WithDegreeOfParallelism(Savers)
            .Select(path =>
            {
                using var vault = Vault.Create(path, VaultRoundTripTests.MasterPassword);
                vault.AddEntry(new VaultEntry { Title = "entry", Password = "secret" });

                // Every saver waits for the last one, so the writes overlap rather than queueing
                // behind each other's Argon2 derivation.
                ready.SignalAndWait();

                try
                {
                    vault.Save();
                    return null;
                }
                catch (VaultException ex)
                {
                    return $"{Path.GetFileName(path)}: {ex.Message}";
                }
            })
            .Where(failure => failure is not null)
            .ToArray();

        Assert.True(failures.Length == 0, string.Join("\n  ", failures));

        foreach (var path in paths)
        {
            using var reopened = Vault.Open(path, VaultRoundTripTests.MasterPassword);
            Assert.Equal("secret", reopened.Find("entry")?.Password, StringComparer.Ordinal);
        }
    }
}
