using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;

namespace Keypaste.App;

/// <summary>
/// The window's chords, read from <see cref="AppCommands"/>: a chord runs the command whose key it is, with exactly
/// its modifiers, while that command can run.
/// </summary>
/// <remarks>
/// Handled at the window, on the tunnelling pass, so a focused list or text field cannot eat them first; a text
/// field's own Copy, Paste and Undo are left to it. Exact modifiers matter: Windows reports AltGr as Ctrl+Alt, so a
/// chord with an extra modifier is a character being typed (Q.1). <c>Ctrl/Cmd+L</c> is the honest counterweight to a
/// five-minute idle timeout: a default that short is only defensible when locking now is one keystroke.
/// </remarks>
internal sealed class Shortcuts : IDisposable
{
    private readonly Window _window;
    private readonly AppCommands _commands;
    private bool _disposed;

    /// <summary>Binds the chords of <paramref name="commands"/> to a window.</summary>
    internal Shortcuts(Window window, AppCommands commands)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(commands);

        _window = window;
        _commands = commands;
        _window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>Binds the chords of the commands that reach the session, the unlock screen and the shell alone.</summary>
    /// <param name="window">The window a person types into.</param>
    /// <param name="session">The session <c>Ctrl/Cmd+L</c> locks.</param>
    /// <param name="unlock">The unlock screen, while it is showing.</param>
    /// <param name="shell">The unlocked shell, while it is showing.</param>
    internal Shortcuts(
        Window window,
        AppVaultSession session,
        Func<UnlockViewModel?> unlock,
        Func<ShellViewModel?> shell)
        : this(window, new AppCommands(new AppCommandHost { Session = session, Unlock = unlock, Shell = shell, Window = () => window }))
    {
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
        if (_commands.ForChord(e.Key, e.KeyModifiers) is { } command)
        {
            command.Run();
            e.Handled = true;
        }
    }
}
