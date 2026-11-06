using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Keypaste.App.Views;

/// <summary>Asks for the agreement Settings' switch needs under terms not agreed to; Cancel, Escape or Continue closes it, and only Continue agrees.</summary>
internal sealed partial class TermsAgreementWindow : PromptWindow
{
    public TermsAgreementWindow()
    {
        AvaloniaXamlLoader.Load(this);
        FrameCard();
        this.FindControl<Button>("Deny")!.Click += (_, _) => Close();
        this.FindControl<Button>("Continue")!.Click += (_, _) => Close();
    }
}
