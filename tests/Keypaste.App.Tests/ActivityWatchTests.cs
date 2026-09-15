using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Keypaste.App.Session;
using Xunit;

namespace Keypaste.App.Tests;

/// <summary>
/// What counts as a person at the app, and so moves the idle deadline.
/// </summary>
/// <remarks>
/// F.13: on Windows a restore raises a pointer move at the resting cursor, and that move used to
/// postpone the idle lock with nobody at the keyboard.
/// </remarks>
public sealed class ActivityWatchTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromMinutes(5);
    private static readonly Point _resting = new(120, 80);

    [Fact]
    public Task A_move_at_the_resting_pointer_after_a_restore_is_not_activity() => Started(app =>
    {
        app.Window.MouseMove(_resting);
        app.Clock.Advance(TimeSpan.FromMinutes(4));

        app.MoveTo(WindowState.Minimized);
        app.MoveTo(WindowState.Normal);
        app.Window.MouseMove(_resting);

        Assert.Equal(2, app.Moves);

        app.Clock.Advance(TimeSpan.FromMinutes(1));

        Assert.False(app.Session.IsUnlocked);
        Assert.Equal(VaultLockReason.Idle, app.Reason);
    });

    [Fact]
    public Task Ordinary_pointer_movement_still_defers_the_lock() => Started(app =>
    {
        app.Window.MouseMove(_resting);
        app.Clock.Advance(TimeSpan.FromMinutes(4));
        app.Window.MouseMove(_resting + new Point(30, 12));
        app.Clock.Advance(TimeSpan.FromMinutes(4));

        Assert.True(app.Session.IsUnlocked);

        app.Clock.Advance(TimeSpan.FromMinutes(1));

        Assert.False(app.Session.IsUnlocked);
    });

    [Fact]
    public Task Moving_the_pointer_after_a_restore_is_activity() => Started(app =>
    {
        app.Window.MouseMove(_resting);
        app.Clock.Advance(TimeSpan.FromMinutes(4));

        app.MoveTo(WindowState.Minimized);
        app.MoveTo(WindowState.Normal);
        app.Window.MouseMove(_resting);
        app.Window.MouseMove(_resting + new Point(0, 40));
        app.Clock.Advance(TimeSpan.FromMinutes(4));

        Assert.True(app.Session.IsUnlocked);
    });

    [Fact]
    public Task A_click_counts_and_clears_the_countdown() => Started(app =>
    {
        app.Clock.Advance(TimeSpan.FromMinutes(4));
        app.Window.MouseDown(_resting, MouseButton.Left);
        app.Window.MouseUp(_resting, MouseButton.Left);
        app.Clock.Advance(TimeSpan.FromMinutes(4));

        Assert.True(app.Session.IsUnlocked);
        Assert.True(app.Activity > 0);
    });

    [Fact]
    public Task A_key_counts() => Started(app =>
    {
        app.Clock.Advance(TimeSpan.FromMinutes(4));
        app.Window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
        app.Clock.Advance(TimeSpan.FromMinutes(4));

        Assert.True(app.Session.IsUnlocked);
    });

    private static Task Started(Action<Armed> body) => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        using var app = new Armed(fixture);
        body(app);
    });

    /// <summary>A shown window, an unlocked session and the watch launch builds between them.</summary>
    private sealed class Armed : IDisposable
    {
        private readonly ActivityWatch _watch;

        internal Armed(TempVault fixture)
        {
            Session = new AppVaultSession(Clock, _timeout);
            Session.Locked += (_, reason) => Reason = reason;

            using (var master = TempVault.Secret(TempVault.Password))
            {
                Assert.Equal(UnlockOutcome.Opened, Session.TryUnlock(fixture.Path_, master.Value));
            }

            Window = new Window { Width = 400, Height = 300 };
            Window.AddHandler(InputElement.PointerMovedEvent, (_, _) => Moves++, RoutingStrategies.Tunnel, handledEventsToo: true);
            Window.Show();

            _watch = App.Observe(Window, Session, Clock, () => Activity++);
        }

        internal ManualClock Clock { get; } = new();

        internal AppVaultSession Session { get; }

        internal Window Window { get; }

        internal VaultLockReason? Reason { get; private set; }

        internal int Moves { get; private set; }

        internal int Activity { get; private set; }

        internal void MoveTo(WindowState state)
        {
            Window.WindowState = state;
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            _watch.Dispose();
            Session.Dispose();
            Window.Close();
        }
    }
}
