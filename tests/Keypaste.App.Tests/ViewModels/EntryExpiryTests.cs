using System.Globalization;
using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.Tests.Clipboard;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// The item pane shows an expiry KeePassXC set beside Created, and reads it as expired once the session's clock, the one
/// a run from the app judges it by, has reached it (D-0337).
/// </summary>
/// <remarks>
/// The session's clock starts at <see cref="AppClock.Start"/>, months before this machine's, so an item the machine's
/// clock has already seen expire still reads as one to come.
/// </remarks>
public sealed class EntryExpiryTests : IDisposable
{
    private static readonly DateTimeOffset _later = AppClock.Start.AddDays(30);
    private static readonly DateTimeOffset _earlier = AppClock.Start.AddDays(-2);

    private readonly TempVault _fixture = new();
    private readonly AppVaultSession _session = new(new ManualClock(AppClock.Start));
    private readonly ClipboardCountdown _countdown = new(new FakeClipboard(), new ManualClock(AppClock.Start));
    private readonly EntriesViewModel _entries;

    public EntryExpiryTests()
    {
        using (var vault = Vault.Open(_fixture.Path_, TempVault.Password))
        {
            vault.AddEntry(new VaultEntry { GroupPath = "api", Title = "renewing", Password = "renewing-pw" });
            vault.AddEntry(new VaultEntry { GroupPath = "api", Title = "lapsed", Password = "lapsed-pw" });
            vault.AddEntry(new VaultEntry { GroupPath = "api", Title = "lasting", Password = "lasting-pw" });
            vault.SetExpiryUnchecked(new EntryName("api", "renewing"), _later);
            vault.SetExpiryUnchecked(new EntryName("api", "lapsed"), _earlier);
            vault.Save();
        }

        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_fixture.Path_, master.Value));
        }

        _entries = new EntriesViewModel(_session, _countdown);
    }

    public void Dispose()
    {
        _entries.Dispose();
        _countdown.Dispose();
        _session.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public void An_expiry_still_to_come_shows_its_date_and_time()
    {
        var detail = Select("api/renewing");

        Assert.True(detail.HasExpiry);
        Assert.False(detail.IsExpired);
        Assert.Equal("Expires", detail.ExpiryLabel);
        Assert.Equal(Local(_later), detail.Expiry);
    }

    [Fact]
    public void An_expiry_the_session_clock_has_passed_reads_expired()
    {
        var detail = Select("api/lapsed");

        Assert.True(detail.HasExpiry);
        Assert.True(detail.IsExpired);
        Assert.Equal("Expired", detail.ExpiryLabel);
        Assert.Equal(Local(_earlier), detail.Expiry);
    }

    [Fact]
    public void An_item_that_never_expires_shows_no_expiry()
    {
        var detail = Select("api/lasting");

        Assert.False(detail.HasExpiry);
        Assert.False(detail.IsExpired);
        Assert.Empty(detail.Expiry);
    }

    private static string Local(DateTimeOffset at) => at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private EntryDetailViewModel Select(string path)
    {
        _entries.Selected = _entries.Rows.Single(row => row.Path == path);
        return _entries.Detail!;
    }
}
