using System.Globalization;
using Keypaste.Core.Ipc;

namespace Keypaste.Core.Approval;

/// <summary>What a linked set's arguments must satisfy before anything is decided, applied by the bridge and again by the owner.</summary>
public static class SetRequestRules
{
    /// <summary>The fewest fields a set names; one field is <c>request_credential</c>'s.</summary>
    public const int MinimumFields = 2;

    /// <summary>The most fields one prompt releases (PRODUCT §2).</summary>
    public const int MaximumFields = 4;

    /// <summary>Finds the first argument a set breaks the rules with.</summary>
    /// <param name="items">The fields asked for, the anchor's first.</param>
    /// <param name="reason">The agent's reason.</param>
    /// <returns>The argument and its rule, or null when the set is well formed.</returns>
    public static CredentialRequestProblem? Check(IReadOnlyList<SetItem> items, string reason)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(reason);

        if (items.Count is < MinimumFields or > MaximumFields)
        {
            return new("items", string.Create(CultureInfo.InvariantCulture, $"must name {MinimumFields} to {MaximumFields} fields"));
        }

        foreach (var item in items)
        {
            if (string.Equals(item.Field, CredentialFields.Attachment, StringComparison.Ordinal))
            {
                return new("field", "must not be an attachment; ask for one with request_attachment");
            }

            if (CredentialRequestRules.Check(item.Entry, item.Field, null, reason) is { } problem)
            {
                return problem;
            }
        }

        if (items.Distinct().Count() != items.Count)
        {
            return new("items", "must name each field once");
        }

        return null;
    }
}
