namespace Keypaste.App.ViewModels;

/// <summary>A sync tool's conflicted copy beside the vault, as Recommendations lists it.</summary>
internal sealed partial class ConflictedCopyRow(RecommendationsViewModel owner, string path) : ObservableObject
{
    internal string Path { get; } = path;

    internal string Name { get; } = System.IO.Path.GetFileName(path);

    /// <summary>What merging would change, once reviewed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReviewed), nameof(OffersReview))]
    internal partial string? Review { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OffersReview), nameof(IsIdle))]
    internal partial bool Busy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    internal partial string? Message { get; set; }

    internal bool IsReviewed => Review is not null;

    internal bool OffersReview => Review is null && !Busy;

    internal bool IsIdle => !Busy;

    internal bool HasMessage => Message is { Length: > 0 };

    [RelayCommand]
    private Task ReviewChangesAsync() => owner.ReviewAsync(this);

    [RelayCommand]
    private Task MergeAsync() => owner.MergeAsync(this);

    [RelayCommand]
    private void CancelReview() => Review = null;
}
