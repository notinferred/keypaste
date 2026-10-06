using System.Globalization;
using Keypaste.Core;
using Keypaste.Core.Recommendations;

namespace Keypaste.App.ViewModels;

/// <summary>One project tag an entry lost in another app, as Settings › Recommendations lists it (V.11).</summary>
internal sealed class LostTagRow : ObservableObject
{
    private IReadOnlyList<string> _confirmation = [];

    internal LostTagRow(RecommendationsViewModel owner, LostProjectTag finding, bool isDismissed)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(finding);

        Finding = finding;
        IsDismissed = isDismissed;
        Entry = RecommendationsViewModel.Shown(finding.Entry);
        Tag = EntryNameSanitizer.Sanitize(finding.Tag).Text;

        RestoreCommand = new RelayCommand(() => owner.AskRestore(this));
        ConfirmRestoreCommand = new RelayCommand(() => owner.Restore(this));
        CancelRestoreCommand = new RelayCommand(() => Confirmation = []);
        DismissCommand = new RelayCommand(() => owner.Dismiss(this));
        ReviewAgainCommand = new RelayCommand(() => owner.ReviewAgain(this));
    }

    internal LostProjectTag Finding { get; }

    /// <summary>The entry's path, sanitized for display.</summary>
    internal string Entry { get; }

    /// <summary>The tag the entry lost, sanitized for display.</summary>
    internal string Tag { get; }

    internal string Where => string.Create(
        CultureInfo.InvariantCulture,
        $"dropped from {Entry} in a change saved {Finding.LostUtc.ToLocalTime():yyyy-MM-dd HH:mm}{(Finding.Protects ? ", so agents are no longer asked live for it" : string.Empty)}");

    internal bool IsDismissed { get; }

    internal bool NeedsReview => !IsDismissed && !IsConfirming;

    internal string StateLabel => IsDismissed ? "Dismissed" : "Needs review";

    /// <summary>What a screen reader hears for the row.</summary>
    internal string Description => $"{Tag} {Where}, {StateLabel.ToLowerInvariant()}";

    /// <summary>What putting the tag back does, shown before it is written (D-0415), or empty when nothing is being asked.</summary>
    internal IReadOnlyList<string> Confirmation
    {
        get => _confirmation;
        set
        {
            if (Set(ref _confirmation, value))
            {
                Raise(nameof(IsConfirming));
                Raise(nameof(NeedsReview));
            }
        }
    }

    internal bool IsConfirming => _confirmation.Count > 0;

    internal RelayCommand RestoreCommand { get; }

    internal RelayCommand ConfirmRestoreCommand { get; }

    internal RelayCommand CancelRestoreCommand { get; }

    internal RelayCommand DismissCommand { get; }

    internal RelayCommand ReviewAgainCommand { get; }
}
