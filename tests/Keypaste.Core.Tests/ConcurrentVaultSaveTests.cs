using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Xunit;

namespace Keypaste.Core.Tests;

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

        // A fresh directory keeps hash-based 8.3 aliases from bypassing the intended collision.
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
            }
        }
    }

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

        // The product reads TMP during its one-time redirect, before the child can run test setup.
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

        // FileTransactionEx uses a transacted save only after the destination file exists.
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
            // A helper may still hold a temporary file during cleanup.
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathNameW(string path, [Out] char[] buffer, uint size);

    private sealed class Contender(Process process, string release, string holding) : IDisposable
    {
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
            }

            process.Dispose();
        }
    }
}

// Avoid adding helper-process scheduling load while other timed tests are running.
[CollectionDefinition(nameof(SavesThatSpawnProcesses), DisableParallelization = true)]
public sealed class SavesThatSpawnProcesses;
