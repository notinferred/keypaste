using System.Windows.Input;

namespace Keypaste.Mvvm;

/// <summary>A command whose owner says when to ask again whether it can run.</summary>
public interface IRelayCommand : ICommand
{
    void RaiseCanExecuteChanged();
}
