using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;

namespace Keypaste.App;

/// <summary>
/// The chords, built from the platform's own command modifier rather than a hardcoded Ctrl.
/// </summary>
/// <remarks>
/// Handled at the window, on the tunnelling pass, so a focused list or text field cannot eat
/// them first. <c>Ctrl/Cmd+L</c> is the honest counterweight to a five-minute idle timeout:
/// a default that short is only defensible when locking now is one keystroke. <c>Ctrl/Cmd+K</c>
/// goes to the titlebar search and a digit to the sidebar row in that position.
/// </remarks>
internal sealed class Shortcuts : IDisposable
{
    private readonly Window _window;
    private readonly AppVaultSession _session;
    private readonly Func<UnlockViewModel?> _unlock;
    private readonly Func<ShellViewModel?> _shell;
    private bool _disposed;

    /// <summary>Binds the chords to a window.</summary>
    /// <param name="window">The window a person types into.</param>
    /// <param name="session">The session <c>Ctrl/Cmd+L</c> locks.</param>
    /// <param name="unlock">The unlock screen, while it is showing.</param>
    /// <param name="shell">The unlocked shell, while it is showing.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    internal Shortcuts(
        Window window,
        AppVaultSession session,
        Func<UnlockViewModel?> unlock,
        Func<ShellViewModel?> shell)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(unlock);
        ArgumentNullException.ThrowIfNull(shell);

        _window = window;
        _session = session;
        _unlock = unlock;
        _shell = shell;

        _window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>Unbinds. Doing it twice is not an error.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Cmd on macOS, Ctrl everywhere else — which is what a platform hotkey configuration
        // resolves to, without depending on where Avalonia keeps that configuration this version.
        var command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

        if ((e.KeyModifiers & command) != command)
        {
            return;
        }

        if (_shell() is not { } shell)
        {
            if (e.Key == Key.O && _unlock() is { } unlock && unlock.BrowseCommand.CanExecute(null))
            {
                unlock.BrowseCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.L)
        {
            _session.Lock(VaultLockReason.Manual);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.K)
        {
            shell.FocusSearch();
            e.Handled = true;
            return;
        }

        var digit = e.Key switch
        {
            Key.D1 or Key.NumPad1 => 1,
            Key.D2 or Key.NumPad2 => 2,
            Key.D3 or Key.NumPad3 => 3,
            Key.D4 or Key.NumPad4 => 4,
            Key.D5 or Key.NumPad5 => 5,
            Key.D6 or Key.NumPad6 => 6,
            Key.D7 or Key.NumPad7 => 7,
            Key.D8 or Key.NumPad8 => 8,
            Key.D9 or Key.NumPad9 => 9,
            _ => 0,
        };

        if (digit > 0 && shell.GoTo(digit))
        {
            e.Handled = true;
        }
    }
}
