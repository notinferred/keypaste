using System.Net;
using Keypaste.Core.Sharing;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>What <c>/api/share</c> is sent, and what each answer means.</summary>
public sealed class ShareClientTests : IDisposable
{
    private const string _id = "Qm9vYmFyQm9vYmFyQm9vYg";
    private static readonly string _hash = new('a', 64);

    private readonly FakeShareServer _server = new();

    private ShareClient Client => new(_server, ShareEndpoint.Default);

    public void Dispose() => _server.Dispose();

    private static string Envelope() => ShareCrypto.Seal(
        new SharePayload("t", [new ShareField("password", "the-value")], DateTimeOffset.UnixEpoch),
        ReadOnlySpan<char>.Empty).Envelope;

    [Fact]
    public async Task Create_PostsTheEnvelopeAndLimits_AsJson()
    {
        var envelope = Envelope();

        var (created, failure, _) = await Client.CreateAsync(envelope, 3, 3600, _hash, CancellationToken.None);

        Assert.Equal(ShareFailure.None, failure);
        Assert.NotNull(created);
        Assert.True(ShareLink.IsId(created.Id));
        Assert.Equal(_server.Now.AddHours(1), created.ExpiresAt);

        var request = Assert.Single(_server.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://keypaste.com/api/share", request.Uri.ToString());
        Assert.Contains("Content-Type: application/json", request.Headers, StringComparison.Ordinal);
        Assert.Contains("User-Agent: keypaste/", request.Headers, StringComparison.Ordinal);
        Assert.Contains("\"envelope\":" + envelope, request.Body, StringComparison.Ordinal);
        Assert.Contains("\"views\":3", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"ttl_seconds\":3600", request.Body, StringComparison.Ordinal);
        Assert.Contains($"\"revoke_sha256\":\"{_hash}\"", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Origin", request.Headers, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheKeyNeverAppearsInAnyRequest()
    {
        var sealedShare = ShareCrypto.Seal(
            new SharePayload("t", [new ShareField("password", "the-value")], DateTimeOffset.UnixEpoch),
            "a passphrase");
        const string token = "revoke-token";

        var (created, _, _) = await Client.CreateAsync(sealedShare.Envelope, 1, 300, FakeShareServer.Sha256(token), CancellationToken.None);
        await Client.StatusAsync(created!.Id, CancellationToken.None);
        await Client.RevokeAsync(created.Id, token, CancellationToken.None);

        Assert.Equal(3, _server.Requests.Count);
        Assert.DoesNotContain(sealedShare.Key, _server.Transcript, StringComparison.Ordinal);
        Assert.DoesNotContain("the-value", _server.Transcript, StringComparison.Ordinal);
        Assert.DoesNotContain("a passphrase", _server.Transcript, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, ShareFailure.Refused)]
    [InlineData(HttpStatusCode.Forbidden, ShareFailure.Refused)]
    [InlineData(HttpStatusCode.NotFound, ShareFailure.Unavailable)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, ShareFailure.TooLarge)]
    [InlineData(HttpStatusCode.TooManyRequests, ShareFailure.RateLimited)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ShareFailure.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, ShareFailure.Protocol)]
    [InlineData(HttpStatusCode.OK, ShareFailure.Protocol)]
    public async Task Create_MapsEachAnswer(HttpStatusCode status, ShareFailure expected)
    {
        _server.Answer = _ => FakeShareServer.Json(status, "{\"error\":\"views must be 1 to 10\"}");

        var (created, failure, message) = await Client.CreateAsync(Envelope(), 1, 300, _hash, CancellationToken.None);

        Assert.Null(created);
        Assert.Equal(expected, failure);
        Assert.False(string.IsNullOrEmpty(message));
        if (expected == ShareFailure.Unavailable)
        {
            Assert.Equal("keypaste.com is not accepting shares right now", message);
        }

        if (status == HttpStatusCode.BadRequest)
        {
            Assert.Contains("views must be 1 to 10", message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Create_RefusesAnIdThatIsNotOneTheServerMakes()
    {
        _server.Answer = _ => FakeShareServer.Json(HttpStatusCode.Created, "{\"id\":\"../../etc\",\"expires_at\":\"2026-09-25T14:40:00Z\"}");

        var (created, failure, _) = await Client.CreateAsync(Envelope(), 1, 300, _hash, CancellationToken.None);

        Assert.Null(created);
        Assert.Equal(ShareFailure.Protocol, failure);
    }

    [Fact]
    public async Task ARedirect_IsNotFollowed()
    {
        _server.Answer = _ =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
            redirect.Headers.Location = new Uri("https://evil.example/api/share");
            return redirect;
        };

        var (created, failure, _) = await Client.CreateAsync(Envelope(), 1, 300, _hash, CancellationToken.None);

        Assert.Null(created);
        Assert.Equal(ShareFailure.Protocol, failure);
        Assert.Single(_server.Requests);
        Assert.Equal(ShareFailure.Protocol, await Client.RevokeAsync(_id, "token", CancellationToken.None));
        Assert.Equal(2, _server.Requests.Count);
    }

    [Fact]
    public async Task AnOversizedBody_IsRefused()
    {
        var huge = "{\"id\":\"" + _id + "\",\"expires_at\":\"2026-09-25T14:40:00Z\",\"pad\":\"" + new string('x', ShareClient.MaximumResponseBytes) + "\"}";
        _server.Answer = _ => FakeShareServer.Json(HttpStatusCode.Created, huge);

        var (created, failure, _) = await Client.CreateAsync(Envelope(), 1, 300, _hash, CancellationToken.None);

        Assert.Null(created);
        Assert.Equal(ShareFailure.Protocol, failure);
    }

    [Fact]
    public async Task ANetworkFailure_IsNetwork()
    {
        _server.Unreachable = true;

        Assert.Equal(ShareFailure.Network, (await Client.CreateAsync(Envelope(), 1, 300, _hash, CancellationToken.None)).Failure);
        Assert.Equal(ShareFailure.Network, (await Client.StatusAsync(_id, CancellationToken.None)).Failure);
        Assert.Equal(ShareFailure.Network, await Client.RevokeAsync(_id, "token", CancellationToken.None));
    }

    [Fact]
    public async Task ABodyThatNeverEnds_IsNetwork_WithinTheTimeout()
    {
        _server.Answer = _ => new HttpResponseMessage(HttpStatusCode.Created) { Content = new StreamContent(new StallingStream()) };
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var failure = await new ShareClient(_server, ShareEndpoint.Default) { Timeout = TimeSpan.FromMilliseconds(200) }
            .RevokeAsync(_id, "token", CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.Equal(ShareFailure.Network, failure);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task Status_ReadsViewsLeft_AndNotFoundMeansGone()
    {
        const string token = "revoke-token";
        var (created, _, _) = await Client.CreateAsync(Envelope(), 3, 3600, FakeShareServer.Sha256(token), CancellationToken.None);

        var (status, failure) = await Client.StatusAsync(created!.Id, CancellationToken.None);
        Assert.Equal(ShareFailure.None, failure);
        Assert.True(status!.Exists);
        Assert.Equal(3, status.ViewsLeft);
        Assert.Equal(HttpMethod.Get, _server.Requests[^1].Method);
        Assert.Equal($"https://keypaste.com/api/share/{created.Id}", _server.Requests[^1].Uri.ToString());

        _server.OpenAll(created.Id);
        (status, failure) = await Client.StatusAsync(created.Id, CancellationToken.None);
        Assert.Equal(ShareFailure.None, failure);
        Assert.False(status!.Exists);
    }

    [Fact]
    public async Task Revoke_SendsTheTokenAsABearer_And204Or404AreBothDone()
    {
        const string token = "revoke-token";
        var (created, _, _) = await Client.CreateAsync(Envelope(), 1, 300, FakeShareServer.Sha256(token), CancellationToken.None);

        Assert.Equal(ShareFailure.None, await Client.RevokeAsync(created!.Id, token, CancellationToken.None));
        Assert.Equal(HttpMethod.Delete, _server.Requests[^1].Method);
        Assert.Contains($"Authorization: Bearer {token}", _server.Requests[^1].Headers, StringComparison.Ordinal);
        Assert.Empty(_server.Shares);

        Assert.Equal(ShareFailure.None, await Client.RevokeAsync(created.Id, token, CancellationToken.None));
    }

    [Fact]
    public async Task AMalformedId_IsNeverSent()
    {
        Assert.Equal(ShareFailure.Refused, (await Client.StatusAsync("../../x", CancellationToken.None)).Failure);
        Assert.Equal(ShareFailure.Refused, await Client.RevokeAsync("../../x", "t", CancellationToken.None));
        Assert.Empty(_server.Requests);
    }

    /// <summary>A response body whose first byte never arrives.</summary>
    private sealed class StallingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
