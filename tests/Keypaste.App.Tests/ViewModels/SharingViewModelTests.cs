using System.Net;
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
/// clipboard's countdown, the list shows limits and statuses, never a key or a value, and a server
/// that does not take shares is shown as such rather than as a success.
/// </summary>
public sealed class SharingViewModelTests : IDisposable
{
    private const string _stripeValue = "sk_live_THE-VALUE-THAT-MUST-NOT-SHOW";

    private readonly TempVault _vault = new();
    private readonly FakeShareServer _server = new();
    private readonly Tests.Clipboard.FakeClipboard _clipboard = new();
    private readonly ManualClock _clock = new();
    private readonly AppVaultSession _session;
    private readonly ClipboardCountdown _countdown;
    private readonly List<string> _toasts = [];

    public SharingViewModelTests()
    {
        using (var vault = Vault.Open(_vault.Path_, TempVault.Password))
        {
            vault.AddEntry(new VaultEntry { GroupPath = "env/acme-api", Title = "STRIPE_KEY", Password = _stripeValue });
            vault.AddEntry(new VaultEntry { GroupPath = "Work", Title = "no-password", Username = "sam" });
            vault.AddEntry(new VaultEntry { GroupPath = ReservedGroups.Tokens, Title = "hidden", Password = "verifier" });
            vault.Save();
        }

        _session = new AppVaultSession(_clock, home: _vault.Home);
        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_vault.Path_, master.Value));
        }

        _server.Now = _clock.GetUtcNow();
        _countdown = new ClipboardCountdown(_clipboard, _clock);
    }

    public void Dispose()
    {
        _countdown.Dispose();
        _session.Dispose();
        _server.Dispose();
        _vault.Dispose();
    }

    private SharingViewModel Screen(string? unavailable = null) => new(
        _session,
        _countdown,
        new ShareService(
            new ShareClient(_server, ShareEndpoint.Default),
            _clock,
            () => AuditLog.TryOpen(KeypasteHome.AuditPath(_vault.Home), _clock, out var log, out _) ? log : null),
        _toasts.Add,
        unavailable);

    [Fact]
    public void Defaults_Are24hAndOneView()
    {
        using var screen = Screen();

        Assert.Equal("24h", screen.Ttl);
        Assert.Equal(1, screen.Views);
        Assert.Equal("password", screen.Field);
        Assert.False(screen.RequirePassphrase);
        Assert.Equal(["1h", "24h", "7d"], screen.TtlChoices);
        Assert.Equal([1, 3, 10], screen.ViewChoices);
        Assert.Empty(screen.Rows);
        Assert.True(screen.IsEmpty);
        Assert.False(screen.IsUnavailable);
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
        Assert.Equal(_stripeValue, Assert.Single(payload.Fields).Value);

        Assert.Equal("Link copied. Expires in 24h, 3 views.", Assert.Single(_toasts));

        var row = Assert.Single(screen.Rows);
        Assert.Equal(id, row.Id);
        Assert.Equal("env/acme-api/STRIPE_KEY", row.What);
        Assert.Equal("sam@acme.dev · 3 views · 24h", row.Detail);
        Assert.Equal("Not opened yet", row.Status);
        Assert.Equal(ShareStatusTone.Ok, row.StatusTone);
        Assert.False(screen.HasUnchecked);
        Assert.Equal("Revoke", row.RevokeLabel);
        Assert.DoesNotContain(key, row.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(_toasts, toast => toast.Contains(key, StringComparison.Ordinal));
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
        Assert.Empty(_toasts);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Create_WhileTheServerTakesNoShares_SaysSo_AndDisablesTheForm(HttpStatusCode answer)
    {
        _server.Answer = _ => FakeShareServer.Json(answer, "{\"error\":\"not found\"}");
        using var screen = Screen();
        screen.SelectedWhat = "env/acme-api/STRIPE_KEY";

        await screen.CreateCommand.ExecuteAsync();

        Assert.True(screen.IsUnavailable);
        Assert.Equal("keypaste.com is not accepting shares right now.", screen.Unavailable);
        Assert.Null(screen.Error);
        Assert.False(screen.CreateCommand.CanExecute(null));
        Assert.True(screen.CanRetry);
        Assert.Null(_clipboard.Content);
        Assert.Empty(_toasts);
        Assert.Empty(screen.Rows);

        _server.Answer = null;
        await screen.RetryCommand.ExecuteAsync();

        Assert.False(screen.IsUnavailable);
        Assert.True(screen.CreateCommand.CanExecute(null));
        Assert.NotNull(_clipboard.Content);
        Assert.Single(screen.Rows);
    }

    [Fact]
    public async Task Create_WhenTheServerCannotBeReached_SaysSo_AndDisablesTheForm()
    {
        _server.Unreachable = true;
        using var screen = Screen();
        screen.SelectedWhat = "env/acme-api/STRIPE_KEY";

        await screen.CreateCommand.ExecuteAsync();

        Assert.Equal("The share server could not be reached.", screen.Unavailable);
        Assert.False(screen.CreateCommand.CanExecute(null));
        Assert.Null(_clipboard.Content);
        Assert.Empty(_toasts);
        Assert.Empty(screen.Rows);
    }

    [Fact]
    public async Task Create_ARefusal_IsAnError_AndLeavesTheFormUsable()
    {
        using var screen = Screen();
        screen.SelectedWhat = "Work/no-password";

        await screen.CreateCommand.ExecuteAsync();

        Assert.Equal("'Work/no-password' has no password to share.", screen.Error);
        Assert.False(screen.IsUnavailable);
        Assert.True(screen.CreateCommand.CanExecute(null));
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task AnEndpointThatDoesNotResolve_KeepsTheFormOff_WithNoRetry()
    {
        using var screen = Screen(unavailable: "Sharing is off: KEYPASTE_SHARE_URL may only name a local development server.");
        screen.SelectedWhat = "env/acme-api/STRIPE_KEY";

        await screen.CreateCommand.ExecuteAsync();

        Assert.True(screen.IsUnavailable);
        Assert.False(screen.CanRetry);
        Assert.False(screen.CreateCommand.CanExecute(null));
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task Revoke_RemovesTheRow()
    {
        using var screen = Screen();
        screen.SelectedWhat = "env/acme-api/STRIPE_KEY";
        await screen.CreateCommand.ExecuteAsync();

        screen.Selected = Assert.Single(screen.Rows);
        await screen.RevokeCommand.ExecuteAsync();

        Assert.Equal("Revoked. The link no longer opens.", _toasts[^1]);
        Assert.Empty(screen.Rows);
        Assert.Empty(_server.Shares);
        Assert.Null(screen.Selected);
    }

    [Fact]
    public async Task RevokeRow_RevokesThatRow()
    {
        using var screen = Screen();
        screen.SelectedWhat = "env/acme-api/STRIPE_KEY";
        await screen.CreateCommand.ExecuteAsync();
        await screen.CreateCommand.ExecuteAsync();
        Assert.Equal(2, screen.Rows.Count);
        var target = screen.Rows[1];

        screen.RevokeRowCommand.Execute(target);
        await Until(() => screen.Rows.Count == 1);

        Assert.DoesNotContain(screen.Rows, row => row.Id == target.Id);
        Assert.False(_server.Shares.ContainsKey(target.Id));
        Assert.Single(_server.Shares);
    }

    [Fact]
    public async Task Revoke_NetworkFailure_KeepsTheRow_AndSaysTheLinkStillOpens()
    {
        using var screen = Screen();
        screen.SelectedWhat = "env/acme-api/STRIPE_KEY";
        await screen.CreateCommand.ExecuteAsync();
        _server.Unreachable = true;

        screen.Selected = Assert.Single(screen.Rows);
        await screen.RevokeCommand.ExecuteAsync();

        Assert.Equal("The share server could not be reached. The link still opens.", screen.Error);
        Assert.Single(screen.Rows);
        Assert.Single(_server.Shares);
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
        Assert.Equal("Opened or revoked", row.Status);
        Assert.Equal(ShareStatusTone.Muted, row.StatusTone);
        Assert.Equal("Remove", row.RevokeLabel);
    }

    [Fact]
    public async Task Refresh_APartlyUsedLink_SaysHowManyViewsWereUsed()
    {
        using var screen = Screen();
        screen.SelectedWhat = "env/acme-api/STRIPE_KEY";
        screen.Views = 3;
        await screen.CreateCommand.ExecuteAsync();
        var id = Assert.Single(screen.Rows).Id;
        _server.Shares[id] = _server.Shares[id] with { ViewsLeft = 2 };

        await screen.RefreshCommand.ExecuteAsync();

        var row = Assert.Single(screen.Rows);
        Assert.Equal("1 of 3 views used", row.Status);
        Assert.Equal(ShareStatusTone.Accent, row.StatusTone);
        Assert.Equal("Revoke", row.RevokeLabel);
    }

    [Fact]
    public async Task Opening_TheScreen_AsksTheServerNothing()
    {
        using (var first = Screen())
        {
            first.SelectedWhat = "env/acme-api/STRIPE_KEY";
            await first.CreateCommand.ExecuteAsync();
        }

        var asked = _server.Requests.Count;
        using var screen = Screen();
        await Until(() => screen.Rows.Count == 1);

        Assert.Equal(asked, _server.Requests.Count);
        Assert.Equal("Not checked", screen.Rows[0].Status);
        Assert.Equal(ShareStatusTone.Muted, screen.Rows[0].StatusTone);
        Assert.True(screen.HasUnchecked);
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
        Assert.Null(screen.Error);
        Assert.True(screen.Passphrase.IsZeroed);
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }
}
