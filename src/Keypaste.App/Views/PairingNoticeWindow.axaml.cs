using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Keypaste.Core.Approval;

namespace Keypaste.App.Views;

/// <summary>Tells the person that a browser asked to pair while pairing was not on, which keypaste refused unasked (D-0634).</summary>
internal sealed partial class PairingNoticeWindow : PromptWindow
{
    public PairingNoticeWindow()
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<Button>("Dismiss")!.Click += (_, _) => Close();
        Opened += (_, _) => this.FindControl<Button>("Dismiss")!.Focus();
    }

    /// <summary>A notice whose Open Settings shows the main window on Settings, where Pair a browser is.</summary>
    /// <param name="notice">What asked.</param>
    /// <param name="openSettings">Shows Settings, or null to offer Close alone.</param>
    internal PairingNoticeWindow(PairingNotice notice, Action? openSettings = null)
        : this()
    {
        ArgumentNullException.ThrowIfNull(notice);

        this.FindControl<TextBlock>("NoticeText")!.Text = PairingNotice.AppText;
        this.FindControl<TextBlock>("IdentityText")!.Text = notice.Identity.Described;

        var open = this.FindControl<Button>("OpenSettings")!;
        open.IsVisible = openSettings is not null;
        open.Click += (_, _) =>
        {
            Close();
            openSettings?.Invoke();
        };
    }
}
