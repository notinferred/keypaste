namespace Keypaste.Mvvm;

/// <summary>A command with nothing to await.</summary>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : IRelayCommand
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

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// A command that is told which item it was pressed for.
/// </summary>
/// <remarks>
/// For a button inside a list, where the alternative is a <c>SelectedItem</c> that exists only so a
/// parameterless command can read it — state a list already has, kept twice.
/// </remarks>
/// <typeparam name="T">What the command expects as its parameter.</typeparam>
public sealed class RelayCommand<T>(Action<T?> execute, Func<T?, bool>? canExecute = null) : IRelayCommand
    where T : class
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter as T) ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            execute(parameter as T);
        }
    }

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
