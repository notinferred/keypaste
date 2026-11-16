using Keypaste.Core.Agents;
using Keypaste.Core.Approval;

namespace Keypaste.Core.Editing;

/// <summary>What an agent's change to an item would do, decided before the person is asked or an unasked change is made (THREATS.md T-57).</summary>
public sealed record ItemChangeCheck
{
    /// <summary>Why it cannot be made, in keypaste's words, or null.</summary>
    public string? Refusal { get; init; }

    /// <summary>The item, carrying its UUID, so the change reaches the item the person saw and no other.</summary>
    public EntryName? Item { get; init; }

    /// <summary>With a delete, the day the agent's approved save created the item, on this computer's clock.</summary>
    public DateOnly? Created { get; init; }
}

/// <summary>
/// Makes a change an agent asked for: moving an item to Trash, setting KeePass's expiry or deleting for good an item the same
/// agent's approved save created, each once the person allowed it, or adding or removing the
/// <see cref="ItemChangeRules.NeedsUpdateTag"/> tag, which nobody is asked about.
/// </summary>
/// <remarks>Nothing here saves the vault or reads a value it holds; the caller saves under its claim.</remarks>
public static class ItemChanges
{
    /// <summary>Decides whether the change can be made to the item, changing nothing.</summary>
    /// <param name="vault">The open vault.</param>
    /// <param name="name">The item, as the request resolved.</param>
    /// <param name="change">The change.</param>
    /// <param name="agent">The asking bridge's raw label, or null.</param>
    /// <param name="zone">The zone a created day is read in.</param>
    /// <returns>A refusal, or the item to change.</returns>
    /// <exception cref="VaultException">More than one item answers to the name.</exception>
    public static ItemChangeCheck Check(Vault vault, EntryName name, ItemChange change, string? agent, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(zone);

        if (vault.EntryUuid(name) is not { } uuid)
        {
            return new ItemChangeCheck { Refusal = "no item answers to that name" };
        }

        var item = name with { Uuid = uuid };

        switch (change.Kind)
        {
            case ItemChangeKind.MoveToTrash when !vault.RecyclesDeletedEntries:
                return new ItemChangeCheck { Refusal = "this vault's Trash is off, so moving the item there would delete it for good" };

            case ItemChangeKind.MarkNeedsUpdate when vault.Tags(item)?.Contains(ItemChangeRules.NeedsUpdateTag, StringComparer.Ordinal) == true:
                return new ItemChangeCheck { Refusal = $"the item is already tagged {ItemChangeRules.NeedsUpdateTag}" };

            case ItemChangeKind.ClearNeedsUpdate when vault.Tags(item)?.Contains(ItemChangeRules.NeedsUpdateTag, StringComparer.Ordinal) != true:
                return new ItemChangeCheck { Refusal = $"the item is not tagged {ItemChangeRules.NeedsUpdateTag}" };

            case ItemChangeKind.Delete:
                if (!AgentNames.IsNamed(agent) || !string.Equals(vault.AgentThatCreated(item), agent, StringComparison.Ordinal))
                {
                    return new ItemChangeCheck { Refusal = "an agent deletes for good only an item it created itself through a save the person approved, and this item is not one" };
                }

                var created = vault.ReadTimes(item)?.Created;
                return new ItemChangeCheck { Item = item, Created = created is { } at ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime) : null };
        }

        return new ItemChangeCheck { Item = item };
    }

    /// <summary>Makes the change, checking first that the item is still the one, by UUID and name, that was checked. Does not save the vault.</summary>
    /// <param name="vault">The open vault.</param>
    /// <param name="change">The change.</param>
    /// <param name="agent">The asking bridge's raw label, or null.</param>
    /// <param name="shown">The check the person saw, or that an unasked change made.</param>
    /// <param name="now">The time, which an expiry given no day takes.</param>
    /// <param name="zone">The zone an expiry's day starts in.</param>
    /// <returns>Null once the vault holds the change, otherwise why nothing was changed.</returns>
    /// <exception cref="VaultException">The vault refused the write.</exception>
    public static string? Apply(Vault vault, ItemChange change, string? agent, ItemChangeCheck shown, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(shown);
        ArgumentNullException.ThrowIfNull(zone);

        if (shown.Item is not { Uuid: { } uuid } item
            || vault.EntryNameOf(uuid) is not { } current
            || !string.Equals(current.GroupPath, item.GroupPath, StringComparison.Ordinal)
            || !string.Equals(current.Title, item.Title, StringComparison.Ordinal))
        {
            return "the item was moved, renamed or deleted while you were asked; nothing was changed";
        }

        if (Check(vault, item, change, agent, zone).Refusal is { } refusal)
        {
            return refusal;
        }

        var made = change.Kind switch
        {
            ItemChangeKind.MoveToTrash => vault.RemoveEntry(item) == DeletionOutcome.Recycled,
            ItemChangeKind.MarkExpired => vault.SetExpiry(item, change.Expires is { } day ? StartOf(day, zone) : now),
            ItemChangeKind.MarkNeedsUpdate => vault.AddTag(item, ItemChangeRules.NeedsUpdateTag),
            ItemChangeKind.ClearNeedsUpdate => vault.RemoveTags(item, [ItemChangeRules.NeedsUpdateTag]),
            ItemChangeKind.Delete => vault.DeleteForGood(item),
            _ => false,
        };

        return made ? null : "the vault did not take the change";
    }

    private static DateTimeOffset StartOf(DateOnly day, TimeZoneInfo zone)
    {
        var midnight = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight));
    }
}
