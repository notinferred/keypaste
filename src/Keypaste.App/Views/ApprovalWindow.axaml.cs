using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

internal sealed partial class ApprovalWindow : Window
{
    public ApprovalWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // Focus starts on Deny, so a keystroke meant for another window can only refuse.
        Opened += (_, _) => this.FindControl<Button>("Deny")?.Focus();
    }

    internal ApprovalWindow(ApprovalViewModel request)
        : this() => DataContext = request;

    /// <summary>However the window closes, a request still open is refused.</summary>
    protected override void OnClosed(EventArgs e)
    {
        (DataContext as ApprovalViewModel)?.Closed();
        base.OnClosed(e);
    }
}
