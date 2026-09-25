using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.HardwareKeys;
using Keypaste.Core.Tests.HardwareKeys;
using Xunit;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// An unlocked session on a vault with a YubiKey: every save asks the key, the shell says to touch
/// it, and cancelling or locking while it waits saves nothing.
/// </summary>
public sealed class HardwareKeySessionTests : IDisposable
{
    private const string _master = "correct-horse-battery-staple";

    private static readonly byte[] _secret = Convert.FromHexString("5a6b7c8d9e0f112233445566778899aabbccddee");

    private readonly TempHome _home = new();
    private readonly SoftwareYubiKey _device = new(_secret);
    private readonly AppVaultSession _session;
    private readonly string _path;

    public HardwareKeySessionTests()
    {
        _session = new AppVaultSession(new ManualClock(), home: _home.Path, hardwareKeys: _device);
        _path = Path.Combine(_home.Path, "vault.kdbx");

        using var key = new HardwareKey(new SoftwareYubiKey(_secret), 2);
        using var vault = Vault.CreateWith(_path, _master, null, key);
        vault.AddEntry(new VaultEntry { Title = "github", Password = "gh" });
        vault.Save();
    }

    public void Dispose()
    {
        _session.Dispose();
        _home.Dispose();
    }

    [Fact]
    public void Each_save_asks_the_key_and_the_file_still_opens_with_it()
    {
        Unlock();
        var asked = _device.Challenges.Count;

        _session.Unlocked!.AddEntry(new VaultEntry { Title = "added", Password = "saved" });
        _session.Unlocked.Save();

        Assert.Equal(asked + 1, _device.Challenges.Count);
        _session.Lock(VaultLockReason.Manual);

        using var key = new HardwareKey(new SoftwareYubiKey(_secret), 2);
        using var reopened = Vault.Open(_path, _master, null, key);
        Assert.Equal("saved", reopened.Find("added")?.Password);
    }

    [Fact]
    public async Task The_shell_says_to_touch_the_key_while_a_save_waits_and_cancel_saves_nothing()
    {
        Unlock();
        using var shell = new ShellViewModel(_session, _home.Path, authority: null);
        var bytes = File.ReadAllBytes(_path);

        _device.NeverTouched = true;
        _session.Unlocked!.AddEntry(new VaultEntry { Title = "added", Password = "unsaved" });
        var saving = Task.Run(_session.Unlocked.Save, TestContext.Current.CancellationToken);

        await Until(() => shell.IsWaitingForTouch);
        shell.CancelTouchCommand.Execute(null);

        var refused = await Assert.ThrowsAsync<HardwareKeyException>(() => saving);
        Assert.Equal(HardwareKeyFailure.Cancelled, refused.Failure);
        Assert.False(shell.IsWaitingForTouch);
        Assert.Equal(bytes, File.ReadAllBytes(_path));
        Assert.True(_session.IsUnlocked);
    }

    [Fact]
    public async Task Locking_while_a_save_waits_for_the_key_saves_nothing()
    {
        Unlock();
        var bytes = File.ReadAllBytes(_path);
        var waiting = new TaskCompletionSource();
        _session.WaitingForTouch += (_, touch) =>
        {
            if (touch)
            {
                waiting.TrySetResult();
            }
        };

        _device.NeverTouched = true;
        var vault = _session.Unlocked!;
        vault.AddEntry(new VaultEntry { Title = "added", Password = "unsaved" });
        var saving = Task.Run(vault.Save, TestContext.Current.CancellationToken);

        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        _session.Lock(VaultLockReason.Manual);

        await Assert.ThrowsAnyAsync<Exception>(() => saving);
        Assert.False(_session.IsUnlocked);
        Assert.Equal(bytes, File.ReadAllBytes(_path));
    }

    private void Unlock()
    {
        using var master = TempVault.Secret(_master);
        Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_path, master.Value, null, 2));
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var tries = 0; tries < 500 && !condition(); tries++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }
}
