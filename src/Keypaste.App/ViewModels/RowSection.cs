using Keypaste.App.Resources;

namespace Keypaste.App.ViewModels;

/// <summary>A heading in Items' list over the items that changed in one range of days, which cannot be chosen.</summary>
/// <param name="DaysBack">The fewest days back it starts: 0 for Today, 1 for This week, 7 for Older.</param>
internal sealed record RowSection(int DaysBack)
{
    internal string Title => DaysBack switch
    {
        0 => Strings.Today,
        < 7 => Strings.ThisWeek,
        _ => Strings.Older,
    };

    public override string ToString() => Title;
}

/// <summary>The three ranges Items' list is split into while it runs by when items changed, as Outlook splits mail.</summary>
internal static class RowSections
{
    internal static RowSection Today { get; } = new(0);

    internal static RowSection ThisWeek { get; } = new(1);

    internal static RowSection Older { get; } = new(7);

    /// <summary>The range an item changed in, against the person's own day; one whose time was not read is Older.</summary>
    internal static RowSection Of(DateTimeOffset? modified, DateTimeOffset now)
    {
        if (modified is not { } at)
        {
            return Older;
        }

        var days = (now.Date - at.ToLocalTime().Date).Days;

        return days <= 0 ? Today : days < 7 ? ThisWeek : Older;
    }
}
