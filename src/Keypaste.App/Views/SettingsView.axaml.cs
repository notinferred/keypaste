using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Keypaste.App.Controls;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

internal sealed partial class SettingsView : UserControl
{
    public SettingsView()
    {
        AvaloniaXamlLoader.Load(this);

        var current = this.FindControl<MaskedInput>("CurrentPassword")!;
        current.CharacterTyped += (_, c) => Access?.TypeCurrent(c);
        current.BackspacePressed += (_, _) => Access?.BackspaceCurrent();
        current.ClearRequested += (_, _) => Access?.ClearCurrent();

        var created = this.FindControl<MaskedInput>("AccessNewPassword")!;
        created.CharacterTyped += (_, c) => Access?.TypeNew(c);
        created.BackspacePressed += (_, _) => Access?.BackspaceNew();
        created.ClearRequested += (_, _) => Access?.ClearNew();
        created.Submitted += (_, _) => this.FindControl<MaskedInput>("AccessConfirmPassword")?.Focus();

        var confirm = this.FindControl<MaskedInput>("AccessConfirmPassword")!;
        confirm.CharacterTyped += (_, c) => Access?.TypeConfirm(c);
        confirm.BackspacePressed += (_, _) => Access?.BackspaceConfirm();
        confirm.ClearRequested += (_, _) => Access?.ClearConfirm();
    }

    private VaultAccessViewModel? Access => (DataContext as SettingsViewModel)?.Access;
}
