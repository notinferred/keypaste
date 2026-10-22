using System.Globalization;
using Keypaste.Core.Recommendations;

namespace Keypaste.App.ViewModels;

/// <summary>One weak, reused or expired password, as Settings › Recommendations lists it: the item and the problem, never the value (V.9).</summary>
internal sealed class PasswordRow
{
    private const int _namedSharers = 3;

    internal PasswordRow(RecommendationsViewModel owner, PasswordFinding finding, bool isDismissed)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(finding);

        Finding = finding;
        IsDismissed = isDismissed;
        Entry = RecommendationsViewModel.Shown(finding.Entry);

        OpenCommand = new RelayCommand(() => owner.Open(this));
        DismissCommand = new RelayCommand(() => owner.Dismiss(this));
        ReviewAgainCommand = new RelayCommand(() => owner.ReviewAgain(this));
    }

    internal PasswordFinding Finding { get; }

    /// <summary>The item's path, sanitized for display.</summary>
    internal string Entry { get; }

    internal string Problem => Finding.Problem switch
    {
        PasswordProblem.Weak => "Weak password",
        PasswordProblem.Reused => "Reused password",
        _ => "Expired item",
    };

    internal string Where => Finding.Problem switch
    {
        PasswordProblem.Weak => $"on {Entry}, short or predictable enough to be guessed",
        PasswordProblem.Reused => $"on {Entry}, also on {Sharers()}",
        _ => string.Create(
            CultureInfo.InvariantCulture,
            $"on {Entry}, whose expiry date {Finding.ExpiredUtc?.ToLocalTime():yyyy-MM-dd HH:mm} has passed"),
    };

    internal bool IsDismissed { get; }

    internal bool NeedsReview => !IsDismissed;

    internal string StateLabel => IsDismissed ? "Dismissed" : "Needs review";

    /// <summary>What a screen reader hears for the row.</summary>
    internal string Description => $"{Problem} {Where}, {StateLabel.ToLowerInvariant()}";

    /// <summary>What a screen reader hears for Open item: the item, never its password.</summary>
    internal string OpenLabel => $"Open {Entry} in Items";

    /// <summary>Shows the item in Items, selected, where its password is replaced.</summary>
    internal RelayCommand OpenCommand { get; }

    internal RelayCommand DismissCommand { get; }

    internal RelayCommand ReviewAgainCommand { get; }

    /// <summary>The other items holding the password: every one up to three, or two and how many more.</summary>
    private string Sharers()
    {
        List<string> names = [.. Finding.SharedWith.Select(RecommendationsViewModel.Shown).Order(StringComparer.Ordinal)];

        if (names.Count > _namedSharers)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{names[0]}, {names[1]} and {names.Count - 2} other items");
        }

        return names.Count == 1 ? names[0] : $"{string.Join(", ", names.SkipLast(1))} and {names[^1]}";
    }
}
