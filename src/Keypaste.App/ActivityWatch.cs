using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Keypaste.App.Session;

namespace Keypaste.App;

/// <summary>
/// Watches a window for signs of a person, and asks the session to re-check on activation.
/// </summary>
/// <remarks>
/// <para>
/// Tunnelling, at the top level, with <c>handledEventsToo</c>, so no control can swallow the
/// signal by handling its own input first.
/// </para>
/// <para>
/// <b>Window activation is not activity.</b> It re-checks the deadline instead.
/// Under focus-follows-mouse a stray pointer pass would otherwise hold the vault open forever,
/// and the case that matters — waking a machine that slept through the timeout — is one the
/// timer cannot see, because timers run on a monotonic clock that slept too.
/// </para>
/// <para>
/// <b>A pointer move is activity only when the pointer actually moved.</b> A window restored or
/// shown under a resting cursor is sent a move at that unchanged position, which on Windows used to
/// postpone the idle lock with nobody there (F.13). So a move counts only against a screen position
/// this window already saw, and leaving <see cref="WindowState.Minimized"/> forgets that position.
/// </para>
/// <para>
/// Pointer movement is throttled: it fires at pointer-poll rate, and <c>Touch()</c> is cheap but
/// not free. Everything else calls straight through.
/// </para>
/// </remarks>
internal sealed class ActivityWatch : IDisposable
{
    private static readonly TimeSpan _pointerThrottle = TimeSpan.FromSeconds(5);

    private readonly Window _window;
    private readonly AppVaultSession _session;
    private readonly TimeProvider _clock;
    private readonly Action _onActivity;

    private DateTimeOffset _lastPointerTouch = DateTimeOffset.MinValue;
    private PixelPoint? _lastPointer;
    private bool _disposed;

    /// <summary>Watches a window.</summary>
    /// <param name="window">The window a person uses.</param>
    /// <param name="session">The session activity keeps open.</param>
    /// <param name="clock">The clock the pointer throttle measures against.</param>
    /// <param name="onActivity">What else a keystroke, click or wheel turn does.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    internal ActivityWatch(Window window, AppVaultSession session, TimeProvider clock, Action onActivity)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(onActivity);

        _window = window;
        _session = session;
        _clock = clock;
        _onActivity = onActivity;

        _window.AddHandler(InputElement.KeyDownEvent, OnActivity, RoutingStrategies.Tunnel, handledEventsToo: true);
        _window.AddHandler(InputElement.TextInputEvent, OnActivity, RoutingStrategies.Tunnel, handledEventsToo: true);
        _window.AddHandler(InputElement.PointerPressedEvent, OnActivity, RoutingStrategies.Tunnel, handledEventsToo: true);
        _window.AddHandler(InputElement.PointerWheelChangedEvent, OnActivity, RoutingStrategies.Tunnel, handledEventsToo: true);
        _window.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        _window.Activated += OnActivated;
        _window.PropertyChanged += OnWindowPropertyChanged;
    }

    /// <summary>Stops watching. Doing it twice is not an error.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.RemoveHandler(InputElement.KeyDownEvent, OnActivity);
        _window.RemoveHandler(InputElement.TextInputEvent, OnActivity);
        _window.RemoveHandler(InputElement.PointerPressedEvent, OnActivity);
        _window.RemoveHandler(InputElement.PointerWheelChangedEvent, OnActivity);
        _window.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
        _window.Activated -= OnActivated;
        _window.PropertyChanged -= OnWindowPropertyChanged;
    }

    private void OnActivity(object? sender, RoutedEventArgs e)
    {
        _session.Touch();
        _onActivity();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var at = _window.PointToScreen(e.GetPosition(_window));
        var moved = _lastPointer is { } last && last != at;
        _lastPointer = at;

        var now = _clock.GetUtcNow();

        if (!moved || now - _lastPointerTouch < _pointerThrottle)
        {
            return;
        }

        _lastPointerTouch = now;
        _session.Touch();
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty && e.GetOldValue<WindowState>() is WindowState.Minimized)
        {
            _lastPointer = null;
        }
    }

    private void OnActivated(object? sender, EventArgs e) => _session.Reevaluate();
}
