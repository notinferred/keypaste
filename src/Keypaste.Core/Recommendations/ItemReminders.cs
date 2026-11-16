using System.Globalization;

namespace Keypaste.Core.Recommendations;

/// <summary>Why Recommendations reminds the person of an item.</summary>
public enum ReminderReason
{
    /// <summary>The item's expiry date falls within the days Settings reminds before, and has not passed.</summary>
    ExpiresSoon,

    /// <summary>The item carries the needs-update tag, which the person or any agent may set.</summary>
    NeedsUpdate,
}

/// <summary>One item to replace before it expires, or marked as needing an update, named by item and never by value.</summary>
/// <param name="Entry">The item.</param>
/// <param name="EntryUuid">The item's KDBX identifier as hex, which survives a rename.</param>
/// <param name="Reason">Why it is listed.</param>
/// <param name="When">When it expires, for <see cref="ReminderReason.ExpiresSoon"/>; when the version adding the tag was saved, for <see cref="ReminderReason.NeedsUpdate"/>, or null when its history no longer says.</param>
/// <param name="Page">Where a new credential is made, or null when the item has no URL keypaste opens.</param>
public sealed record ItemReminder(EntryName Entry, string EntryUuid, ReminderReason Reason, DateTimeOffset? When, NewCredentialPage? Page)
{
    /// <summary>What a dismissal remembers: the reason and its time, so a changed expiry or a new tag is listed again.</summary>
    public string Key => string.Create(
        CultureInfo.InvariantCulture,
        $"{(Reason == ReminderReason.ExpiresSoon ? "expires" : "needs-update")}@{When?.UtcDateTime:O}");
}

/// <summary>Finds items expiring within the days Settings reminds before, and items tagged needs-update, with no network request.</summary>
/// <remarks>
/// It reads each live item's expiry, URL, kind and tags outside keypaste's own groups, never a value. An expiry that has passed is
/// <see cref="PasswordHealthCheck"/>'s, and an item KeePass or KeePassXC leaves out of reports is not reminded of its expiry, as
/// it is not reported expired.
/// </remarks>
public static class ItemReminderCheck
{
    /// <summary>Checks every live item outside keypaste's own groups.</summary>
    /// <param name="vault">The unlocked vault.</param>
    /// <param name="now">The time an expiry is compared with.</param>
    /// <param name="expiryDays">How many days before its expiry an item is listed; 0 lists none.</param>
    /// <returns>The reminders, by item in tree order and then by reason.</returns>
    public static IReadOnlyList<ItemReminder> Scan(Vault vault, DateTimeOffset now, int expiryDays)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentOutOfRangeException.ThrowIfNegative(expiryDays);

        List<ItemReminder> reminders = [];

        foreach (var item in vault.ReadReminderFacts())
        {
            if (expiryDays > 0 && !item.Excluded && item.Expires is { } expires && expires > now && expires <= now.AddDays(expiryDays))
            {
                reminders.Add(new ItemReminder(item.Entry, item.Uuid, ReminderReason.ExpiresSoon, expires, NewCredentialPages.For(item.Url, item.Kind)));
            }

            if (item.NeedsUpdate)
            {
                reminders.Add(new ItemReminder(item.Entry, item.Uuid, ReminderReason.NeedsUpdate, item.TaggedSince, NewCredentialPages.For(item.Url, item.Kind)));
            }
        }

        return reminders;
    }
}

/// <summary>One live item's expiry, URL, kind and needs-update tag, as the reminder check reads them.</summary>
/// <param name="Entry">The item.</param>
/// <param name="Uuid">Its KDBX identifier as hex.</param>
/// <param name="Url">Its URL field as stored.</param>
/// <param name="Kind">The kind its icon keeps.</param>
/// <param name="Expires">When it expires, or null when it does not.</param>
/// <param name="Excluded">Whether it is left out of quality checks and reports.</param>
/// <param name="NeedsUpdate">Whether it carries the needs-update tag.</param>
/// <param name="TaggedSince">When the version adding that tag was saved, or null when it carries none or its history no longer says.</param>
internal sealed record EntryReminderFacts(
    EntryName Entry,
    string Uuid,
    string Url,
    ItemKind Kind,
    DateTimeOffset? Expires,
    bool Excluded,
    bool NeedsUpdate,
    DateTimeOffset? TaggedSince);
