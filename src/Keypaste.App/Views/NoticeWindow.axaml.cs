using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Keypaste.Core.Approval;

namespace Keypaste.App.Views;

/// <summary>Tells the person, where they chose Always for, that the change it asked for did not take effect.</summary>
internal sealed partial class NoticeWindow : PromptWindow
{
    public NoticeWindow()
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<Button>("Dismiss")!.Click += (_, _) => Close();
        Opened += (_, _) => this.FindControl<Button>("Dismiss")!.Focus();
    }

    /// <summary>A notice whose second button opens the agent's settings in the main window.</summary>
    /// <param name="notice">What did not take effect.</param>
    /// <param name="openSettings">Shows the main window with the named agent's settings open, or null to offer Close alone.</param>
    internal NoticeWindow(AlwaysForNotice notice, Action<string>? openSettings = null)
        : this()
    {
        ArgumentNullException.ThrowIfNull(notice);

        this.FindControl<TextBlock>("NoticeText")!.Text = Said(notice);

        var open = this.FindControl<Button>("OpenSettings")!;
        var words = $"Open {notice.Agent}'s settings";
        this.FindControl<TextBlock>("OpenSettingsText")!.Text = words;
        AutomationProperties.SetName(open, words);
        open.IsVisible = openSettings is not null;
        open.Click += (_, _) =>
        {
            Close();
            openSettings?.Invoke(notice.Agent);
        };
    }

    /// <summary>What the window says: what was not made Automatic and why, and where to try again.</summary>
    internal static string Said(AlwaysForNotice notice) =>
        $"{notice.Unmade} To try again, open {notice.Agent}'s settings on the Agents screen.";
}
