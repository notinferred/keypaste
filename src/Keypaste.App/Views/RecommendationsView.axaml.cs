using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Keypaste.App.Views;

/// <summary>Items › Recommendations: what <see cref="ViewModels.RecommendationsViewModel"/> found, for review.</summary>
internal sealed partial class RecommendationsView : UserControl
{
    public RecommendationsView() => AvaloniaXamlLoader.Load(this);
}
