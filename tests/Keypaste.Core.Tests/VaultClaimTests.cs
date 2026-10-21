using System.Globalization;
using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>One owner per vault: a second claim is refused and names the first (D-0309).</summary>
public sealed class VaultClaimTests : IDisposable
{
    private readonly string _home = Directory.CreateTempSubdirectory("keypaste-claim-tests-").FullName;

    private string Vault(string name = "vault.kdbx") => Path.Combine(_home, name);

    [Fact]
    public void ASecondClaimOnTheSameVault_IsRefusedAndNamesTheHolder()
    {
        Assert.True(VaultClaim.TryAcquire(_home, Vault(), OwnerKind.DesktopApp, out var first, out _));

        using (first)
        {
            Assert.False(VaultClaim.TryAcquire(_home, Vault(), OwnerKind.TerminalAgent, out var second, out var refusal));

            Assert.Null(second);
            second?.Dispose();
            Assert.Contains(
                string.Create(CultureInfo.InvariantCulture, $"the keypaste desktop app (process {Environment.ProcessId})"),
                refusal,
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// <c>keypaste approve</c> names itself on disk by the kind every keypaste reads, which D-0470 left as it was, and a
    /// refusal names it by its command.
    /// </summary>
    [Fact]
    public void KeypasteApproveHoldingTheVault_KeepsItsKindOnDisk_AndIsNamedByItsCommand()
    {
        Assert.True(VaultClaim.TryAcquire(_home, Vault(), OwnerKind.TerminalAgent, out var first, out _));

        using (first)
        {
            Assert.Contains("kind=TerminalAgent", File.ReadAllLines(Path.Combine(_home, VaultClaim.DirectoryName, first.Vault.Key + ".owner")));
            Assert.False(VaultClaim.TryAcquire(_home, Vault(), OwnerKind.DesktopApp, out _, out var refusal, out var holder));
            Assert.Contains("keypaste approve (process", refusal, StringComparison.Ordinal);
            Assert.Equal(new VaultOwner(OwnerKind.TerminalAgent, Environment.ProcessId, first.Vault.Path), holder);
        }
    }

    [Fact]
    public void AClaimTaken_NamesNoHolder()
    {
        Assert.True(VaultClaim.TryAcquire(_home, Vault(), OwnerKind.DesktopApp, out var claim, out _, out var holder));

        Assert.Null(holder);
        claim.Dispose();
    }

    [Fact]
    public void AReleasedClaim_CanBeTakenAgain()
    {
        Assert.True(VaultClaim.TryAcquire(_home, Vault(), OwnerKind.DesktopApp, out var first, out _));
        first.Dispose();

        Assert.True(VaultClaim.TryAcquire(_home, Vault(), OwnerKind.TerminalAgent, out var second, out _));
        second.Dispose();
    }

    [Fact]
    public void TwoSpellingsOfOneVault_MeetAtOneClaim()
    {
        var respelled = Path.Combine(_home, ".", OperatingSystem.IsLinux() ? "vault.kdbx" : "VAULT.KDBX");

        Assert.True(VaultClaim.TryAcquire(_home, Vault(), OwnerKind.DesktopApp, out var first, out _));

        using (first)
        {
            Assert.False(VaultClaim.TryAcquire(_home, respelled, OwnerKind.TerminalAgent, out _, out _));
        }
    }

    [Fact]
    public void TwoVaults_DoNotContend()
    {
        Assert.True(VaultClaim.TryAcquire(_home, Vault(), OwnerKind.DesktopApp, out var first, out _));
        Assert.True(VaultClaim.TryAcquire(_home, Vault("other.kdbx"), OwnerKind.DesktopApp, out var second, out _));

        first.Dispose();
        second.Dispose();
    }

    [Fact]
    public void HolderOf_names_who_holds_a_vault_and_takes_nothing()
    {
        Assert.Null(VaultClaim.HolderOf(_home, Vault()));
        Assert.False(Directory.Exists(Path.Combine(_home, VaultClaim.DirectoryName)));

        Assert.True(VaultClaim.TryAcquire(_home, Vault(), OwnerKind.TerminalAgent, out var held, out _));

        using (held)
        {
            Assert.Equal(new VaultOwner(OwnerKind.TerminalAgent, Environment.ProcessId, held.Vault.Path), VaultClaim.HolderOf(_home, Vault()));
            Assert.Null(VaultClaim.HolderOf(_home, Vault("other.kdbx")));
            Assert.False(VaultClaim.TryAcquire(_home, Vault(), OwnerKind.DesktopApp, out _, out _));
        }

        Assert.Null(VaultClaim.HolderOf(_home, Vault()));
        Assert.True(VaultClaim.TryAcquire(_home, Vault(), OwnerKind.DesktopApp, out var next, out _));
        next.Dispose();
    }

    [Fact]
    public void AClaimNeedsNoVaultYet_SoCreatingOneIsCoveredToo()
    {
        Assert.False(File.Exists(Vault("new.kdbx")));
        Assert.True(VaultClaim.TryAcquire(_home, Vault("new.kdbx"), OwnerKind.DesktopApp, out var claim, out _));

        Assert.Equal(PathIdentity.Canonical(Vault("new.kdbx")), claim.Vault.Path);
        claim.Dispose();
    }

    public void Dispose() => Directory.Delete(_home, recursive: true);
}
