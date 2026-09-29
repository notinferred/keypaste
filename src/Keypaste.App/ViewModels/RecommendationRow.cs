using Keypaste.Core.Recommendations;

namespace Keypaste.App.ViewModels;

/// <summary>One key left in notes, as Settings › Recommendations lists it: entry and key, never the value.</summary>
internal sealed class RecommendationRow : ObservableObject
{
    private bool _isSelected;

    internal RecommendationRow(RecommendationsViewModel owner, NoteKeyFinding finding, bool isDismissed)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(finding);

        Finding = finding;
        IsDismissed = isDismissed;
        Entry = RecommendationsViewModel.Shown(finding.Entry);

        MoveCommand = new RelayCommand(() => owner.Move([this]));
        DismissCommand = new RelayCommand(() => owner.Dismiss(this));
        ReviewAgainCommand = new RelayCommand(() => owner.ReviewAgain(this));
    }

    internal NoteKeyFinding Finding { get; }

    /// <summary>The entry's path, sanitized for display.</summary>
    internal string Entry { get; }

    /// <summary>The field the value would become.</summary>
    internal string Key => Finding.Field;

    /// <summary>The token kind for a token found on a line of its own, or null for a <c>KEY=value</c> line.</summary>
    internal string? TokenKind => Finding.TokenKind;

    internal bool IsToken => Finding.Kind == NoteKeyKind.Token;

    internal string Where => $"in the notes of {Entry}";

    internal bool IsDismissed { get; }

    internal bool NeedsReview => !IsDismissed;

    internal string StateLabel => IsDismissed ? "Dismissed" : "Needs review";

    /// <summary>What a screen reader hears for the row.</summary>
    internal string Description => IsToken
        ? $"{TokenKind}, becomes {Key}, {Where}, {StateLabel.ToLowerInvariant()}"
        : $"{Key} {Where}, {StateLabel.ToLowerInvariant()}";

    internal bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value && !IsDismissed);
    }

    internal RelayCommand MoveCommand { get; }

    internal RelayCommand DismissCommand { get; }

    internal RelayCommand ReviewAgainCommand { get; }
}
