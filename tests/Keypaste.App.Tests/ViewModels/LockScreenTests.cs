using System.Security.Cryptography;
using System.Text;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core.Internal;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>What the lock screen says about the vault it shows, and the file an in-place import hands it.</summary>
public sealed class LockScreenTests : IDisposable
{
    private readonly TempVault _vault = new();

    public void Dispose() => _vault.Dispose();

    [Theory]
    [InlineData(nameof(VaultLockReason.Idle), "Locked at 14:32 after 5 minutes without use")]
    [InlineData(nameof(VaultLockReason.Manual), "You locked it at 14:32")]
    [InlineData(nameof(VaultLockReason.Minimized), "Locked at 14:32 when the window was minimized")]
    [InlineData(nameof(VaultLockReason.Requested), "Locked at 14:32 by keypaste lock")]
    [InlineData(nameof(VaultLockReason.AccessChanged), "")]
    [InlineData(nameof(VaultLockReason.Replaced), "")]
    public void The_note_says_why_it_locked(string reason, string expected) =>
        Assert.Equal(expected, UnlockViewModel.DescribeLock(Enum.Parse<VaultLockReason>(reason), new DateTimeOffset(2026, 9, 25, 14, 32, 5, TimeSpan.Zero), TimeSpan.FromMinutes(5)));

    [Theory]
    [InlineData(1, "1 minute")]
    [InlineData(60, "1 hour")]
    [InlineData(90, "1 hour 30 minutes")]
    [InlineData(480, "8 hours")]
    public void The_idle_note_names_the_timeout_in_force(int minutes, string words) =>
        Assert.EndsWith($"after {words} without use", UnlockViewModel.DescribeLock(VaultLockReason.Idle, DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(minutes)), StringComparison.Ordinal);

    [Fact]
    public void A_lock_the_screen_follows_is_described_and_a_launch_is_not()
    {
        _vault.RememberSelf();
        using var session = new AppVaultSession(new ManualClock());

        using (var launched = new UnlockViewModel(session, _vault.Home, new FakeVaultFilePicker(), () => { }))
        {
            Assert.False(launched.HasLockNote);
        }

        using var locked = new UnlockViewModel(session, _vault.Home, new FakeVaultFilePicker(), () => { }, lockedBy: VaultLockReason.Requested);
        Assert.True(locked.HasLockNote);
        Assert.EndsWith("by keypaste lock", locked.LockNote, StringComparison.Ordinal);
    }

    [Fact]
    public void The_heading_names_the_vault_or_invites_one()
    {
        using var session = new AppVaultSession(new ManualClock());
        using var model = new UnlockViewModel(session, _vault.Home, new FakeVaultFilePicker(), () => { });

        Assert.Equal("Open a vault", model.Heading);
        Assert.True(model.Offer(_vault.Path_));
        Assert.Equal($"{Path.GetFileName(_vault.Path_)} is locked", model.Heading);
    }

    [Fact]
    public void The_recent_list_shows_only_when_it_offers_another_vault()
    {
        _vault.RememberSelf();
        using var session = new AppVaultSession(new ManualClock());
        using var model = new UnlockViewModel(session, _vault.Home, new FakeVaultFilePicker(), () => { });

        Assert.True(model.HasSelection);
        Assert.False(model.ShowsRecent);

        model.SelectedPath = null;
        Assert.True(model.ShowsRecent);
    }

    [Fact]
    public void An_in_place_file_arrives_with_its_keyfile()
    {
        _vault.RememberSelf();
        var keyfile = Path.Combine(_vault.Home, "foreign.keyx");
        File.WriteAllBytes(keyfile, RandomNumberGenerator.GetBytes(64));
        var foreign = Path.Combine(_vault.Home, "foreign.kdbx");
        KeePassInterop.WriteForeignUnchecked(foreign, Encoding.UTF8.GetBytes("in-place"), keyfile, "Argon2id", "ChaCha20");

        using var session = new AppVaultSession(new ManualClock());
        using var model = new UnlockViewModel(session, _vault.Home, new FakeVaultFilePicker(), () => { });

        Assert.True(model.Offer(foreign, keyfile));
        Assert.Equal(Path.GetFullPath(foreign), model.SelectedPath);
        Assert.Equal(Path.GetFullPath(keyfile), model.KeyfilePath);
        Assert.True(model.CanTypePassword);
    }

    [Fact]
    public void A_handed_over_file_is_opened_rather_than_called_locked()
    {
        var foreign = Path.Combine(_vault.Home, "handed.kdbx");
        KeePassInterop.WriteForeignUnchecked(foreign, Encoding.UTF8.GetBytes("in-place"), null, "Argon2id", "ChaCha20");

        using var session = new AppVaultSession(new ManualClock());
        using var model = new UnlockViewModel(session, _vault.Home, new FakeVaultFilePicker(), () => { });

        Assert.True(model.Offer(foreign, null));
        Assert.Equal("Open handed.kdbx", model.Heading);
        Assert.Contains("keep editing it in place", model.Subtitle, StringComparison.Ordinal);
        Assert.False(model.HasMessage);

        Assert.True(model.Offer(_vault.Path_));
        Assert.EndsWith("is locked", model.Heading, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_wrong_password_is_an_error_and_a_note_is_not()
    {
        using var session = new AppVaultSession(new ManualClock());
        using var model = new UnlockViewModel(session, _vault.Home, new FakeVaultFilePicker(), () => { });
        Assert.True(model.Offer(_vault.Path_));

        model.Type('x');
        await model.UnlockAsync();

        Assert.True(model.IsError);
        Assert.False(model.HasNote);
        Assert.False(model.HasLooseError);

        model.Message = "A note.";
        Assert.False(model.IsError);
        Assert.True(model.HasNote);
    }

    [Fact]
    public void A_file_that_is_not_a_vault_is_not_offered_with_a_keyfile()
    {
        using var session = new AppVaultSession(new ManualClock());
        using var model = new UnlockViewModel(session, _vault.Home, new FakeVaultFilePicker(), () => { });

        Assert.False(model.Offer(_vault.ImposterPath, _vault.ImposterPath));
        Assert.Null(model.KeyfilePath);
        Assert.False(model.HasSelection);
    }
}
