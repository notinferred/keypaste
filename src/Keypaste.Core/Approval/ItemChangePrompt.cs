using System.Globalization;

namespace Keypaste.Core.Approval;

/// <summary>
/// What a person is shown when an agent asks them to allow a change to an item: who asks, the item, the change in
/// keypaste's own words and the agent's reason, already sanitized, and never a value.
/// </summary>
/// <remarks>It has no member for a value, an always, a default button or a layout (THREATS.md T-2, T-57).</remarks>
public sealed record ItemChangePrompt
{
    /// <summary>The client's asserted name, sanitized.</summary>
    public required string Client { get; init; }

    /// <summary>The bridge's label, sanitized, or null.</summary>
    public string? Label { get; init; }

    /// <summary>Who is asking, as the requester line says it: verified only from what the system said of the connection, never from the label.</summary>
    public RequesterIdentity Identity
    {
        get => _identity ?? new RequesterIdentity(Client, null, false);
        init => _identity = value;
    }

    private readonly RequesterIdentity? _identity;

    /// <summary>The item's own title, sanitized, which a prompt names it by; <see cref="Item"/> is shown under Details.</summary>
    public string ItemTitle
    {
        get => _itemTitle ?? Item[(Item.LastIndexOf('/') + 1)..];
        init => _itemTitle = value;
    }

    private readonly string? _itemTitle;

    /// <summary>The item's path, sanitized.</summary>
    public required string Item { get; init; }

    /// <summary>The change.</summary>
    public required ItemChangeKind Change { get; init; }

    /// <summary>With <see cref="ItemChangeKind.MarkExpired"/>, the day it expires, or null for now.</summary>
    public DateOnly? Expires { get; init; }

    /// <summary>With <see cref="ItemChangeKind.Delete"/>, the day the agent's approved save created the item, on this computer's clock.</summary>
    public DateOnly? Created { get; init; }

    /// <summary>The agent's reason, sanitized and capped. Untrusted text.</summary>
    public required string Reason { get; init; }

    /// <summary>Whether the reason was cut short.</summary>
    public bool ReasonWasTruncated { get; init; }

    /// <summary>Whether the reason or the item had to be scrubbed.</summary>
    public bool WasAltered { get; init; }

    /// <summary>Whether allowing deletes the item for good, which no Trash holds.</summary>
    public bool DeletesForGood => Change == ItemChangeKind.Delete;

    /// <summary>The change in keypaste's own words, as every channel shows it.</summary>
    public string Says => Change switch
    {
        ItemChangeKind.MoveToTrash => $"Move {Item} to Trash",
        ItemChangeKind.MarkExpired when Expires is { } day => $"Mark {Item} as expired on {Day(day)}",
        ItemChangeKind.MarkExpired => $"Mark {Item} as expired now",
        ItemChangeKind.Delete when Created is { } created => $"Delete {Item} for good. An agent created it on {Day(created)}",
        ItemChangeKind.Delete => $"Delete {Item} for good. An agent created it",
        _ => throw new InvalidOperationException($"{Change} is made without asking, so no person is shown it."),
    };

    /// <summary>The prompt for one request, sanitizing everything untrusted on the way in.</summary>
    /// <param name="client">The client's asserted name, or null.</param>
    /// <param name="label">The bridge's <c>--client-label</c>, or null.</param>
    /// <param name="item">The item, by its real name.</param>
    /// <param name="change">The change.</param>
    /// <param name="reason">The agent's stated reason, verbatim.</param>
    /// <returns>A prompt safe for any channel to render.</returns>
    /// <exception cref="ArgumentException">The change is made without asking.</exception>
    public static ItemChangePrompt For(string? client, string? label, EntryName item, ItemChange change, string reason)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(reason);

        if (!change.Asks)
        {
            throw new ArgumentException($"{change.Kind} is made without asking, so no person is shown it.", nameof(change));
        }

        var asked = ApprovalPrompt.For(client, item, SaveRequestRules.Password, reason, label);

        return new ItemChangePrompt
        {
            Client = asked.Client,
            Label = asked.Label,
            Identity = asked.Identity,
            Item = asked.Entry,
            ItemTitle = asked.ItemTitle,
            Change = change.Kind,
            Expires = change.Expires,
            Reason = asked.Reason,
            ReasonWasTruncated = asked.ReasonWasTruncated,
            WasAltered = asked.EntryWasAltered || asked.ReasonWasAltered,
        };
    }

    private static string Day(DateOnly day) => day.ToString(ItemChangeRules.DayFormat, CultureInfo.InvariantCulture);
}
