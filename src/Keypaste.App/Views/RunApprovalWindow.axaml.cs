using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

internal sealed partial class RunApprovalWindow : Window
{
    public RunApprovalWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // Focus starts on Deny, so a keystroke meant for another window can only refuse.
        Opened += (_, _) => this.FindControl<Button>("Deny")?.Focus();
    }

    internal RunApprovalWindow(RunApprovalViewModel request)
        : this() => DataContext = request;

    /// <summary>However the window closes, a request still open is refused.</summary>
    protected override void OnClosed(EventArgs e)
    {
        (DataContext as RunApprovalViewModel)?.Closed();
        base.OnClosed(e);
    }
}
