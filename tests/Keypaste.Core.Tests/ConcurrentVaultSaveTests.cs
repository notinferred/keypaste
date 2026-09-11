using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// V-F.6: a save survives another program holding a <c>KeePass_TxF_</c> name in the temporary
/// directory it was given, at one attempt.
/// </summary>
/// <remarks>
/// <para>
/// <b>The contender is another process, and deliberately not keypaste.</b> D-0122 measured the
/// cause as any holder of such a name — every KeePass-family program spells its transacted
/// temporary the same way, so KeePass 2 saving its own database is exactly the model. A contender
/// built from keypaste would carry D-0123's fix too and contend with nobody.
/// </para>
/// <para>
/// <b>The saver is another process as well, and that is not a convenience.</b> The product
/// redirects once, on its first save, and then relies on <c>TMP</c> staying where it put it. Setting
/// <c>TMP</c> from inside this process after that had happened would not mislead the test so much as
/// undo the fix — <c>TxfPrepare</c> reads it at save time. Launching a process with <c>TMP</c>
/// already set is the only way the ambient value reaches the product's one-time redirect in the
/// right order, through the path a shipped binary takes and with no test-only setup.
/// </para>
/// <para>
/// <b>The contended directory is fresh and nothing else writes into it.</b> A machine-wide
/// <c>%TEMP%</c> cannot do this job: ci run 34628470851 watched the contention evaporate mid-test,
/// because once enough real <c>KeePass_TxF_</c> files occupy <c>KEEPAS~1</c> through <c>KEEPAS~4</c>
/// the file system starts handing out hash-based aliases that collide with nothing. An empty
/// directory leaves <c>KEEPAS~1.TMP</c> free, so the reservation bites every run.
/// </para>
/// <para>
/// <b>The budget is one.</b> The contender holds throughout, so eight attempts would fail too — but
/// then a green run could not tell a repair from a budget that outlasted a transient.
/// </para>
/// </remarks>
[Collection(nameof(SavesThatSpawnProcesses))]
public sealed class ConcurrentVaultSaveTests : IDisposable
{
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

        // Empty, and named by nothing else on the machine. This is what the saver is handed as its
        // ambient temporary directory.
        var contended = Directory.CreateDirectory(Path.Combine(_directory, "contended")).FullName;
        var vault = SeededVault();

        using var held = ContenderHolding(contended);

        if (!ACreateIsRefusedIn(contended))
        {
            Assert.Skip(_noRefusal);
            return;
        }

        var saver = Save(vault, contended, attempts: 1);

        Assert.True(
            held.StillHolding,
            "the contender stopped holding before the save finished, so the name was free again");

        Assert.True(
            ACreateIsRefusedIn(contended),
            "the contended directory stopped refusing the create before the save finished, so there "
            + "was no contention left to survive." + AliasesIn(contended, held.Holding));

        Assert.True(
            saver.Code == 0,
            $"the save was refused at one attempt.{Environment.NewLine}{saver.Output}"
            + AliasesIn(contended, held.Holding));

        using var reopened = Vault.Open(vault, VaultRoundTripTests.MasterPassword);
        Assert.Contains(reopened.ReadEntries(), entry => entry.Title == "written under contention");
    }

    /// <summary>Every name in a directory beside the 8.3 alias the file system gave it.</summary>
    /// <remarks>
    /// The alias is the mechanism, so a failure states it rather than leaving it inferred from an
    /// error code. If <c>KEEPAS~1.TMP</c> is not among these, the collision this test is built on is
    /// not the one that happened.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    private static string AliasesIn(string directory, string holding)
    {
        var report = new StringBuilder()
            .AppendLine()
            .AppendLine($"the contender holds: {holding}")
            .AppendLine($"8.3 aliases in {directory}:");

        try
        {
            foreach (var path in Directory.GetFileSystemEntries(directory))
            {
                var buffer = new char[512];
                var length = GetShortPathNameW(path, buffer, (uint)buffer.Length);
                var alias = length == 0
                    ? "(none)"
                    : Path.GetFileName(new string(buffer, 0, (int)length));

                report.AppendLine($"  {Path.GetFileName(path),-48} {alias}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            report.AppendLine($"  could not be listed: {ex.Message}");
        }

        return report.ToString();
    }

    /// <summary>Whether this build refuses the create the contender holds the alias for.</summary>
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

    /// <summary>Saves the vault from a process whose temporary directory is the contended one.</summary>
    private static (int Code, string Output) Save(string vault, string contended, int attempts)
    {
        var info = new ProcessStartInfo
        {
            FileName = Helper("Keypaste.VaultSaver"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        info.ArgumentList.Add(vault);
        info.ArgumentList.Add(attempts.ToString());

        // Set before the process exists, so the product's one-time redirect is the first thing to
        // read it. This is the whole arrangement; see the class remarks.
        info.Environment["TMP"] = contended;
        info.Environment["TEMP"] = contended;
        info.Environment["KEYPASTE_SAVER_PASSWORD"] = VaultRoundTripTests.MasterPassword;

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("the saver did not start");

        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();

        if (!process.WaitForExit(TimeSpan.FromSeconds(60)))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("the saver never finished");
        }

        return (process.ExitCode, output.Trim());
    }

    private Contender ContenderHolding(string contended)
    {
        var ready = Path.Combine(_directory, "ready");
        var release = Path.Combine(_directory, "release");

        var info = new ProcessStartInfo
        {
            FileName = Helper("Keypaste.TxfContender"),
            UseShellExecute = false,
            RedirectStandardError = true,
        };

        info.ArgumentList.Add(contended);
        info.ArgumentList.Add(ready);
        info.ArgumentList.Add(release);

        var process = Process.Start(info)
            ?? throw new InvalidOperationException("the contender did not start");

        var waited = Stopwatch.StartNew();
        while (!File.Exists(ready))
        {
            if (process.HasExited || waited.Elapsed > TimeSpan.FromSeconds(30))
            {
                var why = process.HasExited ? process.StandardError.ReadToEnd() : "(still running)";
                throw new InvalidOperationException($"the contender never held a name: {why}");
            }

            Thread.Sleep(20);
        }

        return new Contender(process, release, File.ReadAllText(ready));
    }

    private static string Helper(string name)
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
            name,
            configuration,
            OperatingSystem.IsWindows() ? name + ".exe" : name);
    }

    private string SeededVault()
    {
        var path = Path.Combine(
            Directory.CreateDirectory(Path.Combine(_directory, "vault")).FullName, "vault.kdbx");

        using var vault = Vault.Create(path, VaultRoundTripTests.MasterPassword);
        vault.AddEntry(new VaultEntry { Title = "seeded", Password = "seed" });

        // Saved once so the file exists. FileTransactionEx writes in place when the base file is
        // absent, so this save is not transacted and names no temporary anywhere.
        vault.Save();

        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A helper's scratch may still be going; the OS cleans the temp tree.
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathNameW(string path, [Out] char[] buffer, uint size);

    private sealed class Contender(Process process, string release, string holding) : IDisposable
    {
        /// <summary>The name it reserved, and with it the 8.3 alias everything else wants.</summary>
        internal string Holding => holding;

        internal bool StillHolding => !process.HasExited;

        public void Dispose()
        {
            File.WriteAllText(release, "go");

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

/// <summary>Saves driven from helper processes, run on their own.</summary>
/// <remarks>
/// Not load-bearing: V-F.6 mutates no process-wide state, and its contended directory is created
/// fresh and touched only by its own two helpers, so nothing another test does can reach it. This
/// keeps two spawned processes and their timing off a two-core runner's busiest moment, and no more
/// than that.
/// </remarks>
[CollectionDefinition(nameof(SavesThatSpawnProcesses), DisableParallelization = true)]
public sealed class SavesThatSpawnProcesses;
