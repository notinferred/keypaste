using System.Net;
using System.Net.Http.Headers;
using Keypaste.Core.CrashReports;
using Xunit;

namespace Keypaste.Core.Tests;

/// <summary>
/// 4.11a: the sender posts each waiting report's exact bytes to keypaste.com or a loopback server and nowhere else, keeps
/// what was not accepted, waits when told to, and never throws or holds anyone up (PRODUCT §4.1, D-0560).
/// </summary>
/// <remarks>
/// The mutation each must fail: a header, a cookie or another host; a report dropped on a failure or kept after a 204, 400
/// or 413; a pass that goes on after a failure or past ten; a Retry-After ignored or followed for more than a day; an
/// exception or a wait past ten seconds from a server that does not answer; a first pass before thirty seconds.
/// </remarks>
public sealed class CrashSenderTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("keypaste-crash-sender-").FullName;
    private readonly ManualClock _clock = new(CrashFixtures.At);
    private readonly FakeCrashServer _server = new();
    private readonly CrashOutbox _outbox;

    public CrashSenderTests() => _outbox = new CrashOutbox(Path.Combine(_directory, "crash-reports"), _clock);

    public void Dispose()
    {
        _server.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private CrashSender Sender => new(_server, CrashSender.KeypasteCom, _outbox, _clock);

    [Fact]
    public async Task A_report_is_posted_as_its_exact_bytes_with_only_a_content_type()
    {
        var report = Queued("agents");

        Assert.Equal(1, await Sender.SendWaitingAsync(Token));

        var request = Assert.Single(_server.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://keypaste.com/api/crash", request.Uri.ToString());
        Assert.Equal(string.Empty, request.RequestHeaders);
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal(report.Json.ToArray(), request.Body);
        Assert.Empty(_outbox.Waiting());
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, false)]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.MovedPermanently, true)]
    [InlineData(HttpStatusCode.Forbidden, true)]
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    public async Task Only_a_204_400_or_413_takes_a_report_off_the_outbox(HttpStatusCode status, bool kept)
    {
        Queued("agents");
        _server.Answer = _ => new HttpResponseMessage(status);

        Assert.Equal(status == HttpStatusCode.NoContent ? 1 : 0, await Sender.SendWaitingAsync(Token));

        Assert.Single(_server.Requests);
        Assert.Equal(kept ? 1 : 0, _outbox.Waiting().Count);
    }

    [Fact]
    public async Task A_failure_ends_the_pass_and_keeps_the_rest()
    {
        Queued("agents");
        Queued("settings");
        Queued("items");
        _server.Answer = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        Assert.Equal(0, await Sender.SendWaitingAsync(Token));

        Assert.Single(_server.Requests);
        Assert.Equal(3, _outbox.Waiting().Count);

        _server.Answer = null;

        Assert.Equal(3, await Sender.SendWaitingAsync(Token));
        Assert.Equal(4, _server.Requests.Count);
        Assert.Empty(_outbox.Waiting());
    }

    [Fact]
    public async Task A_pass_sends_at_most_ten_oldest_first()
    {
        for (var i = 0; i < 12; i++)
        {
            Queued($"screen-{i}");
            _clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(10, await Sender.SendWaitingAsync(Token));

        Assert.Equal(10, _server.Requests.Count);
        Assert.Contains("\"screen\":\"screen-0\"", System.Text.Encoding.UTF8.GetString(_server.Requests[0].Body), StringComparison.Ordinal);
        Assert.Equal(2, _outbox.Waiting().Count);
    }

    [Theory]
    [InlineData(120, 120)]
    [InlineData(3 * 24 * 3600, 24 * 3600)]
    public async Task Nothing_is_sent_before_a_retry_after_of_at_most_a_day(int retryAfter, int honoured)
    {
        Queued("agents");
        var sender = Sender;
        _server.Answer = number => number == 0 ? Busy(TimeSpan.FromSeconds(retryAfter)) : null;

        Assert.Equal(0, await sender.SendWaitingAsync(Token));

        _clock.Advance(TimeSpan.FromSeconds(honoured - 1));
        Assert.Equal(0, await sender.SendWaitingAsync(Token));
        Assert.Single(_server.Requests);

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, await sender.SendWaitingAsync(Token));
        Assert.Equal(2, _server.Requests.Count);
    }

    [Fact]
    public async Task An_unreachable_server_keeps_the_report_without_throwing()
    {
        Queued("agents");
        _server.Unreachable = true;

        Assert.Equal(0, await Sender.SendWaitingAsync(Token));

        Assert.Single(_server.Requests);
        Assert.Single(_outbox.Waiting());
    }

    [Fact]
    public async Task A_server_that_does_not_answer_is_given_ten_seconds()
    {
        Queued("agents");
        _server.Hangs = true;

        var sending = Sender.SendWaitingAsync(Token);
        await _server.NextRequestAsync();

        _clock.Advance(TimeSpan.FromSeconds(9));
        Assert.False(sending.IsCompleted);

        _clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(0, await sending.WaitAsync(TimeSpan.FromSeconds(30), Token));
        Assert.Single(_outbox.Waiting());
    }

    [Fact]
    public async Task The_first_pass_waits_thirty_seconds_and_the_next_an_hour()
    {
        var clock = new HourWatchingClock(_clock);
        var sender = new CrashSender(_server, CrashSender.KeypasteCom, _outbox, clock);
        Queued("agents");

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var running = sender.RunAsync(stop.Token);

        _clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Empty(_server.Requests);

        _clock.Advance(TimeSpan.FromSeconds(1));
        await _server.NextRequestAsync();
        await clock.HourStarted.WaitAsync(TimeSpan.FromSeconds(30), Token);

        Queued("settings");
        _clock.Advance(TimeSpan.FromMinutes(59));
        Assert.Single(_server.Requests);

        _clock.Advance(TimeSpan.FromMinutes(1));
        await _server.NextRequestAsync();
        Assert.Equal(2, _server.Requests.Count);

        await stop.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(30), Token);
        Assert.True(running.IsCompletedSuccessfully);
    }

    [Theory]
    [InlineData(null, "https://keypaste.com/")]
    [InlineData("", "https://keypaste.com/")]
    [InlineData("http://127.0.0.1:8787", "http://127.0.0.1:8787/")]
    [InlineData("http://localhost:8787/", "http://localhost:8787/")]
    [InlineData("https://[::1]:8443", "https://[::1]:8443/")]
    [InlineData("https://example.com", null)]
    [InlineData("https://keypaste.com.example.com", null)]
    [InlineData("http://127.0.0.1:8787/api", null)]
    [InlineData("http://alice@127.0.0.1:8787", null)]
    [InlineData("ftp://127.0.0.1", null)]
    [InlineData("127.0.0.1:8787", null)]
    public void The_variable_may_name_only_a_loopback_origin(string? fromEnvironment, string? expected)
    {
        Assert.Equal(expected is not null, CrashSender.TryResolveEndpoint(fromEnvironment, out var endpoint));
        Assert.Equal(expected, endpoint?.ToString());
    }

    [Theory]
    [InlineData("https://keypaste.com", true)]
    [InlineData("http://127.0.0.1:8787", true)]
    [InlineData("https://example.com", false)]
    [InlineData("http://keypaste.com", false)]
    [InlineData("https://keypaste.com:8443", false)]
    [InlineData("https://keypaste.com/elsewhere", false)]
    public void A_sender_is_made_only_for_keypaste_com_or_a_loopback_server(string endpoint, bool made)
    {
        var uri = new Uri(endpoint);

        if (made)
        {
            Assert.NotNull(new CrashSender(_server, uri, _outbox, _clock));
        }
        else
        {
            Assert.Throws<ArgumentException>(() => new CrashSender(_server, uri, _outbox, _clock));
        }
    }

    private CrashReport Queued(string screen)
    {
        var report = CrashScrubber.Crash(CrashFixtures.Thrown(() => throw new InvalidOperationException()), CrashFixtures.Context with { Screen = screen });
        Assert.True(_outbox.Add(report));
        return report;
    }

    private static HttpResponseMessage Busy(TimeSpan retryAfter)
    {
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter);
        return response;
    }

    /// <summary>The test's clock, saying when something starts waiting an hour on it.</summary>
    private sealed class HourWatchingClock(ManualClock clock) : TimeProvider
    {
        private readonly TaskCompletionSource _hourStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task HourStarted => _hourStarted.Task;

        public override long TimestampFrequency => clock.TimestampFrequency;

        public override DateTimeOffset GetUtcNow() => clock.GetUtcNow();

        public override long GetTimestamp() => clock.GetTimestamp();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = clock.CreateTimer(callback, state, dueTime, period);

            if (dueTime == TimeSpan.FromHours(1))
            {
                _hourStarted.TrySetResult();
            }

            return timer;
        }
    }
}
