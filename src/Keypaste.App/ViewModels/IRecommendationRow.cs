namespace Keypaste.App.ViewModels;

/// <summary>What every row of Recommendations can do, whatever it found.</summary>
internal interface IRecommendationRow
{
    bool NeedsReview { get; }

    bool IsDismissed { get; }

    /// <summary>Shows the row's item in Items, which pressing the row does.</summary>
    RelayCommand OpenCommand { get; }

    RelayCommand DismissCommand { get; }

    RelayCommand ReviewAgainCommand { get; }
}
