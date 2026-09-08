using Avalonia;
using Avalonia.Controls;

namespace Keypaste.App;

/// <summary>
/// Turns a native minimize into a lock, when the person asked for that.
/// </summary>
/// <remarks>
/// <para>
/// <b>It locks by calling the same method Ctrl/Cmd+L calls.</b> Everything a lock has to do —
/// disposing the vault, taking the shell out of the visual tree, clearing a copied secret, putting
/// the unlock screen back — already hangs off <c>AppVaultSession.Lock</c>, so this is a new caller
/// and not a second lock path. A second path would be a second set of things to forget.
/// </para>
/// <para>
/// <b>The setting is read at the minimize, never captured here.</b> The one
/// <see cref="DesktopPreferences"/> this process holds is the same object the Settings screen
/// writes through, so ticking the box arms this on the next minimize rather than the next launch,
/// and no change notification has to exist for that to be true.
/// </para>
/// <para>
/// Only the transition <b>into</b> <see cref="WindowState.Minimized"/> acts. Restoring, maximizing
/// and going full screen are not security events, and restoring in particular must never unlock —
/// it shows the locked screen, because the window survives the lock and only its content changes.
/// </para>
/// </remarks>
internal sealed class MinimizeLock : IDisposable
{
    private readonly Window _window;
    private readonly Func<bool> _enabled;
    private readonly Action _lockNow;

    private bool _disposed;

    /// <summary>Watches a window.</summary>
    /// <param name="window">The window whose state to follow.</param>
    /// <param name="enabled">Whether minimizing should lock, asked each time.</param>
    /// <param name="lockNow">What locking is.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    internal MinimizeLock(Window window, Func<bool> enabled, Action lockNow)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(enabled);
        ArgumentNullException.ThrowIfNull(lockNow);

        _window = window;
        _enabled = enabled;
        _lockNow = lockNow;

        _window.PropertyChanged += OnWindowPropertyChanged;
    }

    /// <summary>Whether this platform tells the app that its window was minimized.</summary>
    /// <remarks>
    /// <para>
    /// True on all three desktop targets keypaste advertises. <c>Avalonia.Desktop</c> carries
    /// exactly three window backends — Win32, X11 and macOS — and each reports a native minimize
    /// through <c>WindowState</c>, which is what this watches.
    /// </para>
    /// <para>
    /// It exists so the Settings screen can <b>omit</b> the checkbox anywhere else rather than
    /// offer a security setting that does nothing (docs/PRODUCT.md law 3.7). An inactive one is the
    /// defect F.2b repairs; shipping the same shape again somewhere else would repeat it.
    /// </para>
    /// </remarks>
    internal static bool IsSupported =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux();

    /// <summary>Stops watching. Doing it twice is not an error.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.PropertyChanged -= OnWindowPropertyChanged;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Window.WindowStateProperty)
        {
            return;
        }

        // Avalonia raises this for the operating system's own minimize as well as an assignment:
        // every backend reports the state change back into the property.
        if (e.GetNewValue<WindowState>() is not WindowState.Minimized)
        {
            return;
        }

        if (_enabled())
        {
            _lockNow();
        }
    }
}
