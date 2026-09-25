using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Keypaste.App.Tests;

/// <summary>
/// <c>/api/share</c> in memory, answering as <c>site/src/share.js</c> does, and recording every
/// request whole so a test can search them for a key or a value.
/// </summary>
internal sealed class FakeShareServer : HttpMessageHandler
{
    internal sealed record Seen(HttpMethod Method, Uri Uri, string Headers, string Body);

    internal sealed record Share(string Envelope, int ViewsLeft, DateTimeOffset ExpiresAt, string RevokeSha256);

    internal List<Seen> Requests { get; } = [];

    internal Dictionary<string, Share> Shares { get; } = new(StringComparer.Ordinal);

    /// <summary>The server's clock.</summary>
    internal DateTimeOffset Now { get; set; } = new(2026, 9, 24, 14, 40, 0, TimeSpan.Zero);

    /// <summary>Every request fails as a network failure would.</summary>
    internal bool Unreachable { get; set; }

    /// <summary>Answers instead of the store when it returns a response.</summary>
    internal Func<HttpRequestMessage, HttpResponseMessage?>? Answer { get; set; }

    /// <summary>Runs as each request arrives, before it is answered.</summary>
    internal Action<Seen>? OnRequest { get; set; }

    /// <summary>The text of every request, for a scan.</summary>
    internal string Transcript => string.Join("\n", Requests.Select(r => $"{r.Method} {r.Uri}\n{r.Headers}\n{r.Body}"));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var headers = request.Headers.ToString() + (request.Content?.Headers.ToString() ?? string.Empty);
        var seen = new Seen(request.Method, request.RequestUri!, headers, body);
        Requests.Add(seen);
        OnRequest?.Invoke(seen);

        if (Unreachable)
        {
            throw new HttpRequestException("connection refused");
        }

        if (Answer?.Invoke(request) is { } answer)
        {
            return answer;
        }

        var path = request.RequestUri!.AbsolutePath;
        if (path == "/api/share" && request.Method == HttpMethod.Post)
        {
            return Create(body);
        }

        var parts = path["/api/share/".Length..].Split('/');
        var id = parts[0];
        var live = Shares.TryGetValue(id, out var share) && share.ViewsLeft > 0 && share.ExpiresAt > Now;

        if (request.Method == HttpMethod.Get)
        {
            return live
                ? Json(HttpStatusCode.OK, $"{{\"views_left\":{share!.ViewsLeft},\"expires_at\":\"{share.ExpiresAt:O}\",\"kdf\":null,\"check_iv\":\"x\",\"check\":\"y\"}}")
                : NotFound();
        }

        if (request.Method == HttpMethod.Delete)
        {
            var token = request.Headers.Authorization?.Parameter ?? string.Empty;
            if (share is null || share.RevokeSha256 != Sha256(token))
            {
                return NotFound();
            }

            Shares.Remove(id);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        return NotFound();
    }

    /// <summary>Spends every view of a share, as its recipient opening it would.</summary>
    internal void OpenAll(string id) => Shares.Remove(id);

    internal static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    internal static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private HttpResponseMessage Create(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var id = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        var expires = Now.AddSeconds(root.GetProperty("ttl_seconds").GetInt32());

        Shares[id] = new Share(
            root.GetProperty("envelope").GetRawText(),
            root.GetProperty("views").GetInt32(),
            expires,
            root.GetProperty("revoke_sha256").GetString()!);

        return Json(HttpStatusCode.Created, $"{{\"id\":\"{id}\",\"expires_at\":\"{expires.UtcDateTime:yyyy-MM-dd'T'HH:mm:ss.fff'Z'}\"}}");
    }

    private static HttpResponseMessage NotFound() => Json(HttpStatusCode.NotFound, "{\"error\":\"not found\"}");
}
