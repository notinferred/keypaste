using System.Windows.Input;

namespace Keypaste.App;

internal static class Commands
{
    /// <summary>Runs <paramref name="command"/> only while it can run, as its button would.</summary>
    internal static void Press(this ICommand command, object? parameter = null)
    {
        if (command.CanExecute(parameter))
        {
            command.Execute(parameter);
        }
    }
}
