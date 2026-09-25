using Keypaste.App.Clipboard;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Sharing;
using Xunit;

namespace Keypaste.App.Tests.ViewModels;

/// <summary>
/// The Sharing screen's form and list, against a fake keypaste.com: the link leaves only through the
/// clipboard's countdown, and the list shows limits and statuses, never a key or a value.
/// </summary>
public sealed class SharingViewModelTests : IDisposable
{
    private const string StripeValue = "sk_live_THE-VALUE-THAT-MUST-NOT-SHOW";

    private readonly TempVault _vault = new();
    private readonly FakeShareServer _server = new();
    private readonly Tests.Clipboard.FakeClipboard _clipboard = new();
    private readonly ManualClock _clock = new();
    private readonly AppVaultSession _session;
    private readonly ClipboardCountdown _countdown;

    public SharingViewModelTests()
    {
        using (var vault = Vault.Open(_vault.Path_, TempVault.Password))
        {
            vault.AddEntry(new VaultEntry { GroupPath = "env/acme-api", Title = "STRIPE_KEY", Password = StripeValue });
            vault.AddEntry(new VaultEntry { GroupPath = ReservedGroups.Tokens, Title = "hidden", Password = "verifier" });
            vault.Save();
        }

        _session = new AppVaultSession(_clock, home: _vault.Home);
        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_vault.Path_, master.Value));
        }

        _countdown = new ClipboardCountdown(_clipboard, _clock);
    }

    public void Dispose()
    {
        _countdown.Dispose();
        _session.Dispose();
        _server.Dispose();
        _vault.Dispose();
    }

    private SharingViewModel Screen() => new(
        _session,
        _countdown,
        new ShareService(
            new ShareClient(_server, ShareEndpoint.Default),
            _clock,
            () => AuditLog.TryOpen(KeypasteHome.AuditPath(_vault.Home), _clock, out var log, out _) ? log : null));

    [Fact]
    public void Defaults_Are24hAndOneView()
    {
        using var screen = Screen();

        Assert.Equal("24h", screen.Ttl);
        Assert.Equal(1, screen.Views);
        Assert.Equal("password", screen.Field);
        Assert.False(screen.RequirePassphrase);
        Assert.Equal(["1h", "24h", "7d"], SharingViewModel.TtlOptions);
        Assert.Equal([1, 3, 10], SharingViewModel.ViewOptions);
        Assert.Empty(screen.Rows);
        Assert.Contains("env/acme-api/STRIPE_KEY", screen.Candidates);
        Assert.DoesNotContain(screen.Candidates, path => path.StartsWith(ReservedGroups.Root, StringComparison.Ordinal));
        Assert.False(screen.CreateCommand.CanExecute(null));
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task Create_CopiesTheLink_AndAddsARow()
    {
        using var screen = Screen();
        screen.SelectedWhat = "env/acme-api/STRIPE_KEY";
        screen.Views = 3;
        screen.Recipient = "sam@acme.dev";

        await screen.CreateCommand.ExecuteAsync();

        Assert.Null(screen.Error);
        Assert.NotNull(_clipboard.Content);
        Assert.True(_clipboard.ContentWasSetAsASecret);
        Assert.True(_countdown.IsCounting);
        Assert.True(ShareLink.TryParse(_clipboard.Content, out var id, out var key));
        Assert.True(ShareCrypto.TryOpen(_server.Shares[id].Envelope, key, ReadOnlySpan<char>.Empty, out var payload, out _));
        Assert.Equal(StripeValue, Assert.Single(payload.Fields).Value);

        var until = TimeZoneInfo.ConvertTime(_server.Now.AddHours(24), _clock.LocalTimeZone).ToString("d MMM HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal($"Link copied. It opens 3 times, until {until}.", screen.Toast);

        var row = Assert.Single(screen.Rows);
        Assert.Equal(id, row.Id);
        Assert.Equal("env/acme-api/STRIPE_KEY", row.What);
        Assert.Equal("sam@acme.dev", row.Recipient);
        Assert.Equal("3 views · 24h", row.Rule);
        Assert.Equal("3 views left", row.Status);
        Assert.Equal(ShareStatusTone.Ok, row.StatusTone);
        Assert.DoesNotContain(key, row.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(key, screen.Toast, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_WithAPassphrase_SealsWithIt()
    {
        using var screen = Screen();
        screen.SelectedWhat = "env/acme-api/STRIPE_KEY";
        screen.RequirePassphrase = true;

        await screen.CreateCommand.ExecuteAsync();
        Assert.NotNull(screen.Error);
        Assert.Empty(_server.Requests);

        foreach (var c in "a long passphrase")
        {
            screen.Passphrase.Type(c);
        }

        await screen.CreateCommand.ExecuteAsync();

        Assert.Null(screen.Error);
        Assert.True(ShareLink.TryParse(_clipboard.Content!, out var id, out var key));
        Assert.True(ShareCrypto.TryOpen(_server.Shares[id].Envelope, key, "a long passphrase", out _, out _));
        Assert.Contains("passphrase", Assert.Single(screen.Rows).Rule, StringComparison.Ordinal);
        Assert.False(screen.Passphrase.HasValue);
    }

    [Fact]
    public async Task Create_ClipboardFailure_WithdrawsTheLink()
    {
        _clipboard.SetFails = true;
        using var screen = Screen();
        screen.SelectedWhat = "env/acme-api/STRIPE_KEY";

        await screen.CreateCommand.ExecuteAsync();

        Assert.Equal("The link could not be copied, so it was withdrawn.", screen.Error);
        Assert.Empty(_server.Shares);
        Assert.Empty(screen.Rows);
    }

    [Fact]
    public async Task Revoke_RemovesTheRow()
    {
        using var screen = Screen();
        screen.SelectedWhat = "env/acme-api/STRIPE_KEY";
        await screen.CreateCommand.ExecuteAsync();

        screen.Selected = Assert.Single(screen.Rows);
        await screen.RevokeCommand.ExecuteAsync();

        Assert.Equal("Revoked. The link no longer opens.", screen.Toast);
        Assert.Empty(screen.Rows);
        Assert.Empty(_server.Shares);
        Assert.Null(screen.Selected);
    }

    [Fact]
    public async Task Refresh_ShowsWhatTheServerSays()
    {
        using var screen = Screen();
        screen.SelectedWhat = "env/acme-api/STRIPE_KEY";
        await screen.CreateCommand.ExecuteAsync();
        _server.OpenAll(Assert.Single(screen.Rows).Id);

        await screen.RefreshCommand.ExecuteAsync();

        var row = Assert.Single(screen.Rows);
        Assert.Equal("gone", row.Status);
        Assert.Equal(ShareStatusTone.Muted, row.StatusTone);
    }

    [Fact]
    public async Task Dispose_ForgetsEverythingReadFromTheVault()
    {
        var screen = Screen();
        screen.SelectedWhat = "env/acme-api/STRIPE_KEY";
        await screen.CreateCommand.ExecuteAsync();

        screen.Dispose();

        Assert.Empty(screen.Rows);
        Assert.Empty(screen.Candidates);
        Assert.Null(screen.SelectedWhat);
        Assert.Null(screen.Toast);
        Assert.True(screen.Passphrase.IsZeroed);
    }
}
