using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>Items › Recommendations: what <see cref="ViewModels.RecommendationsViewModel"/> found, for review.</summary>
internal sealed partial class RecommendationsView : UserControl
{
    public RecommendationsView()
    {
        AvaloniaXamlLoader.Load(this);
        AddHandler(TappedEvent, OpenTappedRow);
    }

    // Pressing a finding's row opens its item, unless the press was on one of the row's own controls.
    private static void OpenTappedRow(object? sender, TappedEventArgs e)
    {
        foreach (var visual in (e.Source as Visual)?.GetSelfAndVisualAncestors() ?? [])
        {
            if (visual is Button or ComboBox or TextBox)
            {
                return;
            }

            if (visual is Grid { DataContext: IRecommendationRow row } grid && grid.Classes.Contains("opens-item"))
            {
                row.OpenCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }
    }
}
