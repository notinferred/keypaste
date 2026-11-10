using Keypaste.Core.Ownership;
using Xunit;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// A start on macOS waits for an awake display before Avalonia initializes, driven through <c>Program.Run</c> with a
/// stand-in for the login session; the CoreVideo call and the process's signals in <see cref="MacSession"/> run on a real Mac only.
/// </summary>
public sealed class DisplayWaitTests
{
    private const string _renderTimerFailure = "Avalonia.Native was not able to start the RenderTimer. Native error code is: -6661";

    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _poll = TimeSpan.FromMilliseconds(1);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void A_start_with_a_display_awake_starts_without_waiting()
    {
        using var home = new TempHome();
        using var stderr = new StringWriter();
        var session = new FakeSession(looksAsleep: 0);
        var started = false;

        var code = Start(session, home, stderr, _ => { started = true; return 7; });

        Assert.Equal(7, code);
        Assert.True(started);
        Assert.Equal(1, session.Looks);
        Assert.False(session.Listened);
        Assert.Empty(stderr.ToString());
    }

    [Theory]
    [InlineData("--background")]
    [InlineData(null)]
    public void A_start_with_every_display_asleep_starts_once_one_wakes(string? flag)
    {
        using var home = new TempHome();
        using var stderr = new StringWriter();
        var session = new FakeSession(looksAsleep: 3);
        var looksWhenStarted = 0;

        var code = Program.Run(
            flag is null ? [] : [flag],
            home.Path,
            null,
            new DisplayWait(session, stderr, _poll).Before(_ => { looksWhenStarted = session.Looks; return 7; }),
            TextWriter.Null,
            _wait);

        Assert.Equal(7, code);
        Assert.Equal(4, looksWhenStarted);
        Assert.Contains("display", stderr.ToString(), StringComparison.Ordinal);
        Assert.True(session.StoppedListening);
    }

    [Fact]
    public async Task A_session_ending_during_the_wait_ends_the_start_cleanly_and_releases_the_home()
    {
        using var home = new TempHome();
        using var stderr = new StringWriter();
        var session = new FakeSession(looksAsleep: int.MaxValue);
        var started = false;

        var running = Task.Run(() => Start(session, home, stderr, _ => { started = true; return 7; }), Token);
        var ending = await session.Listening.WaitAsync(_wait, Token);
        ending();

        Assert.Equal(0, await running.WaitAsync(_wait, Token));
        Assert.False(started);
        Assert.True(session.StoppedListening);
        Assert.True(AppClaim.TryAcquire(home.Path, out var claim), "the start kept the home after it ended");
        claim.Dispose();
    }

    [Fact]
    public void A_render_timer_that_still_fails_ends_the_start_with_a_message_not_a_crash()
    {
        using var home = new TempHome();
        using var stderr = new StringWriter();

        var code = Start(new FakeSession(looksAsleep: 0), home, stderr, _ => throw new InvalidOperationException(_renderTimerFailure));

        Assert.Equal(1, code);
        Assert.Contains("open it again", stderr.ToString(), StringComparison.Ordinal);
        Assert.True(AppClaim.TryAcquire(home.Path, out var claim), "the start kept the home after it ended");
        claim.Dispose();
    }

    [Fact]
    public void Any_other_failure_stays_a_failure()
    {
        using var home = new TempHome();

        Assert.Throws<InvalidOperationException>(
            () => Start(new FakeSession(looksAsleep: 0), home, TextWriter.Null, _ => throw new InvalidOperationException("something else")));
    }

    [Theory]
    [InlineData("--background", 0)]
    [InlineData(null, 1)]
    public void A_start_the_running_copy_takes_never_looks_at_the_display(string? flag, int expected)
    {
        using var home = new TempHome();
        Assert.True(AppClaim.TryAcquire(home.Path, out var held));
        using var claim = held;
        var session = new FakeSession(looksAsleep: int.MaxValue);

        var code = Program.Run(
            flag is null ? [] : [flag],
            home.Path,
            null,
            new DisplayWait(session, TextWriter.Null, _poll).Before(_ => throw new InvalidOperationException("no second app")),
            TextWriter.Null,
            TimeSpan.FromMilliseconds(100));

        Assert.Equal(expected, code);
        Assert.Equal(0, session.Looks);
    }

    private static int Start(FakeSession session, TempHome home, TextWriter stderr, Func<AppClaim, int> start) =>
        Program.Run([], home.Path, null, new DisplayWait(session, stderr, _poll).Before(start), TextWriter.Null, _wait);

    private sealed class FakeSession(int looksAsleep) : IMacSession
    {
        private readonly TaskCompletionSource<Action> _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _looks;

        public int Looks => Volatile.Read(ref _looks);

        public bool Listened => _listening.Task.IsCompleted;

        public bool StoppedListening { get; private set; }

        public Task<Action> Listening => _listening.Task;

        public bool DisplayIsAwake => Interlocked.Increment(ref _looks) > looksAsleep;

        public IDisposable OnEnding(Action ending)
        {
            _listening.TrySetResult(ending);
            return new Stop(() => StoppedListening = true);
        }

        private sealed class Stop(Action stop) : IDisposable
        {
            public void Dispose() => stop();
        }
    }
}
