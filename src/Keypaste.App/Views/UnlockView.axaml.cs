using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Keypaste.App.Controls;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

internal sealed partial class UnlockView : UserControl
{
    private readonly FileDragData.Judged<bool> _judged = new();
    private UnlockViewModel? _watched;

    public UnlockView()
    {
        AvaloniaXamlLoader.Load(this);
        Cue = this.FindControl<DropCue>(nameof(Cue))!;

        var password = this.FindControl<MaskedInput>("Password")!;
        password.CharacterTyped += (_, c) => Model?.Type(c);
        password.BackspacePressed += (_, _) => Model?.Backspace();
        password.ClearRequested += (_, _) => Model?.ClearPassword();
        // Enter on an empty field asks Windows Hello where it can unlock the vault, so the keyboard path stays launch, Enter.
        password.Submitted += (_, _) => (Model is { MaskedLength: 0, OffersQuickUnlock: true } model ? model.ResumeCommand : Model?.UnlockCommand)?.Press();
        password.Pasting = () => Model?.PasteAsync() ?? Task.CompletedTask;

        var created = this.FindControl<MaskedInput>("NewPassword")!;
        created.CharacterTyped += (_, c) => Model?.TypeNew(c);
        created.BackspacePressed += (_, _) => Model?.BackspaceNew();
        created.ClearRequested += (_, _) => Model?.ClearNew();
        created.Submitted += (_, _) => this.FindControl<MaskedInput>("ConfirmPassword")?.Focus();
        created.Pasting = () => Model?.PasteNewAsync() ?? Task.CompletedTask;

        var confirm = this.FindControl<MaskedInput>("ConfirmPassword")!;
        confirm.CharacterTyped += (_, c) => Model?.TypeConfirm(c);
        confirm.BackspacePressed += (_, _) => Model?.BackspaceConfirm();
        confirm.ClearRequested += (_, _) => Model?.ClearConfirm();
        confirm.Submitted += (_, _) => Model?.CreateCommand.Press();
        confirm.Pasting = () => Model?.PasteConfirmAsync() ?? Task.CompletedTask;

        var backup = this.FindControl<MaskedInput>("BackupPassword")!;
        backup.CharacterTyped += (_, c) => Model?.Restore?.Type(c);
        backup.BackspacePressed += (_, _) => Model?.Restore?.Backspace();
        backup.ClearRequested += (_, _) => Model?.Restore?.ClearPassword();
        backup.Submitted += (_, _) => Model?.Restore?.CheckCommand.Press();
        backup.Pasting = () => Model?.Restore?.PasteAsync() ?? Task.CompletedTask;

        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // Launch, type, Enter — with no mouse. The claim is only true if focus starts here, and it
        // has to be posted rather than set inline: binding the recent list's SelectedItem gives a
        // ListBoxItem focus after Loaded runs, which silently stole the first keystrokes whenever a
        // recent list existed. Found by running it, not by reading it.
        Loaded += (_, _) => Dispatcher.UIThread.Post(() => password.Focus(), DispatcherPriority.Background);

        DataContextChanged += (_, _) =>
        {
            if (_watched is not null)
            {
                _watched.PropertyChanged -= OnModelChanged;
                _watched.FileChosen -= OnFileChosen;
            }

            _watched = Model;

            if (_watched is not null)
            {
                _watched.PropertyChanged += OnModelChanged;
                _watched.FileChosen += OnFileChosen;

                // Answered, the keyboard goes back to what the screen is for, after the choice has kept it from falling away.
                if (_watched.CrashChoice is { } choice)
                {
                    choice.PropertyChanged += (_, changed) =>
                    {
                        if (changed.PropertyName == nameof(CrashChoiceViewModel.Outcome) && Model is { HasSelection: true })
                        {
                            Dispatcher.UIThread.Post(FocusPassword, DispatcherPriority.Background);
                        }
                    };
                }
            }
        };
    }

    /// <summary>Each form takes the keyboard as it appears, so typing never lands on a field that is gone.</summary>
    private void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        var field = e.PropertyName switch
        {
            nameof(UnlockViewModel.IsCreating) when Model is { IsCreating: true } => "NewPassword",
            nameof(UnlockViewModel.IsRestoring) when Model is { IsRestoring: true } => "BackupPassword",
            nameof(UnlockViewModel.IsOpening) when Model is { IsOpening: true } => "Password",
            _ => null,
        };

        if (field is not null && this.FindControl<MaskedInput>(field) is { } input)
        {
            Dispatcher.UIThread.Post(() => input.Focus(), DispatcherPriority.Background);
        }
    }

    // A vault picked in the open panel or dropped here is opened by typing its password, so that is where focus goes.
    private void OnFileChosen(object? sender, EventArgs e) => FocusPassword();

    private void FocusPassword()
    {
        if (this.FindControl<MaskedInput>("Password") is { } password)
        {
            Dispatcher.UIThread.Post(() => password.Focus(), DispatcherPriority.Background);
        }
    }

    private UnlockViewModel? Model => DataContext as UnlockViewModel;

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        // One file, which reads as a vault: the screen lights up only for what it would take. A platform that names the file only at the drop gets no highlight until then.
        var path = FileDragData.SinglePath(e.DataTransfer);
        var takes = Model is { } model && path is not null && _judged.Of(path, model.TakesDrop);
        var unnamed = path is null && !FileDragData.HasMany(e.DataTransfer) && e.DataTransfer.Contains(DataFormat.File);

        e.DragEffects = takes || unnamed ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;

        if (takes)
        {
            Cue.Show(this, UnlockViewModel.DropCaption(path!));
        }
        else
        {
            Cue.Hide();
        }
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        _judged.Forget();
        Cue.Hide();
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        OnDragLeave(sender, e);

        // A real filesystem path — the thing an HTML5 DataTransfer could never have given, and the
        // reason "drag a .kdbx" is achievable here at all.
        if (FileDragData.SinglePath(e.DataTransfer) is { } path && Model?.TakeDrop(path) == true)
        {
            FocusPassword();
        }

        e.Handled = true;
    }
}
