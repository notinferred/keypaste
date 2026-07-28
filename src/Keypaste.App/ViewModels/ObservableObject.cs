using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Keypaste.App.ViewModels;

/// <summary>
/// The smallest thing that makes a binding update.
/// </summary>
/// <remarks>
/// Hand-rolled rather than <c>CommunityToolkit.Mvvm</c>, for the reason D-0028 gave for writing a
/// TOML subset by hand and D-0019 gave for taking the narrow MCP package: this whole file is sixty
/// lines, and a package still enters <c>packages.lock.json</c>, still restores under
/// <c>--locked-mode</c>, and still turns the build red the day it draws a low-severity advisory
/// under <c>NuGetAudit</c> (CORE.md law 3.9). Worth revisiting when 4.2's entry list arrives with
/// real evidence of how many bindings this app actually needs.
/// </remarks>
internal abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(name);
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// A command that runs once at a time.
/// </summary>
/// <remarks>
/// <b>Single-flight is not decoration here.</b> Unlocking runs Argon2, which takes a good fraction
/// of a second, and a double-click on the unlock button would otherwise start two derivations
/// against one buffer. The second would find it disposed.
/// </remarks>
internal sealed class AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool _running;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _running = true;
        RaiseCanExecuteChanged();

        try
        {
            await execute().ConfigureAwait(true);
        }
        finally
        {
            _running = false;
            RaiseCanExecuteChanged();
        }
    }

    internal void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>A command with nothing to await.</summary>
internal sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            execute();
        }
    }

    internal void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
