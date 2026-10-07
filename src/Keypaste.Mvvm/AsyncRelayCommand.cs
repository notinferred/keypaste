namespace Keypaste.Mvvm;

/// <summary>
/// A command that runs once at a time.
/// </summary>
/// <remarks>
/// <b>Single-flight is not decoration here.</b> Unlocking runs Argon2, which takes a good fraction
/// of a second, and a double-click on the unlock button would otherwise start two derivations
/// against one buffer. The second would find it disposed.
/// </remarks>
public sealed class AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null) : IRelayCommand
{
    private bool _running;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter) => await ExecuteAsync().ConfigureAwait(true);

    /// <summary>
    /// The same work, awaitable.
    /// </summary>
    /// <remarks>
    /// <see cref="System.Windows.Input.ICommand.Execute"/> returns <c>void</c>, so a test that drove
    /// the command would race whatever it wanted to assert. The same reason
    /// <c>UnlockViewModel.UnlockAsync</c> is reachable rather than private.
    /// </remarks>
    public async Task ExecuteAsync()
    {
        if (!CanExecute(null))
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

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
