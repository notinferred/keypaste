using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Keypaste.App.Views;

/// <summary>Asks for the agreement Settings' switch needs under terms not agreed to; Cancel, Escape or Agree closes it, and only Agree agrees.</summary>
internal sealed partial class TermsAgreementWindow : PromptWindow
{
    public TermsAgreementWindow()
    {
        AvaloniaXamlLoader.Load(this);
        FrameCard();
        this.FindControl<Button>("Deny")!.Click += (_, _) => Close();
        this.FindControl<Button>("Agree")!.Click += (_, _) => Close();
    }
}
