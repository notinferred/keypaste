using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Keypaste.App.Controls;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

internal sealed partial class UnlockView : UserControl
{
    public UnlockView()
    {
        AvaloniaXamlLoader.Load(this);

        var password = this.FindControl<MaskedInput>("Password")!;
        password.CharacterTyped += (_, c) => Model?.Type(c);
        password.BackspacePressed += (_, _) => Model?.Backspace();
        password.ClearRequested += (_, _) => Model?.ClearPassword();
        password.Submitted += (_, _) => Run(Model?.UnlockCommand);

        var created = this.FindControl<MaskedInput>("NewPassword")!;
        created.CharacterTyped += (_, c) => Model?.TypeNew(c);
        created.BackspacePressed += (_, _) => Model?.BackspaceNew();
        created.ClearRequested += (_, _) => Model?.ClearNew();
        created.Submitted += (_, _) => this.FindControl<MaskedInput>("ConfirmPassword")?.Focus();

        var confirm = this.FindControl<MaskedInput>("ConfirmPassword")!;
        confirm.CharacterTyped += (_, c) => Model?.TypeConfirm(c);
        confirm.BackspacePressed += (_, _) => Model?.BackspaceConfirm();
        confirm.ClearRequested += (_, _) => Model?.ClearConfirm();
        confirm.Submitted += (_, _) => Run(Model?.CreateCommand);

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // Launch, type, Enter — with no mouse. The claim is only true if focus starts here, and it
        // has to be posted rather than set inline: binding the recent list's SelectedItem gives a
        // ListBoxItem focus after Loaded runs, which silently stole the first keystrokes whenever a
        // recent list existed. Found by running it, not by reading it.
        Loaded += (_, _) => Dispatcher.UIThread.Post(() => password.Focus(), DispatcherPriority.Background);
    }

    private UnlockViewModel? Model => DataContext as UnlockViewModel;

    private static void Run(AsyncRelayCommand? command)
    {
        if (command is not null && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Link
            : DragDropEffects.None;

        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        // A real filesystem path — the thing an HTML5 DataTransfer could never have given, and the
        // reason "drag a .kdbx" is achievable here at all.
        if (e.DataTransfer.TryGetFile() is { } file && file.TryGetLocalPath() is { } path)
        {
            Model?.Offer(path);
        }

        e.Handled = true;
    }
}
