using System.Globalization;
using Keypaste.Core.Ipc;

namespace Keypaste.Core.Approval;

/// <summary>A change an agent may ask the person to make to an item (PRODUCT §2, THREATS.md T-57).</summary>
public enum ItemChangeKind
{
    /// <summary>Move the item to Trash, which only a vault whose Trash is on holds.</summary>
    MoveToTrash = 1,

    /// <summary>Set KeePass's own expiry on the item.</summary>
    MarkExpired = 2,

    /// <summary>Tag the item <see cref="ItemChangeRules.NeedsUpdateTag"/>.</summary>
    MarkNeedsUpdate = 3,

    /// <summary>Delete the item for good, which only an item the same agent created through an approved save may be.</summary>
    Delete = 4,
}

/// <summary>One change as the rules read it: what it is and, for an expiry, the day it names.</summary>
/// <param name="Kind">The change.</param>
/// <param name="Expires">With <see cref="ItemChangeKind.MarkExpired"/>, the day given, or null for now.</param>
public readonly record struct ItemChange(ItemChangeKind Kind, DateOnly? Expires);

/// <summary>The shape of an agent's request to change an item, checked by the bridge before it asks and by the owner again.</summary>
public static class ItemChangeRules
{
    /// <summary>The tool's name for <see cref="ItemChangeKind.MoveToTrash"/>, in KeePass's word for Trash.</summary>
    public const string MoveToRecycleBin = "move_to_recycle_bin";

    /// <summary>The tool's name for <see cref="ItemChangeKind.MarkExpired"/>.</summary>
    public const string MarkExpired = "mark_expired";

    /// <summary>The tool's name for <see cref="ItemChangeKind.MarkNeedsUpdate"/>.</summary>
    public const string MarkNeedsUpdate = "mark_needs_update";

    /// <summary>The tool's name for <see cref="ItemChangeKind.Delete"/>.</summary>
    public const string Delete = "delete";

    /// <summary>The tag that marks an item as needing an update.</summary>
    public const string NeedsUpdateTag = "needs-update";

    /// <summary>How an expiry's day is written.</summary>
    public const string DayFormat = "yyyy-MM-dd";

    /// <summary>Why a request names no change that can be put to the person, or null.</summary>
    /// <param name="request">The request.</param>
    /// <param name="change">The change it names, when there is no problem.</param>
    /// <returns>The argument and the rule it breaks, or null.</returns>
    public static CredentialRequestProblem? Check(ItemChangeRequest request, out ItemChange change)
    {
        ArgumentNullException.ThrowIfNull(request);

        change = default;

        if (CredentialRequestRules.Check(request.Entry, SaveRequestRules.Password, request.Reason) is { } problem)
        {
            return problem;
        }

        ItemChangeKind? kind = request.Change switch
        {
            MoveToRecycleBin => ItemChangeKind.MoveToTrash,
            MarkExpired => ItemChangeKind.MarkExpired,
            MarkNeedsUpdate => ItemChangeKind.MarkNeedsUpdate,
            Delete => ItemChangeKind.Delete,
            _ => null,
        };

        if (kind is not { } named)
        {
            return new CredentialRequestProblem("change", $"is {MoveToRecycleBin}, {MarkExpired}, {MarkNeedsUpdate} or {Delete}");
        }

        if (request.Expires.Length == 0)
        {
            change = new ItemChange(named, null);
            return null;
        }

        if (named != ItemChangeKind.MarkExpired)
        {
            return new CredentialRequestProblem("expires", $"is given only with {MarkExpired}");
        }

        if (!DateOnly.TryParseExact(request.Expires, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return new CredentialRequestProblem("expires", "is a day written as yyyy-MM-dd, such as 2026-12-01");
        }

        change = new ItemChange(named, day);
        return null;
    }
}
