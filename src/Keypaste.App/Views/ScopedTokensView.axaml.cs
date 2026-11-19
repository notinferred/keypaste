using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Keypaste.App.Views;

/// <summary>Settings › Scoped tokens: the vault's tokens, making one and revoking one.</summary>
internal sealed partial class ScopedTokensView : UserControl
{
    // Under this the form's three fields stand one above another at the form's width.
    private const double _fieldsStackBelow = 520;

    public ScopedTokensView()
    {
        AvaloniaXamlLoader.Load(this);

        var fields = this.FindControl<Grid>("TokenFields")!;
        fields.SizeChanged += (_, e) => Dispatcher.UIThread.Post(() => Stack(fields, e.NewSize.Width < _fieldsStackBelow), DispatcherPriority.Loaded);

        var learnMore = this.FindControl<Button>("LearnMoreScopes")!;
        learnMore.Flyout!.Closed += (_, _) => learnMore.Focus();
    }

    private static void Stack(Grid fields, bool stacked)
    {
        for (var index = 0; index < fields.Children.Count; index++)
        {
            Grid.SetColumn(fields.Children[index], stacked ? 0 : index);
            Grid.SetRow(fields.Children[index], stacked ? index : 0);
            Grid.SetColumnSpan(fields.Children[index], stacked ? fields.ColumnDefinitions.Count : 1);
        }
    }
}
