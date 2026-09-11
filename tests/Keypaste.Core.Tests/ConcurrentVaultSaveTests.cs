using System.Diagnostics;
using System.Runtime.Versioning;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// V-F.6: a save survives another program holding a <c>KeePass_TxF_</c> name in the shared
/// <c>%TEMP%</c>, at one attempt.
/// </summary>
/// <remarks>
/// <para>
/// <b>The contenders are other processes, and deliberately not keypaste.</b> D-0122 measured the
/// cause as any holder of such a name — every KeePass-family program spells its transacted
/// temporary the same way, so KeePass 2 saving its own database is exactly the model. A contender
/// built from keypaste would carry D-0123's fix too and contend with nobody.
/// </para>
/// <para>
/// <b>The budget is one.</b> The retry absorbs this refusal given eight tries, which is what made
/// the original failure intermittent, so a green run at eight would prove the budget outlasted the
/// contention rather than that anything was repaired.
/// </para>
/// <para>
/// <b>The save reaches its private directory the way a shipped binary does</b>, through the same
/// <c>Vault</c> call path; there is no test-only setup here. If there were, a green run would prove
/// the harness rather than the product.
/// </para>
/// <para>
/// The contenders hold until they are told to let go, so the save meets the refusal on every run
/// rather than on most.
/// </para>
/// </remarks>
public sealed class ConcurrentVaultSaveTests : IDisposable
{
    private const int _contenders = 4;

    private const string _notWindows =
        "Transactional NTFS is a Windows file system feature; there is no name to contend for.";

    private const string _noRefusal =
        "This Windows build hands out the next 8.3 alias instead of refusing the create, so the " +
        "contention D-0122 measured on 10.0.26100 does not happen here and there is nothing to survive.";

    private readonly string _directory =
        Directory.CreateTempSubdirectory("keypaste-vf6-").FullName;

    [Fact]
    [SupportedOSPlatform("windows")]
    public void ASaveSurvivesAnotherProgramHoldingATemporaryName_AtOneAttempt()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip(_notWindows);
            return;
        }

        var shared = SharedTemporaryDirectory();
        var vault = NewSavedVault(out var path);
        using var guard = vault;

        using var held = ContendersHolding(shared);

        if (!ACreateIsRefusedIn(shared))
        {
            Assert.Skip(_noRefusal);
            return;
        }

        // The whole row. Before D-0123 this save named its temporary in the shared directory, where
        // the contenders hold the alias, and one attempt had nothing to fall back on.
        vault.AddEntry(new VaultEntry { Title = "written under contention", Password = "secret" });
        vault.SaveWaiting(waitBetweenAttempts: null, attempts: 1);

        using var reopened = Vault.Open(path, VaultRoundTripTests.MasterPassword);
        Assert.Contains(reopened.ReadEntries(), entry => entry.Title == "written under contention");
    }

    /// <summary>
    /// The temporary directory an unrepaired keypaste would have named its file in, which is where
    /// the contenders must hold.
    /// </summary>
    /// <remarks>
    /// Read from what the redirect recorded, and from the process's own temporary path when no save
    /// has redirected yet. Both answer the same question — where everything else on this machine
    /// puts its temporary files — so one test runs against a build with the fix and one without.
    /// </remarks>
    private static string SharedTemporaryDirectory() =>
        ProcessTemporaryDirectory.OriginalTemporaryVariables.TryGetValue("TMP", out var original)
        && !string.IsNullOrEmpty(original)
            ? original
            : Path.GetTempPath();

    /// <summary>Whether this build refuses the create the contenders hold the alias for.</summary>
    [SupportedOSPlatform("windows")]
    private static bool ACreateIsRefusedIn(string directory)
    {
        var probe = Path.Combine(directory, "KeePass_TxF_" + Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            using var stream = new FileStream(probe, FileMode.Create, FileAccess.Write, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        finally
        {
            try
            {
                File.Delete(probe);
            }
            catch (IOException)
            {
                // It was never created.
            }
        }
    }

    private Contenders ContendersHolding(string shared)
    {
        var release = Path.Combine(_directory, "release");
        var started = new List<Process>();

        for (var i = 0; i < _contenders; i++)
        {
            var ready = Path.Combine(_directory, "ready-" + i);
            var info = new ProcessStartInfo { FileName = ContenderPath(), UseShellExecute = false };
            info.ArgumentList.Add(shared);
            info.ArgumentList.Add(ready);
            info.ArgumentList.Add(release);

            var process = Process.Start(info)
                ?? throw new InvalidOperationException("the contender did not start");

            started.Add(process);

            var waited = Stopwatch.StartNew();
            while (!File.Exists(ready))
            {
                if (process.HasExited || waited.Elapsed > TimeSpan.FromSeconds(30))
                {
                    throw new InvalidOperationException(
                        "a contender never reported holding a name; exited: " + process.HasExited);
                }

                Thread.Sleep(20);
            }
        }

        return new Contenders(started, release);
    }

    private static string ContenderPath()
    {
        var directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "keypaste.slnx")))
        {
            var parent = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
            if (string.IsNullOrEmpty(parent))
            {
                throw new InvalidOperationException(
                    "Could not locate keypaste.slnx above " + AppContext.BaseDirectory);
            }

            directory = parent;
        }

        var configuration = AppContext.BaseDirectory.Contains("debug", StringComparison.OrdinalIgnoreCase)
            ? "debug"
            : "release";

        return Path.Combine(
            directory,
            "artifacts",
            "bin",
            "Keypaste.TxfContender",
            configuration,
            OperatingSystem.IsWindows() ? "Keypaste.TxfContender.exe" : "Keypaste.TxfContender");
    }

    private Vault NewSavedVault(out string path)
    {
        path = Path.Combine(
            Directory.CreateDirectory(Path.Combine(_directory, "vault")).FullName, "vault.kdbx");

        var vault = Vault.Create(path, VaultRoundTripTests.MasterPassword);
        vault.AddEntry(new VaultEntry { Title = "seeded", Password = "seed" });

        // Saved once so the file exists. FileTransactionEx writes in place when the base file is
        // absent, and would never reach the transacted path this test is about.
        vault.Save();

        return vault;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A contender's own scratch may still be going; the OS cleans the temp tree.
        }
    }

    private sealed class Contenders(List<Process> processes, string release) : IDisposable
    {
        public void Dispose()
        {
            File.WriteAllText(release, "go");

            foreach (var process in processes)
            {
                try
                {
                    if (!process.WaitForExit(TimeSpan.FromSeconds(15)))
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Already gone.
                }

                process.Dispose();
            }
        }
    }
}
