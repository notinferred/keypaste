using System.Globalization;

namespace Keypaste.Core.Approval;

/// <summary>What a credential request's arguments must satisfy before anything is decided.</summary>
/// <remarks>
/// The vault's owner applies this to every request it is sent, and the bridge applies it too so it can
/// name the argument to the agent (D-0324). A process that speaks the approver protocol without the
/// bridge meets the same limits.
/// </remarks>
public static class CredentialRequestRules
{
    /// <summary>The longest <c>entry</c> argument accepted.</summary>
    public const int MaximumEntryLength = 512;

    /// <summary>The longest <c>reason</c> accepted.</summary>
    public const int MaximumReasonLength = 2000;

    /// <summary>Finds the first argument a request breaks the rules with.</summary>
    /// <param name="entry">The entry, handle or path.</param>
    /// <param name="field">The field asked for.</param>
    /// <param name="reason">The agent's reason.</param>
    /// <returns>The argument and its rule, or null when the request is well formed.</returns>
    public static CredentialRequestProblem? Check(string entry, string field, string reason) => Check(entry, field, null, reason);

    /// <summary>Checks a request's arguments, an attachment's name among them.</summary>
    /// <param name="entry">The <c>entry</c> argument.</param>
    /// <param name="field">The <c>field</c> argument.</param>
    /// <param name="attachment">The attachment's name, which a request for <see cref="CredentialFields.Attachment"/> needs and no other may give.</param>
    /// <param name="reason">The <c>reason</c> argument.</param>
    /// <returns>The first problem, or null.</returns>
    public static CredentialRequestProblem? Check(string entry, string field, string? attachment, string reason)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(reason);

        if (entry.Length is 0 or > MaximumEntryLength)
        {
            return new("entry", Length(MaximumEntryLength));
        }

        if (!CredentialFields.IsRequestable(field))
        {
            return new("field", $"must be {CredentialFields.RequestRule}");
        }

        if (string.Equals(field, CredentialFields.Attachment, StringComparison.Ordinal)
            ? attachment is not { Length: > 0 and <= MaximumEntryLength }
            : attachment is not null)
        {
            return new("name", string.Equals(field, CredentialFields.Attachment, StringComparison.Ordinal) ? Length(MaximumEntryLength) : "is given only for an attachment");
        }

        if (reason.Length is 0 or > MaximumReasonLength)
        {
            return new("reason", Length(MaximumReasonLength));
        }

        return null;
    }

    private static string Length(int maximum) =>
        string.Create(CultureInfo.InvariantCulture, $"must be 1 to {maximum} characters");
}

/// <summary>An argument a credential request got wrong.</summary>
/// <param name="Argument">The argument's name as the tool schema spells it.</param>
/// <param name="Rule">What it must be, completing a sentence that begins with the argument.</param>
public sealed record CredentialRequestProblem(string Argument, string Rule);
