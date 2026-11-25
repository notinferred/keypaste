using Keypaste.App.Resources;
using Keypaste.Core;
using Keypaste.Core.Recommendations;

namespace Keypaste.App.ViewModels;

/// <summary>The keys left in one item's notes, as Security lists them: one finding moved together, item and keys, never a value.</summary>
internal sealed partial class RecommendationRow : ObservableObject, IRecommendationRow
{
    private readonly RecommendationsViewModel _owner;
    private bool _isSelected;

    internal RecommendationRow(RecommendationsViewModel owner, IReadOnlyList<NoteKeyFinding> findings, bool isDismissed)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(findings);

        _owner = owner;
        IsDismissed = isDismissed;
        Entry = owner.Named(findings[0].Entry);
        EntryPath = RecommendationsViewModel.Shown(findings[0].Entry);
        Keys = [.. findings.Select(finding => new NoteKeyRow(this, finding))];

        MoveCommand = new RelayCommand(() => owner.Move([this]), () => NeedsReview && HasIncludedKeys);
        AskMoveToStageCommand = new RelayCommand(() => owner.AskMoveToStage(this), () => NeedsReview && HasIncludedKeys);
        ConfirmMoveToStageCommand = new RelayCommand(() => owner.MoveToStage(this), () => ChosenStage is not null && HasIncludedKeys);
        CancelMoveToStageCommand = new RelayCommand(() => Stages = []);
        OpenCommand = new RelayCommand(() => owner.Open(this));
        DismissCommand = new RelayCommand(() => owner.Dismiss(this));
        ReviewAgainCommand = new RelayCommand(() => owner.ReviewAgain(this));
    }

    /// <summary>Each key found, in the order of the notes' lines.</summary>
    internal IReadOnlyList<NoteKeyRow> Keys { get; }

    /// <summary>The item's title as a sentence names it.</summary>
    internal string Entry { get; }

    /// <summary>The item's group path and title, for its tooltip.</summary>
    internal string EntryPath { get; }

    internal string EntryUuid => Keys[0].Finding.EntryUuid;

    internal bool HasSeveralKeys => Keys.Count > 1;

    internal bool HasOneKey => !HasSeveralKeys;

    /// <summary>The row's first line: the one key, or how many there are and where.</summary>
    internal string Heading => HasSeveralKeys
        ? UseText.KeysInNotes(Keys.Count, Entry)
        : Keys[0].Key;

    /// <summary>The row's second line: where the one key is, or every key's name.</summary>
    internal string Detail => HasSeveralKeys
        ? string.Join(", ", Keys.Select(key => key.Key))
        : UseText.InNotesOf(Entry);

    /// <summary>Whether the keys are listed one per row, each to tick or untick.</summary>
    [ObservableProperty]
    internal partial bool IsExpanded { get; set; }

    internal IReadOnlyList<NoteKeyFinding> Included => [.. Keys.Where(key => key.IsIncluded).Select(key => key.Finding)];

    internal bool HasIncludedKeys => Keys.Any(key => key.IsIncluded);

    public bool IsDismissed { get; }

    public bool NeedsReview => !IsDismissed;

    /// <summary>What a screen reader hears for the row.</summary>
    internal string Description => string.Join(", ", Heading, Detail, IsDismissed ? Strings.Dismissed : Strings.NeedsReview);

    /// <summary>Whether the bulk move takes this item's keys.</summary>
    internal bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value && !IsDismissed);
    }

    /// <summary>The project stages the keys can move into while that choice is open, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChoosingStage))]
    internal partial IReadOnlyList<ProjectMembership> Stages { get; set; } = [];

    internal bool IsChoosingStage => Stages.Count > 0;

    [ObservableProperty]
    internal partial ProjectMembership? ChosenStage { get; set; }

    /// <summary>What moving into the chosen stage does, shown before it is written.</summary>
    [ObservableProperty]
    internal partial string StagePreview { get; set; } = string.Empty;

    internal RelayCommand MoveCommand { get; }

    internal RelayCommand AskMoveToStageCommand { get; }

    internal RelayCommand ConfirmMoveToStageCommand { get; }

    internal RelayCommand CancelMoveToStageCommand { get; }

    public RelayCommand OpenCommand { get; }

    public RelayCommand DismissCommand { get; }

    public RelayCommand ReviewAgainCommand { get; }

    internal void IncludedChanged()
    {
        MoveCommand.NotifyCanExecuteChanged();
        AskMoveToStageCommand.NotifyCanExecuteChanged();
        ConfirmMoveToStageCommand.NotifyCanExecuteChanged();
        StagePreview = _owner.StagePreview(this);
    }

    partial void OnChosenStageChanged(ProjectMembership? value)
    {
        ConfirmMoveToStageCommand.NotifyCanExecuteChanged();
        StagePreview = _owner.StagePreview(this);
    }
}

/// <summary>One key of an item's notes, ticked to move with the rest.</summary>
internal sealed partial class NoteKeyRow : ObservableObject
{
    private readonly RecommendationRow _owner;

    internal NoteKeyRow(RecommendationRow owner, NoteKeyFinding finding)
    {
        _owner = owner;
        Finding = finding;
    }

    internal NoteKeyFinding Finding { get; }

    /// <summary>The field the value would become.</summary>
    internal string Key => Finding.Field;

    /// <summary>The token kind for a token found on a line of its own, or null for a <c>KEY=value</c> line.</summary>
    internal string? TokenKind => Finding.TokenKind;

    internal bool IsToken => Finding.Kind == NoteKeyKind.Token;

    [ObservableProperty]
    internal partial bool IsIncluded { get; set; } = true;

    partial void OnIsIncludedChanged(bool value) => _owner.IncludedChanged();
}
