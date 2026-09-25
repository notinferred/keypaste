using System.Globalization;
using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>
/// One line of the trash: what was deleted, where it came from and when, and the identity that
/// puts it back.
/// </summary>
/// <remarks>
/// <para>
/// <b>It carries no field value</b>, because <see cref="RecycledEntry"/> carries none: a recovery
/// list holds every row for as long as the screen is open, and a password among them would be the
/// widest surface in the app. What the person is choosing between here is names, and a restored
/// entry is read back through the entry list like any other.
/// </para>
/// <para>
/// <see cref="Id"/> is the address (DECISIONS.md D-0250). Two entries called <c>TOKEN</c> deleted
/// from two projects land in one bin, so neither the title nor the row's position can say which
/// one a person picked.
/// </para>
/// </remarks>
internal sealed class TrashRow
{
    internal TrashRow(RecycledEntry recycled)
    {
        ArgumentNullException.ThrowIfNull(recycled);

        Id = recycled.Id;
        Title = recycled.Title;
        OriginalGroupPath = recycled.OriginalGroupPath;
        DeletedUtc = recycled.DeletedUtc;
    }

    /// <summary>The identity to restore or delete for good by.</summary>
    internal RecycledEntryId Id { get; }

    /// <summary>The title the vault holds, verbatim. Never drawn; see <see cref="DisplayTitle"/>.</summary>
    internal string Title { get; }

    /// <summary>The group it was deleted from, or null when the vault does not record one.</summary>
    internal string? OriginalGroupPath { get; }

    /// <summary>When it was moved to the bin.</summary>
    internal DateTime DeletedUtc { get; }

    /// <summary>The title as the list draws it, scrubbed of anything that misrepresents it.</summary>
    internal string DisplayTitle => EntryNameSanitizer.Sanitize(Title).Text;

    /// <summary>Whether the group this came from is still in the vault.</summary>
    /// <remarks>
    /// An empty path is the root, which is a group; null is the bin having no live group to name,
    /// and a restore then lands at the root. The row says which, because that is where the entry
    /// will reappear.
    /// </remarks>
    internal bool CameFromALiveGroup => OriginalGroupPath is not null;

    /// <summary>Where it came from, as the list draws it.</summary>
    internal string Where => OriginalGroupPath switch
    {
        null => "a group that is gone",
        { Length: 0 } => "the root",
        var path => EntryNameSanitizer.SanitizePath(path).Text,
    };

    /// <summary>When it was deleted, on this machine's clock as Settings dates a backup.</summary>
    internal string When =>
        DateTime.SpecifyKind(DeletedUtc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

    /// <summary>What the row says under its name.</summary>
    internal string Summary => CameFromALiveGroup
        ? $"from {Where} · {When}"
        : $"from {Where}, so it comes back at the root · {When}";
}
