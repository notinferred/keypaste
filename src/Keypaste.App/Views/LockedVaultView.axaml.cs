using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Keypaste.App.Controls;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

internal sealed partial class LockedVaultView : UserControl
{
    private UnlockViewModel? _watched;

    public LockedVaultView()
    {
        AvaloniaXamlLoader.Load(this);

        var password = Field("LockedVaultPassword");
        password.CharacterTyped += (_, c) => Model?.Type(c);
        password.BackspacePressed += (_, _) => Model?.Backspace();
        password.ClearRequested += (_, _) => Model?.ClearPassword();
        password.Submitted += (_, _) => (Model is { MaskedLength: 0, OffersQuickUnlock: true } model ? model.ResumeCommand : Model?.UnlockCommand)?.Press();
        password.Pasting = () => Model?.PasteAsync() ?? Task.CompletedTask;

        var created = Field("LockedVaultNewPassword");
        created.CharacterTyped += (_, c) => Model?.TypeNew(c);
        created.BackspacePressed += (_, _) => Model?.BackspaceNew();
        created.ClearRequested += (_, _) => Model?.ClearNew();
        created.Submitted += (_, _) => Field("LockedVaultConfirmPassword").Focus();
        created.Pasting = () => Model?.PasteNewAsync() ?? Task.CompletedTask;

        var confirm = Field("LockedVaultConfirmPassword");
        confirm.CharacterTyped += (_, c) => Model?.TypeConfirm(c);
        confirm.BackspacePressed += (_, _) => Model?.BackspaceConfirm();
        confirm.ClearRequested += (_, _) => Model?.ClearConfirm();
        confirm.Submitted += (_, _) => Model?.CreateCommand.Press();
        confirm.Pasting = () => Model?.PasteConfirmAsync() ?? Task.CompletedTask;

        // Choosing the locked vault puts the keyboard in its password, so choose, type, Return unlocks it.
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
        };
    }

    private UnlockViewModel? Model => (DataContext as LockedVaultViewModel)?.Unlock;

    private MaskedInput Field(string name) => this.FindControl<MaskedInput>(name)!;

    private void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(UnlockViewModel.IsCreating) or nameof(UnlockViewModel.IsOpening))
        {
            FocusForm();
        }
    }

    private void FocusForm()
    {
        var field = Field(Model is { IsCreating: true } ? "LockedVaultNewPassword" : "LockedVaultPassword");
        Dispatcher.UIThread.Post(() => field.Focus(), DispatcherPriority.Background);
    }
}
