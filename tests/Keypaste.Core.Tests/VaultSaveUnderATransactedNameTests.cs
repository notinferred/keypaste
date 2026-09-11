using System.ComponentModel;
using System.Reflection;
using System.Runtime.Versioning;
using Keypaste.Core.Internal;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// What a save does when the vault's own name is held by somebody else's transaction
/// (docs/STEPS.md F.7).
/// </summary>
/// <remarks>
/// <para>
/// KeePassLib's Windows save moves a temporary file onto the vault. When Transactional NTFS refuses
/// that move it falls back to two plain moves — onto the vault's drive, then onto the vault — and
/// raises <see cref="Win32Exception"/> if either is refused. That type is not
/// <see cref="IOException"/>, so before F.7 none of the eight attempts ran: the save reported
/// failure at once, having already left the first hop's file at <c>&lt;vault&gt;.kdbx.tmp</c>.
/// </para>
/// <para>
/// <b>Every test here asserts on the data, not only on the exception type</b>, which is the rule
/// <see cref="VaultConcurrentWriteTests"/> states and for the same reason: an implementation that
/// throws and writes anyway satisfies <c>Assert.Throws</c> perfectly and loses the write regardless.
/// </para>
/// <para>
/// <b>The retry is the reason a conflict here is resolved from inside the wait rather than on a
/// timer.</b> An attempt is nearly all Argon2 derivation and the move is its last act, so a commit
/// landing mid-attempt lets that attempt's move succeed — and a timer cannot say which it will hit.
/// <see cref="Vault.SaveWaiting"/> exists so the commit or rollback lands at the one instant that
/// makes the outcome the same every run: after an attempt has been refused, before the vault is
/// re-read.
/// </para>
/// <para>
/// <see cref="ASaveRefusedTheVaultsOwnName_SaysWhichCodeRefusedIt_AndLeavesNothingBesideTheVault"/>
/// is the exception, and holds the name throughout. It therefore runs the whole budget — eight
/// Argon2 derivations plus about 2.2 seconds of sleeps, so three or four seconds. That is the test
/// working, not hanging.
/// </para>
/// <para>
/// The <c>[SupportedOSPlatform]</c> attributes are for the analyzer, which does not carry an
/// <c>OperatingSystem.IsWindows</c> guard into a lambda body. Each test still guards itself at
/// runtime and reports a skip, because that is what decides whether it runs.
/// </para>
/// </remarks>
public sealed class VaultSaveUnderATransactedNameTests : IDisposable
{
    internal const string MasterPassword = "transacted-name-tests-master-pw";

    private const string _notWindows =
        "Transactional NTFS is a Windows file system feature; there is no name to reserve here.";

    private const string _noTransactions =
        "this volume does not support transactions, so KeePassLib's transacted save path is not taken here either.";

    // docs/STEPS.md F.7 records the refusal on the Windows 10 floor this project advertises and
    // leaves it unobserved on Server 2025, so this asks the running machine rather than assuming
    // it. A runner that allows the move must skip rather than pass having exercised nothing.
    private const string _noRefusal =
        "this Windows version allows a plain move onto a name a transaction holds, so it does not reproduce F.7.";

    private readonly string _directory;

    public VaultSaveUnderATransactedNameTests()
    {
        _directory = Directory.CreateTempSubdirectory("keypaste-transacted-name-").FullName;
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>
    /// A name that comes free again is saved to, rather than refused on the first attempt.
    /// </summary>
    /// <remarks>
    /// The rollback leaves the vault exactly as it was, so nothing else wrote and the save is
    /// entitled to proceed. Reverting the transient rule makes the first attempt final and turns
    /// this red.
    /// </remarks>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void ANameThatComesFreeAgain_IsSavedRatherThanRefusedAtOnce()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip(_notWindows);
            return;
        }

        var (vault, path) = NewSavedVault("comes-free");
        using var _ = vault;

        using var held = HeldTransactedName.TryHold(path);
        if (held is null)
        {
            Assert.Skip(_noTransactions);
            return;
        }

        if (!HeldTransactedName.ReproducesTheRefusal(path))
        {
            Assert.Skip(_noRefusal);
            return;
        }

        vault.AddEntry(new VaultEntry { Title = "written-while-contended", Password = "kept" });
        vault.SaveWaiting(attempt =>
        {
            if (attempt == 1)
            {
                held.RollBack();
            }
        });

        // The claim is that the write landed, so read it back.
        using var reopened = Vault.Open(path, MasterPassword);
        Assert.Equal("kept", reopened.Find("written-while-contended")?.Password, StringComparer.Ordinal);
        Assert.NotNull(reopened.Find("seeded"));

        // The refused first attempt stranded one, and a later attempt succeeding is exactly the
        // case that would otherwise never go back for it.
        Assert.False(
            File.Exists(path + KeePassInterop.StrandedTemporarySuffix),
            "a save that succeeded on a retry left <vault>.kdbx.tmp beside the vault");
    }

    /// <summary>
    /// A name taken by a writer that then commits is refused, not reverted.
    /// </summary>
    /// <remarks>
    /// What holds the vault's name in the wild is another process saving this same vault. Waiting
    /// it out and then writing would discard its save from a stale copy, with no history item,
    /// which is the loss <see cref="VaultChangedOnDiskException"/> exists to refuse. Removing the
    /// re-read between attempts lets the next attempt find the name free and overwrite the commit,
    /// and turns this red.
    /// </remarks>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void ANameTakenByAWriterThatCommits_IsRefusedRatherThanReverted()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip(_notWindows);
            return;
        }

        var (vault, path) = NewSavedVault("commits");
        using var _ = vault;

        using var held = HeldTransactedName.TryHold(path);
        if (held is null)
        {
            Assert.Skip(_noTransactions);
            return;
        }

        if (!HeldTransactedName.ReproducesTheRefusal(path))
        {
            Assert.Skip(_noRefusal);
            return;
        }

        vault.AddEntry(new VaultEntry { Title = "must-not-land", Password = "no" });

        Assert.Throws<VaultChangedOnDiskException>(() => vault.SaveWaiting(attempt =>
        {
            if (attempt == 1)
            {
                held.Commit();
            }
        }));

        // The committed bytes still there, unchanged and entire, is both halves of the claim: the
        // other writer's file survived, and this vault — which does not contain them — was not
        // written over it.
        Assert.Equal(HeldTransactedName.DecoyBytes, File.ReadAllBytes(path));

        Assert.False(
            File.Exists(path + KeePassInterop.StrandedTemporarySuffix),
            "a save abandoned as changed-on-disk left <vault>.kdbx.tmp beside the vault");
    }

    /// <summary>
    /// A save that is refused throughout names the code that refused it, and strands nothing.
    /// </summary>
    /// <remarks>
    /// No wall-clock ceiling anywhere: the floor proves the attempts happened, and a ceiling would
    /// only assert how busy the machine is — which is what grew this budget in the first place
    /// (D-0107).
    /// </remarks>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void ASaveRefusedTheVaultsOwnName_SaysWhichCodeRefusedIt_AndLeavesNothingBesideTheVault()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip(_notWindows);
            return;
        }

        var (vault, path) = NewSavedVault("refused-throughout");
        using var _ = vault;

        using var held = HeldTransactedName.TryHold(path);
        if (held is null)
        {
            Assert.Skip(_noTransactions);
            return;
        }

        if (!HeldTransactedName.ReproducesTheRefusal(path))
        {
            Assert.Skip(_noRefusal);
            return;
        }

        vault.AddEntry(new VaultEntry { Title = "never-written", Password = "no" });

        var started = Environment.TickCount64;
        var failure = Assert.Throws<VaultException>(vault.Save);
        var elapsed = Environment.TickCount64 - started;

        // Not merely that it failed: that it failed on the refusal F.7 is about. Anything upstream
        // of the fallback move reports a different type, and this test would then be passing on a
        // failure it was never arranged to produce.
        var win32 = Assert.IsType<Win32Exception>(failure.InnerException);
        Assert.Equal(6800, win32.NativeErrorCode);

        // A fixed floor, deliberately NOT computed from SaveAttempts and SaveRetryDelayMilliseconds
        // — an expectation derived from the values under test follows them down to zero.
        Assert.True(elapsed >= 50, $"a doomed save returned in {elapsed}ms; it cannot have retried");

        Assert.False(
            File.Exists(path + KeePassInterop.StrandedTemporarySuffix),
            "a save that failed left <vault>.kdbx.tmp beside the vault");

        // The vault it could not write is still the vault it opened.
        held.RollBack();
        using var reopened = Vault.Open(path, MasterPassword);
        Assert.NotNull(reopened.Find("seeded"));
        Assert.Null(reopened.Find("never-written"));
    }

    /// <summary>
    /// The name keypaste sweeps is still the name KeePassLib writes.
    /// </summary>
    /// <remarks>
    /// <see cref="KeePassInterop.StrandedTemporarySuffix"/> restates a constant that is internal to
    /// the vendored assembly, so an upstream rename would silently turn the sweep into a no-op.
    /// Runs on every platform, unlike the regression above, which is the point of pinning it here
    /// rather than trusting the Windows tests to notice.
    /// </remarks>
    [Fact]
    public void TheVendoredTemporarySuffixIsStillTheOneWeSweep()
    {
        var vendored = Type.GetType(
            "KeePassLib.Serialization.FileTransactionEx, KeePassLib", throwOnError: true)!;

        var suffix = vendored.GetField("StrTempSuffix", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(suffix);
        Assert.Equal(
            KeePassInterop.StrandedTemporarySuffix,
            (string?)suffix.GetRawConstantValue(),
            StringComparer.Ordinal);
    }

    private (Vault Vault, string Path) NewSavedVault(string name)
    {
        var home = Directory.CreateDirectory(System.IO.Path.Combine(_directory, name)).FullName;
        var path = System.IO.Path.Combine(home, "vault.kdbx");

        var vault = Vault.Create(path, MasterPassword);
        vault.AddEntry(new VaultEntry { Title = "seeded", Password = "seed" });

        // Saved before the name is held, so the file exists: FileTransactionEx writes in place when
        // the base file is absent, and would never reach the transacted path this test is about.
        vault.Save();

        return (vault, path);
    }
}
