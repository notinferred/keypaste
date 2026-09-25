using System.Net;
using Keypaste.Core.Sharing;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// Sharing fails closed after the upload: a link is returned only once its record is saved and its
/// audit line written, and any failure on the way withdraws it from the server.
/// </summary>
public sealed class ShareServiceTests : IDisposable
{
    private readonly ShareFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private int ShareRecordsOnDisk()
    {
        using var vault = _fixture.Open();
        return new ShareStore(vault).List().Count;
    }

    [Fact]
    public async Task Create_UploadsStoresAudits_InThatOrder()
    {
        List<string> order = [];
        _fixture.Server.OnRequest = request => order.Add($"{request.Method} {request.Uri.AbsolutePath}");
        _fixture.OnAudit = () => order.Add($"audit with {ShareRecordsOnDisk()} record(s) saved");

        using var vault = _fixture.Open();
        var outcome = await _fixture.Service().CreateAsync(vault, ShareFixture.Request(views: 3, to: "sam@acme.dev"), CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Message);
        Assert.Equal(["POST /api/share", "audit with 1 record(s) saved"], order);

        Assert.True(ShareLink.TryParse(outcome.Link!, out var id, out var key));
        Assert.StartsWith("https://keypaste.com/s/#", outcome.Link, StringComparison.Ordinal);
        Assert.Equal(id, outcome.Info!.Id);
        Assert.Equal("3 views · 24h", outcome.Info.Rule);
        Assert.Equal(_fixture.Server.Now.AddHours(24), outcome.Info.Expires);
        Assert.DoesNotContain(key, outcome.ToString(), StringComparison.Ordinal);

        var envelope = _fixture.Server.Shares[id].Envelope;
        Assert.True(ShareCrypto.TryOpen(envelope, key, ReadOnlySpan<char>.Empty, out var payload, out var error), error);
        Assert.Equal("STRIPE_SECRET_KEY", payload.Title);
        Assert.Equal(ShareFixture.StripeValue, Assert.Single(payload.Fields).Value);

        var audit = _fixture.AuditText();
        Assert.Contains("\"method\":\"share-created\"", audit, StringComparison.Ordinal);
        Assert.Contains("\"decision\":\"granted\"", audit, StringComparison.Ordinal);
        Assert.Contains("\"tool\":\"share\"", audit, StringComparison.Ordinal);
        Assert.Contains($"share {id}: 3 views, expires 2026-09-25T14:40:00Z, to sam@acme.dev", audit, StringComparison.Ordinal);
        Assert.Contains("env/acme-api/STRIPE_SECRET_KEY", audit, StringComparison.Ordinal);
        Assert.DoesNotContain(key, audit, StringComparison.Ordinal);
        Assert.DoesNotContain(ShareFixture.StripeValue, audit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_Login_SharesTheNonEmptyLoginFields()
    {
        using var vault = _fixture.Open();
        var outcome = await _fixture.Service().CreateAsync(vault, ShareFixture.Request(ShareFixture.Chase, "login", passphrase: "a long passphrase"), CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Message);
        Assert.True(ShareLink.TryParse(outcome.Link!, out var id, out var key));
        Assert.True(ShareCrypto.TryOpen(_fixture.Server.Shares[id].Envelope, key, "a long passphrase", out var payload, out _));
        Assert.Equal(["username", "password", "url"], payload.Fields.Select(f => f.Name));
        Assert.Contains("passphrase", _fixture.AuditText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_ServerRefusal_StoresNothing()
    {
        _fixture.Server.Answer = request => request.Method == HttpMethod.Post
            ? FakeShareServer.Json(HttpStatusCode.ServiceUnavailable, "{\"error\":\"sharing is not available\"}")
            : null;

        using var vault = _fixture.Open();
        var outcome = await _fixture.Service().CreateAsync(vault, ShareFixture.Request(), CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Null(outcome.Link);
        Assert.Equal(ShareFailure.Unavailable, outcome.Failure);
        Assert.Equal("keypaste.com is not accepting shares right now", outcome.Message);
        Assert.Empty(new ShareStore(vault).List());
        Assert.Equal(0, ShareRecordsOnDisk());
        Assert.Equal(string.Empty, _fixture.AuditText());
    }

    [Fact]
    public async Task Create_SaveFailure_WithdrawsTheLink()
    {
        _fixture.Server.OnRequest = request =>
        {
            if (request.Method != HttpMethod.Post)
            {
                return;
            }

            using var other = _fixture.Open();
            other.AddEntry(new VaultEntry { Title = "written elsewhere meanwhile" });
            other.Save();
        };

        using var vault = _fixture.Open();
        var outcome = await _fixture.Service().CreateAsync(vault, ShareFixture.Request(), CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Null(outcome.Link);
        Assert.Contains("the link was withdrawn", outcome.Message, StringComparison.Ordinal);
        Assert.Equal([HttpMethod.Post, HttpMethod.Delete], _fixture.Server.Requests.Select(r => r.Method));
        Assert.Empty(_fixture.Server.Shares);
        Assert.Empty(new ShareStore(vault).List());
        Assert.Equal(0, ShareRecordsOnDisk());
        Assert.Equal(string.Empty, _fixture.AuditText());
    }

    [Fact]
    public async Task Create_VaultLockedDuringUpload_WithdrawsTheLink()
    {
        using var vault = _fixture.Open();
        _fixture.Server.OnRequest = request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                vault.Dispose();
            }
        };

        var outcome = await _fixture.Service().CreateAsync(vault, ShareFixture.Request(), CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Contains("the link was withdrawn", outcome.Message, StringComparison.Ordinal);
        Assert.Empty(_fixture.Server.Shares);
        Assert.Equal(0, ShareRecordsOnDisk());
    }

    [Fact]
    public async Task Create_AuditFailure_WithdrawsAndForgets()
    {
        _fixture.AuditBroken = true;

        using var vault = _fixture.Open();
        var outcome = await _fixture.Service().CreateAsync(vault, ShareFixture.Request(), CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Null(outcome.Link);
        Assert.Equal("the audit log could not be written, so the link was withdrawn", outcome.Message);
        Assert.Equal([HttpMethod.Post, HttpMethod.Delete], _fixture.Server.Requests.Select(r => r.Method));
        Assert.Empty(_fixture.Server.Shares);
        Assert.Equal(0, ShareRecordsOnDisk());
    }

    [Fact]
    public async Task Create_ReservedEntry_IsRefused()
    {
        using var vault = _fixture.Open();
        var outcome = await _fixture.Service().CreateAsync(vault, ShareFixture.Request(new EntryName(ReservedGroups.Tokens, "abc")), CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Equal("keypaste's own records cannot be shared", outcome.Message);
        Assert.Empty(_fixture.Server.Requests);

        outcome = await _fixture.Service().CreateAsync(vault, ShareFixture.Request(new EntryName(".KEYPASTE/shares", "x")), CancellationToken.None);
        Assert.False(outcome.Ok);
        Assert.Empty(_fixture.Server.Requests);
    }

    [Fact]
    public async Task Create_ChangedOnDisk_IsRefused()
    {
        using var vault = _fixture.Open();
        using (var other = _fixture.Open())
        {
            other.AddEntry(new VaultEntry { Title = "written elsewhere" });
            other.Save();
        }

        var outcome = await _fixture.Service().CreateAsync(vault, ShareFixture.Request(), CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Contains("changed on disk", outcome.Message, StringComparison.Ordinal);
        Assert.Empty(_fixture.Server.Requests);
    }

    [Theory]
    [InlineData("secret", 1, 24, null, "not a field")]
    [InlineData("password", 0, 24, null, "1 to 10")]
    [InlineData("password", 11, 24, null, "1 to 10")]
    [InlineData("password", 1, 0, null, "5 minutes to 7 days")]
    [InlineData("password", 1, 24 * 8, null, "5 minutes to 7 days")]
    [InlineData("password", 1, 24, "short", "at least 8")]
    [InlineData("password", 1, 24, "café ✓ long enough", "Latin")]
    public async Task Create_RefusesLimitsOutsideTheRules_BeforeAsking(string field, int views, int hours, string? passphrase, string expected)
    {
        using var vault = _fixture.Open();
        var request = ShareFixture.Request(field: field, views: views, passphrase: passphrase) with { Ttl = TimeSpan.FromHours(hours) };

        var outcome = await _fixture.Service().CreateAsync(vault, request, CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Contains(expected, outcome.Message, StringComparison.Ordinal);
        Assert.Empty(_fixture.Server.Requests);
    }

    [Fact]
    public async Task Create_AnEmptyField_IsRefused()
    {
        using var vault = _fixture.Open();
        var outcome = await _fixture.Service().CreateAsync(vault, ShareFixture.Request(field: "notes"), CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Contains("has no notes", outcome.Message, StringComparison.Ordinal);
        Assert.Empty(_fixture.Server.Requests);
    }

    [Fact]
    public async Task Create_OnAnotherEndpoint_RecordsIt()
    {
        using var vault = _fixture.Open();
        var outcome = await _fixture.Service(new Uri("https://share.example.org")).CreateAsync(vault, ShareFixture.Request(), CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Message);
        Assert.StartsWith("https://share.example.org/s/#", outcome.Link, StringComparison.Ordinal);
        Assert.Equal("https://share.example.org", outcome.Info!.Endpoint);
        Assert.Equal("https://share.example.org/api/share", _fixture.Server.Requests[0].Uri.ToString());
    }

    [Fact]
    public async Task Revoke_ForgetsAndAudits_OnTheEndpointItWasMadeOn()
    {
        using var vault = _fixture.Open();
        var created = await _fixture.Service(new Uri("https://share.example.org")).CreateAsync(vault, ShareFixture.Request(), CancellationToken.None);

        var outcome = await _fixture.Service().RevokeAsync(vault, created.Info!.Id[..8], CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Message);
        Assert.Equal($"https://share.example.org/api/share/{created.Info.Id}", _fixture.Server.Requests[^1].Uri.ToString());
        Assert.Empty(_fixture.Server.Shares);
        Assert.Equal(0, ShareRecordsOnDisk());
        Assert.Contains($"share {created.Info.Id} revoked", _fixture.AuditText(), StringComparison.Ordinal);
        Assert.Contains("\"method\":\"share-revoked\"", _fixture.AuditText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revoke_404_StillForgets()
    {
        using var vault = _fixture.Open();
        var created = await _fixture.Service().CreateAsync(vault, ShareFixture.Request(), CancellationToken.None);
        _fixture.Server.OpenAll(created.Info!.Id);

        var outcome = await _fixture.Service().RevokeAsync(vault, created.Info.Id, CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Message);
        Assert.Equal(0, ShareRecordsOnDisk());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Revoke_WhileSharingIsSwitchedOff_KeepsTheRevokeToken(HttpStatusCode answer)
    {
        using var vault = _fixture.Open();
        var created = await _fixture.Service().CreateAsync(vault, ShareFixture.Request(), CancellationToken.None);
        _fixture.Server.Answer = _ => FakeShareServer.Json(answer, "{\"error\":\"sharing is not available\"}");

        var outcome = await _fixture.Service().RevokeAsync(vault, created.Info!.Id, CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Equal(ShareFailure.Unavailable, outcome.Failure);
        Assert.Equal(1, ShareRecordsOnDisk());
        Assert.DoesNotContain("share-revoked", _fixture.AuditText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revoke_NetworkFailure_Keeps()
    {
        using var vault = _fixture.Open();
        var created = await _fixture.Service().CreateAsync(vault, ShareFixture.Request(), CancellationToken.None);
        _fixture.Server.Unreachable = true;

        var outcome = await _fixture.Service().RevokeAsync(vault, created.Info!.Id, CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Equal(ShareFailure.Network, outcome.Failure);
        Assert.Equal(1, ShareRecordsOnDisk());
        Assert.DoesNotContain("share-revoked", _fixture.AuditText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revoke_UnknownOrAmbiguousPrefix_AsksNothing()
    {
        using var vault = _fixture.Open();

        var outcome = await _fixture.Service().RevokeAsync(vault, "nothing", CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Contains("no share starts with", outcome.Message, StringComparison.Ordinal);
        Assert.Empty(_fixture.Server.Requests);
    }

    [Fact]
    public async Task List_Statuses()
    {
        using var vault = _fixture.Open();
        var service = _fixture.Service();
        var opened = await service.CreateAsync(vault, ShareFixture.Request(), CancellationToken.None);
        var live = await service.CreateAsync(vault, ShareFixture.Request(views: 3), CancellationToken.None);
        var shortLived = await service.CreateAsync(vault, ShareFixture.Request() with { Ttl = TimeSpan.FromHours(1) }, CancellationToken.None);
        _fixture.Server.OpenAll(opened.Info!.Id);
        _fixture.Clock.Advance(TimeSpan.FromHours(2));
        _fixture.Server.Now = _fixture.Server.Now.AddHours(2);

        var online = await service.ListAsync(vault, online: true, CancellationToken.None);
        var byId = online.ToDictionary(row => row.Info.Id);

        Assert.Equal("gone", byId[opened.Info.Id].Status);
        Assert.Equal("3 views left", byId[live.Info!.Id].Status);
        Assert.Equal(3, byId[live.Info.Id].ViewsLeft);
        Assert.Equal("expired", byId[shortLived.Info!.Id].Status);

        var offline = await service.ListAsync(vault, online: false, CancellationToken.None);
        Assert.Equal("unknown", offline.Single(row => row.Info.Id == live.Info.Id).Status);
        Assert.Equal("expired", offline.Single(row => row.Info.Id == shortLived.Info.Id).Status);

        _fixture.Server.Unreachable = true;
        var unreachable = await service.ListAsync(vault, online: true, CancellationToken.None);
        Assert.Equal("unknown", unreachable.Single(row => row.Info.Id == live.Info.Id).Status);
    }
}
