using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Keypaste.App.Views;

/// <summary>The terms of use the app carries, whole and scrollable, which Escape or Close dismisses.</summary>
internal sealed partial class TermsWindow : PromptWindow
{
    public TermsWindow()
    {
        AvaloniaXamlLoader.Load(this);
        FrameCard();
        this.FindControl<Button>("Dismiss")!.Click += (_, _) => Close();
        var text = this.FindControl<ScrollViewer>("TermsText")!;
        Opened += (_, _) => text.Focus();
    }
}
