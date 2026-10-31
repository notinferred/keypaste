using CommunityToolkit.Mvvm.Input;

namespace Keypaste.App.Tests;

internal static class Pressing
{
    /// <summary>Runs <paramref name="command"/> to its end only while it can run, as its button would.</summary>
    internal static async Task PressAsync(this IAsyncRelayCommand command, object? parameter = null)
    {
        if (command.CanExecute(parameter))
        {
            await command.ExecuteAsync(parameter);
        }
    }
}
