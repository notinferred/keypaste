using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Recent;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Windows.Tests;

/// <summary>
/// The app's Windows build reaches Windows Hello only on Windows, and on a hosted runner, where nobody has set Windows
/// Hello up, reports it unavailable, so a password unlock keeps nothing and the screen offers only the full unlock (D-0549).
/// </summary>
/// <remarks>
/// A machine with Windows Hello set up needs a person: making the key, the prompt's focus, cancelling, the PIN and a
/// YubiKey vault resumed are checked by hand (4.10c).
/// </remarks>
public sealed class WindowsHelloTests : IDisposable
{
    private const string _master = "correct-horse-battery-staple";

    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-windows-hello-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void The_Windows_build_reaches_Windows_Hello_only_on_Windows()
    {
        var signer = QuickUnlockSigners.ForThisMachine();

        Assert.Equal(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041), signer is not null);

        if (signer is not null)
        {
            Assert.Equal("Windows Hello", signer.Name);
        }
    }

    [Fact]
    public async Task On_a_hosted_runner_Windows_Hello_is_unavailable_and_the_screen_offers_only_the_full_unlock()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") == "github-hosted",
            "Only a hosted Windows runner is known to have no Windows Hello set up.");

        var signer = QuickUnlockSigners.ForThisMachine();
        Assert.NotNull(signer);
        Assert.False(signer.IsAvailable());

        var path = Path.Combine(_directory, "vault.kdbx");
        using (var created = Vault.Create(path, _master))
        {
            created.Save();
        }

        RecentVaults.Save(KeypasteHome.RecentPath(_directory), [new RecentVault(path, DateTimeOffset.UtcNow)]);
        using var session = new AppVaultSession(new ManualClock(), home: _directory, quickUnlock: signer);
        var landed = 0;

        using (var unlock = new UnlockViewModel(session, _directory, new NoPicker(), () => landed++))
        {
            foreach (var c in _master)
            {
                unlock.Type(c);
            }

            await unlock.UnlockAsync();
        }

        Assert.Equal(1, landed);
        Assert.Equal(QuickUnlockOutcome.Unavailable, session.KeepForQuickUnlock());
        session.Lock(VaultLockReason.Manual);

        Assert.False(session.CanResume(path));
        using var locked = new UnlockViewModel(session, _directory, new NoPicker(), () => landed++, lockedBy: VaultLockReason.Manual);
        Assert.False(locked.OffersQuickUnlock);
        Assert.True(locked.UnlockIsPrimary);
        Assert.Equal("Enter its master password to open it.", locked.Subtitle);
    }

    /// <summary>A picker nobody answers; these tests choose no file.</summary>
    private sealed class NoPicker : IVaultFilePicker
    {
        public Task<string?> PickExistingAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickNewAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickExportDestinationAsync(string suggestedName) => Task.FromResult<string?>(null);

        public Task<string?> PickKeyfileAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickDotEnvAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
    }
}
