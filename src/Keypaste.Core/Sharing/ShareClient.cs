using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Keypaste.Core.Sharing;

/// <summary>Why the share server did not do what was asked.</summary>
public enum ShareFailure
{
    /// <summary>It did.</summary>
    None = 0,

    /// <summary>Sharing is switched off or has no store behind it.</summary>
    Unavailable = 1,

    /// <summary>The server refused the request as malformed, or keypaste refused it before asking.</summary>
    Refused = 2,

    /// <summary>What would be shared is over the size limit.</summary>
    TooLarge = 3,

    /// <summary>Too many requests from this address in the last minute.</summary>
    RateLimited = 4,

    /// <summary>The server could not be reached, or did not answer in time.</summary>
    Network = 5,

    /// <summary>The server answered something keypaste does not understand, including a redirect.</summary>
    Protocol = 6,
}

/// <summary>A share the server accepted.</summary>
/// <param name="Id">Its server-made id.</param>
/// <param name="ExpiresAt">When the server stops serving it.</param>
public sealed record ShareCreated(string Id, DateTimeOffset ExpiresAt);

/// <summary>What the server says about a share, without spending a view.</summary>
/// <param name="Exists">Whether it still opens. False for opened, expired and revoked alike.</param>
/// <param name="ViewsLeft">Views left, when it exists.</param>
/// <param name="ExpiresAt">When it expires, when it exists.</param>
public sealed record ShareStatus(bool Exists, int ViewsLeft, DateTimeOffset? ExpiresAt);

/// <summary>Speaks <c>/api/share</c> on one endpoint. It never sends a key or a plaintext byte.</summary>
/// <remarks>
/// The handler is the caller's, with redirects off: a redirect is answered as
/// <see cref="ShareFailure.Protocol"/> rather than followed, so an envelope and a revoke token
/// only ever reach the origin the person chose.
/// </remarks>
/// <param name="handler">The transport, owned by the caller.</param>
/// <param name="endpoint">The origin to speak to.</param>
public sealed class ShareClient(HttpMessageHandler handler, Uri endpoint)
{
    /// <summary>The largest response body read.</summary>
    internal const int MaximumResponseBytes = 64 * 1024;

    // site/src/share.js sets this on the 404 of a lookup that found no live share, and on no other answer.
    private const string _goneHeader = "x-keypaste-share";
    private const string _goneMark = "gone";

    private readonly HttpMessageHandler _handler = handler ?? throw new ArgumentNullException(nameof(handler));

    /// <summary>The origin this client speaks to.</summary>
    public Uri Endpoint { get; } = endpoint ?? throw new ArgumentNullException(nameof(endpoint));

    /// <summary>How long one exchange may take, from sending the request to reading the last body byte.</summary>
    internal TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>A client for another endpoint over the same transport.</summary>
    public ShareClient WithEndpoint(Uri other) => new(_handler, other) { Timeout = Timeout };

    /// <summary>Uploads an envelope.</summary>
    /// <param name="envelope">The sealed envelope.</param>
    /// <param name="views">How many times it may be opened.</param>
    /// <param name="ttlSeconds">How long it may be opened for.</param>
    /// <param name="revokeSha256Hex">The lowercase hex SHA-256 of the revoke token.</param>
    /// <param name="ct">Cancels the request.</param>
    public async Task<(ShareCreated? Created, ShareFailure Failure, string Message)> CreateAsync(
        string envelope, int views, int ttlSeconds, string revokeSha256Hex, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(revokeSha256Hex);

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WritePropertyName("envelope");
            json.WriteRawValue(envelope);
            json.WriteNumber("views", views);
            json.WriteNumber("ttl_seconds", ttlSeconds);
            json.WriteString("revoke_sha256", revokeSha256Hex);
            json.WriteEndObject();
        }

        using var request = Request(HttpMethod.Post, "/api/share");
        request.Content = new ByteArrayContent(buffer.ToArray());
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var (status, body, failure, _) = await SendAsync(request, ct).ConfigureAwait(false);
        if (failure != ShareFailure.None)
        {
            return (null, failure, Describe(failure));
        }

        if (status != HttpStatusCode.Created)
        {
            var mapped = Map(status);
            return (null, mapped, mapped == ShareFailure.Refused ? Refusal(body) : Describe(mapped));
        }

        if (!TryRead(body, out var root)
            || !TryString(root, "id", out var id) || !ShareLink.IsId(id)
            || !TryTime(root, "expires_at", out var expires))
        {
            return (null, ShareFailure.Protocol, Describe(ShareFailure.Protocol));
        }

        return (new ShareCreated(id, expires), ShareFailure.None, string.Empty);
    }

    /// <summary>Asks whether a share still opens. Spends no view.</summary>
    public async Task<(ShareStatus? Status, ShareFailure Failure)> StatusAsync(string id, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (!ShareLink.IsId(id))
        {
            return (null, ShareFailure.Refused);
        }

        using var request = Request(HttpMethod.Get, "/api/share/" + id);
        var (status, body, failure, gone) = await SendAsync(request, ct).ConfigureAwait(false);

        if (failure != ShareFailure.None)
        {
            return (null, failure);
        }

        if (gone)
        {
            return (new ShareStatus(false, 0, null), ShareFailure.None);
        }

        if (status != HttpStatusCode.OK)
        {
            return (null, Map(status));
        }

        if (!TryRead(body, out var root)
            || !root.TryGetProperty("views_left", out var views) || !views.TryGetInt32(out var left) || left < 0
            || !TryTime(root, "expires_at", out var expires))
        {
            return (null, ShareFailure.Protocol);
        }

        return (new ShareStatus(true, left, expires), ShareFailure.None);
    }

    /// <summary>
    /// Revokes a share. A share the server says it no longer has counts as revoked; any other 404,
    /// such as a switched-off or misrouted API, does not, so the revoke token is kept.
    /// </summary>
    public async Task<ShareFailure> RevokeAsync(string id, string revokeToken, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(revokeToken);

        if (!ShareLink.IsId(id))
        {
            return ShareFailure.Refused;
        }

        using var request = Request(HttpMethod.Delete, "/api/share/" + id);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", revokeToken);

        var (status, _, failure, gone) = await SendAsync(request, ct).ConfigureAwait(false);

        return failure != ShareFailure.None ? failure
            : status == HttpStatusCode.NoContent || gone ? ShareFailure.None
            : Map(status);
    }

    /// <summary>The sentence a front end shows for a failure.</summary>
    public static string Describe(ShareFailure failure) => failure switch
    {
        ShareFailure.None => string.Empty,
        ShareFailure.Unavailable => "keypaste.com is not accepting shares right now",
        ShareFailure.Refused => "the share server refused the request",
        ShareFailure.TooLarge => "what would be shared is too large",
        ShareFailure.RateLimited => "too many share requests from this address; try again in a minute",
        ShareFailure.Network => "the share server could not be reached",
        _ => "the share server answered something keypaste does not understand",
    };

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(Endpoint, path));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("keypaste", UserAgentVersion()));
        return request;
    }

    private async Task<(HttpStatusCode Status, byte[] Body, ShareFailure Failure, bool Gone)> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var client = new HttpClient(_handler, disposeHandler: false) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

        // HttpClient.Timeout stops at the headers under ResponseHeadersRead; this one also bounds the body.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(Timeout);
        var token = deadline.Token;

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);

            if ((int)response.StatusCode is >= 300 and < 400)
            {
                return (response.StatusCode, [], ShareFailure.Protocol, false);
            }

            if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            {
                return (response.StatusCode, [], ShareFailure.Protocol, false);
            }

            var gone = response.StatusCode == HttpStatusCode.NotFound
                && response.Headers.TryGetValues(_goneHeader, out var marks)
                && marks.Contains(_goneMark, StringComparer.Ordinal);

            var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                var body = new byte[MaximumResponseBytes + 1];
                var read = 0;
                int last;
                while (read < body.Length && (last = await stream.ReadAsync(body.AsMemory(read), token).ConfigureAwait(false)) > 0)
                {
                    read += last;
                }

                return read > MaximumResponseBytes
                    ? (response.StatusCode, [], ShareFailure.Protocol, false)
                    : (response.StatusCode, body[..read], ShareFailure.None, gone);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException
                                       || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return (default, [], ShareFailure.Network, false);
        }
    }

    private static ShareFailure Map(HttpStatusCode status) => status switch
    {
        HttpStatusCode.BadRequest or HttpStatusCode.Forbidden => ShareFailure.Refused,
        HttpStatusCode.NotFound or HttpStatusCode.ServiceUnavailable => ShareFailure.Unavailable,
        HttpStatusCode.RequestEntityTooLarge => ShareFailure.TooLarge,
        HttpStatusCode.TooManyRequests => ShareFailure.RateLimited,
        _ => ShareFailure.Protocol,
    };

    private static string Refusal(byte[] body) =>
        TryRead(body, out var root) && TryString(root, "error", out var error)
            ? "the share server refused the request: " + EntryNameSanitizer.SanitizeProse(error, 200).Text
            : Describe(ShareFailure.Refused);

    private static bool TryRead(byte[] body, out JsonElement root)
    {
        root = default;

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            root = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryString(JsonElement root, string name, out string value)
    {
        value = string.Empty;

        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString()!;
        return true;
    }

    private static bool TryTime(JsonElement root, string name, out DateTimeOffset value)
    {
        value = default;

        return TryString(root, name, out var text)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value);
    }

    private static string UserAgentVersion()
    {
        var version = CoreInfo.Version;
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? version : version[..plus];
    }
}
