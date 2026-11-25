using System.Globalization;
using Keypaste.Core;
using Keypaste.Core.Recommendations;

namespace Keypaste.App.ViewModels;

/// <summary>The reminders of one reason, which their heading explains once.</summary>
/// <param name="Heading">The reason.</param>
/// <param name="Reason">What it means.</param>
/// <param name="Rows">Each item with it.</param>
internal sealed record ReminderGroup(string Heading, string Reason, IReadOnlyList<ReminderRow> Rows);

/// <summary>One item expiring soon or marked as needing an update, as Security lists it, with where to make a new one.</summary>
internal sealed class ReminderRow : IRecommendationRow
{
    internal ReminderRow(RecommendationsViewModel owner, ItemReminder finding, bool isDismissed)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(finding);

        Finding = finding;
        IsDismissed = isDismissed;
        Entry = owner.Named(finding.Entry);

        OpenCommand = new RelayCommand(() => owner.Open(this));
        OpenPageCommand = new AsyncRelayCommand(() => owner.OpenPageAsync(this), () => HasPage);
        DismissCommand = new RelayCommand(() => owner.Dismiss(this));
        ReviewAgainCommand = new RelayCommand(() => owner.ReviewAgain(this));
    }

    internal ItemReminder Finding { get; }

    /// <summary>The item's title as a sentence names it.</summary>
    internal string Entry { get; }

    /// <summary>The row's muted second line: when it expires or was marked, or empty.</summary>
    internal string Detail => Finding.When is { } when
        ? Finding.Reason == ReminderReason.ExpiresSoon ? UseText.Expires(when) : UseText.Marked(when)
        : string.Empty;

    internal bool HasDetail => Detail.Length > 0;

    /// <summary>The item's group path and title, for its tooltip.</summary>
    internal string EntryPath => RecommendationsViewModel.Shown(Finding.Entry);

    /// <summary>A group's heading.</summary>
    internal static string Heading(ReminderReason reason) => reason switch
    {
        ReminderReason.ExpiresSoon => "Expires soon",
        _ => "Marked as needing an update",
    };

    /// <summary>What a group's heading means.</summary>
    internal static string Reason(ReminderReason reason, int expiryDays) => reason switch
    {
        ReminderReason.ExpiresSoon => expiryDays == 1
            ? "Their expiry date is within the next day."
            : string.Create(CultureInfo.InvariantCulture, $"Their expiry date is within the next {expiryDays} days."),
        _ => $"Tagged {Core.Approval.ItemChangeRules.NeedsUpdateTag} by you or an agent.",
    };

    internal bool HasPage => Finding.Page is not null;

    /// <summary>What the page button says: where it goes, never that keypaste changes anything there.</summary>
    internal string PageLabel => Finding.Page?.Kind switch
    {
        NewCredentialPageKind.Provider => "Open key page",
        NewCredentialPageKind.ChangePassword => "Open password page",
        _ => "Open URL",
    };

    /// <summary>The address the page button opens, as its tooltip shows it.</summary>
    internal string PageAddress => Finding.Page is { } page ? DisplayTextSanitizer.Sanitize(page.Address.AbsoluteUri, DisplayTextSanitizer.MaximumLength).Text : string.Empty;

    public bool IsDismissed { get; }

    public bool NeedsReview => !IsDismissed;

    internal string StateLabel => IsDismissed ? "Dismissed" : "Needs review";

    /// <summary>What a screen reader hears for the row.</summary>
    internal string Description => $"{Heading(Finding.Reason)}: {Entry}, {Detail}, {StateLabel.ToLowerInvariant()}";

    /// <summary>What a screen reader hears for the page button.</summary>
    internal string PageDescription => $"{PageLabel} for {Entry}: {PageAddress}";

    /// <summary>Shows the item in Items, selected.</summary>
    public RelayCommand OpenCommand { get; }

    /// <summary>Opens <see cref="ItemReminder.Page"/> in the browser; keypaste itself makes no request.</summary>
    internal AsyncRelayCommand OpenPageCommand { get; }

    public RelayCommand DismissCommand { get; }

    public RelayCommand ReviewAgainCommand { get; }
}
