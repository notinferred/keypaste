using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Keypaste.App.Controls;
using Keypaste.App.Resources;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>The one unlock form: each of its fields takes the keyboard as it appears, so typing never lands on a field that is gone.</summary>
internal sealed partial class UnlockForm : UserControl
{
    private UnlockViewModel? _watched;

    public UnlockForm()
    {
        AvaloniaXamlLoader.Load(this);

        var password = Field("Password");
        password.CharacterTyped += (_, c) => Model?.Type(c);
        password.BackspacePressed += (_, _) => Model?.Backspace();
        password.ClearRequested += (_, _) => Model?.ClearPassword();
        // Enter on an empty field asks Windows Hello or Touch ID where it can unlock the vault, so the keyboard path stays launch, Enter.
        password.Submitted += (_, _) => (Model is { MaskedLength: 0, OffersQuickUnlock: true } model ? model.ResumeCommand : Model?.UnlockCommand)?.Press();
        password.Pasting = () => Model?.PasteAsync() ?? Task.CompletedTask;

        var created = Field("NewPassword");
        created.CharacterTyped += (_, c) => Model?.TypeNew(c);
        created.BackspacePressed += (_, _) => Model?.BackspaceNew();
        created.ClearRequested += (_, _) => Model?.ClearNew();
        created.Submitted += (_, _) => Field("ConfirmPassword").Focus();
        created.Pasting = () => Model?.PasteNewAsync() ?? Task.CompletedTask;

        var confirm = Field("ConfirmPassword");
        confirm.CharacterTyped += (_, c) => Model?.TypeConfirm(c);
        confirm.BackspacePressed += (_, _) => Model?.BackspaceConfirm();
        confirm.ClearRequested += (_, _) => Model?.ClearConfirm();
        confirm.Submitted += (_, _) => Model?.CreateCommand.Press();
        confirm.Pasting = () => Model?.PasteConfirmAsync() ?? Task.CompletedTask;

        var backup = Field("BackupPassword");
        backup.CharacterTyped += (_, c) => Model?.Restore?.Type(c);
        backup.BackspacePressed += (_, _) => Model?.Restore?.Backspace();
        backup.ClearRequested += (_, _) => Model?.Restore?.ClearPassword();
        backup.Submitted += (_, _) => Model?.Restore?.CheckCommand.Press();
        backup.Pasting = () => Model?.Restore?.PasteAsync() ?? Task.CompletedTask;

        // Posted rather than set inline: a list bound to the selection gives its row focus after Loaded runs, which took the first keystrokes.
        Loaded += (_, _) => FocusForm();

        DataContextChanged += (_, _) =>
        {
            if (_watched is not null)
            {
                _watched.PropertyChanged -= OnModelChanged;
            }

            _watched = Model;

            if (_watched is not null)
            {
                _watched.PropertyChanged += OnModelChanged;
            }

            LabelQuickUnlock();
        };
    }

    // Set, not bound, so the button is announced by one of the screen's literal labels.
    private void LabelQuickUnlock()
    {
        var label = Model?.QuickUnlockLabel ?? Strings.UnlockWithWindowsHello;
        AutomationProperties.SetName(this.FindControl<Button>("QuickUnlock")!, label);
        this.FindControl<TextBlock>("QuickUnlockLabel")!.Text = label;
    }

    private UnlockViewModel? Model => DataContext as UnlockViewModel;

    /// <summary>Puts the keyboard in the field the form shows now.</summary>
    internal void FocusForm()
    {
        var field = Field(Model switch
        {
            { IsCreating: true } => "NewPassword",
            { IsRestoring: true } => "BackupPassword",
            _ => "Password",
        });
        Dispatcher.UIThread.Post(() => field.Focus(), DispatcherPriority.Background);
    }

    private MaskedInput Field(string name) => this.FindControl<MaskedInput>(name)!;

    // A form that changes what it is for drops what the last one said, so a refusal from opening is not heard over creating.
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(UnlockViewModel.IsCreating) or nameof(UnlockViewModel.IsRestoring) or nameof(UnlockViewModel.ShowsPasswordForm))
        {
            Announcer.Clear(this);
            FocusForm();
        }
    }
}
