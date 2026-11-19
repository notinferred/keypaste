using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>Settings › Diagnostics: the vault, keypaste's home and the version in use, and crash reports.</summary>
internal sealed partial class DiagnosticsView : UserControl
{
    private TermsAgreementWindow? _agreeing;

    public DiagnosticsView()
    {
        AvaloniaXamlLoader.Load(this);

        DataContextChanged += (_, _) =>
        {
            if (DataContext is DiagnosticsViewModel diagnostics)
            {
                diagnostics.AskTerms = AskTerms;
            }
        };
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
            toggle.IsChecked = (DataContext as DiagnosticsViewModel)?.SendCrashReports;
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
}
