using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Keypaste.App.Terms;

namespace Keypaste.App.Views;

/// <summary>"Terms of use": opens the terms in its <see cref="TermsOfUse"/> data context in a small window over this one, and has the keyboard back when it closes.</summary>
internal sealed partial class TermsLink : UserControl
{
    private TermsWindow? _open;

    public TermsLink()
    {
        AvaloniaXamlLoader.Load(this);
        var link = this.FindControl<Button>("OpenTerms")!;
        link.Click += (_, _) => Open(link);
    }

    private void Open(Button link)
    {
        if (_open is not null || DataContext is not TermsOfUse || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        _open = new TermsWindow { DataContext = DataContext };
        _open.Closed += (_, _) =>
        {
            _open = null;
            Dispatcher.UIThread.Post(() => link.Focus(), DispatcherPriority.Background);
        };
        _ = _open.ShowDialog(owner);
    }

    // A lock replaces the screen this link is on, and the modal window must not stand over the unlock screen.
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _open?.Close();
    }
}
