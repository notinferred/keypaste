using System.Net;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary><c>/api/crash</c> in memory, recording every request whole and answering as the test says.</summary>
internal sealed class FakeCrashServer : HttpMessageHandler
{
    private readonly Lock _gate = new();
    private readonly List<Seen> _requests = [];
    private readonly SemaphoreSlim _arrived = new(0);

    internal sealed record Seen(HttpMethod Method, Uri Uri, string RequestHeaders, string ContentType, byte[] Body);

    internal IReadOnlyList<Seen> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>The answer to each request, by its number from zero; null answers 204.</summary>
    internal Func<int, HttpResponseMessage?>? Answer { get; set; }

    /// <summary>Every request fails as a network failure would.</summary>
    internal bool Unreachable { get; set; }

    /// <summary>Requests are answered only by their cancellation.</summary>
    internal bool Hangs { get; set; }

    /// <summary>The text of every request, for a scan.</summary>
    internal string Transcript =>
        string.Join("\n", Requests.Select(seen => $"{seen.Method} {seen.Uri}\n{seen.RequestHeaders}\n{seen.ContentType}\n{System.Text.Encoding.UTF8.GetString(seen.Body)}"));

    internal async Task NextRequestAsync() =>
        Assert.True(await _arrived.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken), "no request arrived");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? Array.Empty<byte>() : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        int number;

        lock (_gate)
        {
            number = _requests.Count;
            _requests.Add(new Seen(request.Method, request.RequestUri!, request.Headers.ToString(), request.Content?.Headers.ContentType?.ToString() ?? string.Empty, body));
        }

        _arrived.Release();

        if (Unreachable)
        {
            throw new HttpRequestException("connection refused");
        }

        if (Hangs)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        if (Answer?.Invoke(number) is { } answer)
        {
            return answer;
        }

        return Accepted();
    }

    private static HttpResponseMessage Accepted() => new(HttpStatusCode.NoContent);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _arrived.Dispose();
        }

        base.Dispose(disposing);
    }
}
