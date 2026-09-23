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

    [Fact]
    public void AnAgentHoldingTheVault_IsNamedAsTheAgent()
    {
        Assert.True(VaultClaim.TryAcquire(_home, Vault(), OwnerKind.TerminalAgent, out var first, out _));

        using (first)
        {
            Assert.False(VaultClaim.TryAcquire(_home, Vault(), OwnerKind.DesktopApp, out _, out var refusal));
            Assert.Contains("keypaste agent (process", refusal, StringComparison.Ordinal);
        }
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
    public void AClaimNeedsNoVaultYet_SoCreatingOneIsCoveredToo()
    {
        Assert.False(File.Exists(Vault("new.kdbx")));
        Assert.True(VaultClaim.TryAcquire(_home, Vault("new.kdbx"), OwnerKind.DesktopApp, out var claim, out _));

        Assert.Equal(PathIdentity.Canonical(Vault("new.kdbx")), claim.Vault.Path);
        claim.Dispose();
    }

    public void Dispose() => Directory.Delete(_home, recursive: true);
}
