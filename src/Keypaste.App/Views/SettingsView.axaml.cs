using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Keypaste.App.Controls;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

internal sealed partial class SettingsView : UserControl
{
    private TermsAgreementWindow? _agreeing;

    public SettingsView()
    {
        AvaloniaXamlLoader.Load(this);

        DataContextChanged += (_, _) =>
        {
            if (DataContext is SettingsViewModel settings)
            {
                settings.AskTerms = AskTerms;
            }
        };

        var current = this.FindControl<MaskedInput>("CurrentPassword")!;
        current.CharacterTyped += (_, c) => Access?.TypeCurrent(c);
        current.BackspacePressed += (_, _) => Access?.BackspaceCurrent();
        current.ClearRequested += (_, _) => Access?.ClearCurrent();
        current.Pasting = () => Access?.PasteCurrentAsync() ?? Task.CompletedTask;

        var created = this.FindControl<MaskedInput>("AccessNewPassword")!;
        created.CharacterTyped += (_, c) => Access?.TypeNew(c);
        created.BackspacePressed += (_, _) => Access?.BackspaceNew();
        created.ClearRequested += (_, _) => Access?.ClearNew();
        created.Submitted += (_, _) => this.FindControl<MaskedInput>("AccessConfirmPassword")?.Focus();
        created.Pasting = () => Access?.PasteNewAsync() ?? Task.CompletedTask;

        var confirm = this.FindControl<MaskedInput>("AccessConfirmPassword")!;
        confirm.CharacterTyped += (_, c) => Access?.TypeConfirm(c);
        confirm.BackspacePressed += (_, _) => Access?.BackspaceConfirm();
        confirm.ClearRequested += (_, _) => Access?.ClearConfirm();
        confirm.Pasting = () => Access?.PasteConfirmAsync() ?? Task.CompletedTask;
    }

    private void AskTerms(FirstLaunchViewModel terms)
    {
        if (_agreeing is not null || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        _agreeing = new TermsAgreementWindow { DataContext = terms };
        _agreeing.Closed += (_, _) =>
        {
            _agreeing = null;

            // A binding puts back only a value that changed, and a switch that was not agreed to has not.
            var toggle = this.FindControl<ToggleSwitch>("SendCrashReports")!;
            toggle.IsChecked = (DataContext as SettingsViewModel)?.SendCrashReports;
            Dispatcher.UIThread.Post(() => toggle.Focus(), DispatcherPriority.Background);
        };
        _ = _agreeing.ShowDialog(owner);
    }

    // A lock replaces this screen, and the modal window must not stand over the unlock screen.
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _agreeing?.Close();
    }

    private VaultAccessViewModel? Access => (DataContext as SettingsViewModel)?.Access;
}
