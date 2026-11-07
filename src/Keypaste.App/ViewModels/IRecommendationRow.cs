namespace Keypaste.App.ViewModels;

/// <summary>What every row of Recommendations can do, whatever it found.</summary>
internal interface IRecommendationRow
{
    bool NeedsReview { get; }

    bool IsDismissed { get; }

    RelayCommand DismissCommand { get; }

    RelayCommand ReviewAgainCommand { get; }
}
