using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Vaults saved at the same time all get saved (DECISIONS.md D-0017).
/// </summary>
/// <remarks>
/// <para>
/// On Windows a save goes through KeePassLib's Transactional NTFS path, whose temporary file lives
/// in the one shared <c>%TEMP%</c> directory. A directory enlisted in one TxF transaction refuses
/// operations from outside it, so concurrent saves fail each other with <b>"The function attempted
/// to use a name that is reserved for use by another transaction"</b> — thrown where the temporary
/// file is opened, which is before the move that TxF has a fallback for.
/// </para>
/// <para>
/// keypaste is several processes by design — CLI, MCP server, approver, desktop app — so two saves
/// at once is an ordinary arrangement rather than a test artifact. This asserts the arrangement
/// keypaste actually ships.
/// </para>
/// <para>
/// <b>It does not reproduce the failure on a developer machine</b>, which is stated rather than
/// implied: 32 overlapping savers at six saves each pass here, and run 34303291945 failed twice on
/// a four-core Windows runner where the whole suite saves at once. This is a guard against the
/// retry budget being cut back, not the reproduction — that lives in CI.
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
