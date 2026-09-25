using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Keypaste.App.Controls;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

internal sealed partial class KdbxImportView : UserControl
{
    public KdbxImportView()
    {
        AvaloniaXamlLoader.Load(this);

        var password = this.FindControl<MaskedInput>("ImportPassword")!;
        password.Submitted += (_, _) => Run(Model?.UnlockCommand);

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && Model is { } model)
            {
                model.CancelCommand.Execute(null);
                e.Handled = true;
            }
        };

        // The view is built with the shell and hidden; a dialog opens when it is given a model.
        DataContextChanged += (_, _) =>
        {
            if (Model is not null)
            {
                Dispatcher.UIThread.Post(() => password.Focus(), DispatcherPriority.Background);
            }
        };
    }

    private KdbxImportViewModel? Model => DataContext as KdbxImportViewModel;

    private static void Run(AsyncRelayCommand? command)
    {
        if (command is not null && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }
}
