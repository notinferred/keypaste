using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;

namespace Keypaste.Core.CrashReports;

/// <summary>
/// Posts the outbox's crash reports to keypaste.com's <c>/api/crash</c>, or to a loopback test server, one report a request
/// (D-0560).
/// </summary>
/// <remarks>
/// Nothing in Core starts one: the app does, once the person has chosen to send reports. It sends a report's bytes as the
/// outbox keeps them, with no header but the content type, and follows no redirect. A 204, 400 or 413 takes the report off
/// the outbox; any other answer, a network failure or a request past its time leaves that report and the rest for a later
/// pass, and after a 429 or 503 nothing is sent before its Retry-After, at most a day later. Sending never throws.
/// </remarks>
public sealed class CrashSender
{
    /// <summary>The variable naming a loopback test server in place of keypaste.com.</summary>
    public const string EnvironmentVariable = "KEYPASTE_CRASH_URL";

    /// <summary>The most reports one pass sends.</summary>
    internal const int ReportsPerPass = 10;

    private const string _path = "/api/crash";

    private readonly HttpMessageHandler _handler;
    private readonly Uri _endpoint;
    private readonly CrashOutbox _outbox;
    private readonly TimeProvider _clock;
    private DateTimeOffset _notBefore = DateTimeOffset.MinValue;

    /// <summary>Sends one outbox's reports.</summary>
    /// <param name="handler">The transport, owned by the caller: <see cref="CreateTransport"/>, or a test's fake.</param>
    /// <param name="endpoint">keypaste.com or a loopback origin, from <see cref="TryResolveEndpoint"/>.</param>
    /// <param name="outbox">The reports to send.</param>
    /// <param name="clock">The clock the passes, each request's time and a Retry-After are measured on.</param>
    /// <exception cref="ArgumentException">The endpoint is another host.</exception>
    public CrashSender(HttpMessageHandler handler, Uri endpoint, CrashOutbox outbox, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(outbox);

        if (!IsKeypasteCom(endpoint) && !IsLoopbackOrigin(endpoint))
        {
            throw new ArgumentException("crash reports go only to keypaste.com or a loopback server", nameof(endpoint));
        }

        _handler = handler;
        _endpoint = endpoint;
        _outbox = outbox;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>keypaste.com.</summary>
    public static Uri KeypasteCom { get; } = new("https://keypaste.com");

    /// <summary>How long after the app starts the first pass waits.</summary>
    internal static TimeSpan FirstPass { get; } = TimeSpan.FromSeconds(30);

    /// <summary>How long one pass waits for the next.</summary>
    internal static TimeSpan BetweenPasses { get; } = TimeSpan.FromHours(1);

    /// <summary>How long one request may take, from connecting to its answer's headers.</summary>
    internal static TimeSpan RequestTime { get; } = TimeSpan.FromSeconds(10);

    /// <summary>The longest Retry-After honoured.</summary>
    internal static TimeSpan LongestRetryAfter { get; } = TimeSpan.FromHours(24);

    /// <summary>Picks where reports go: keypaste.com, or the loopback origin <see cref="EnvironmentVariable"/> names.</summary>
    /// <param name="fromEnvironment">The variable's value.</param>
    /// <param name="endpoint">The origin, when this returns true.</param>
    /// <returns>False when the variable names anything but a loopback origin, and then nothing is to be sent.</returns>
    public static bool TryResolveEndpoint(string? fromEnvironment, [NotNullWhen(true)] out Uri? endpoint)
    {
        if (string.IsNullOrEmpty(fromEnvironment))
        {
            endpoint = KeypasteCom;
            return true;
        }

        endpoint = Uri.TryCreate(fromEnvironment, UriKind.Absolute, out var uri) && IsLoopbackOrigin(uri)
            ? new Uri(uri.GetLeftPart(UriPartial.Authority))
            : null;

        return endpoint is not null;
    }

    /// <summary>The transport the app sends over, owned and disposed by the caller.</summary>
    /// <returns>
    /// A handler that follows no redirect, keeps no cookie, decompresses nothing, adds no trace header and gives a connection
    /// 10 seconds.
    /// </returns>
    public static HttpMessageHandler CreateTransport() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.None,
        ActivityHeadersPropagator = null,
        ConnectTimeout = RequestTime,
    };

    /// <summary>Waits <see cref="FirstPass"/>, then sends a pass every <see cref="BetweenPasses"/> until cancelled.</summary>
    /// <param name="ct">Ends it.</param>
    public async Task RunAsync(CancellationToken ct)
    {
        for (var wait = FirstPass; await WaitedAsync(wait, ct).ConfigureAwait(false); wait = BetweenPasses)
        {
            await SendWaitingAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>Sends up to <see cref="ReportsPerPass"/> waiting reports, oldest first, stopping at the first kept.</summary>
    /// <param name="ct">Ends the pass, keeping what was not answered.</param>
    /// <returns>How many keypaste.com accepted.</returns>
    public async Task<int> SendWaitingAsync(CancellationToken ct)
    {
        if (_clock.GetUtcNow() < _notBefore)
        {
            return 0;
        }

        var accepted = 0;

        foreach (var report in _outbox.Waiting().Take(ReportsPerPass))
        {
            var outcome = await PostAsync(report, ct).ConfigureAwait(false);

            if (outcome == Outcome.Kept)
            {
                break;
            }

            _outbox.Remove(report);

            if (outcome == Outcome.Accepted)
            {
                accepted++;
            }
        }

        return accepted;
    }

    private async Task<Outcome> PostAsync(CrashReport report, CancellationToken ct)
    {
        try
        {
            using var timeout = new CancellationTokenSource(RequestTime, _clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            using var invoker = new HttpMessageInvoker(_handler, disposeHandler: false);
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_endpoint, _path));
            request.Content = new ReadOnlyMemoryContent(report.Json);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            using var response = await invoker.SendAsync(request, linked.Token).ConfigureAwait(false);

            switch (response.StatusCode)
            {
                case HttpStatusCode.NoContent:
                    return Outcome.Accepted;
                case HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge:
                    return Outcome.Refused;
                case HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable:
                    WaitAfter(response.Headers.RetryAfter);
                    return Outcome.Kept;
                default:
                    return Outcome.Kept;
            }
        }
        catch (Exception)
        {
            return Outcome.Kept;
        }
    }

    private void WaitAfter(RetryConditionHeaderValue? retryAfter)
    {
        var now = _clock.GetUtcNow();
        var wait = retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - now : TimeSpan.Zero);

        if (wait > TimeSpan.Zero)
        {
            _notBefore = now + (wait < LongestRetryAfter ? wait : LongestRetryAfter);
        }
    }

    private async Task<bool> WaitedAsync(TimeSpan wait, CancellationToken ct)
    {
        try
        {
            await Task.Delay(wait, _clock, ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static bool IsOrigin(Uri uri) =>
        uri.IsAbsoluteUri && uri.UserInfo.Length == 0 && uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0;

    private static bool IsKeypasteCom(Uri uri) =>
        IsOrigin(uri) && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && string.Equals(uri.Host, KeypasteCom.Host, StringComparison.OrdinalIgnoreCase);

    private static bool IsLoopbackOrigin(Uri uri) =>
        IsOrigin(uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && uri.IsLoopback;

    private enum Outcome
    {
        Accepted,
        Refused,
        Kept,
    }
}
