using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Xunit;

namespace Keypaste.App.Tests;

/// <summary>
/// Presses a command as its button would, and fails the test when the button is off: a press that does nothing proves
/// nothing, so a test that means to reach a disabled command asserts that <see cref="ICommand.CanExecute"/> is false.
/// </summary>
/// <remarks>Declared in the tests' namespace, so it is the <c>Press</c> every test finds before the app's own.</remarks>
internal static class Pressing
{
    private const string _disabled = "the command was pressed while it cannot run; assert that CanExecute is false instead";

    internal static void Press(this ICommand command, object? parameter = null)
    {
        Assert.True(command.CanExecute(parameter), _disabled);
        command.Execute(parameter);
    }

    /// <summary>Runs <paramref name="command"/> to its end.</summary>
    internal static async Task PressAsync(this IAsyncRelayCommand command, object? parameter = null)
    {
        Assert.True(command.CanExecute(parameter), _disabled);
        await command.ExecuteAsync(parameter);
    }
}
