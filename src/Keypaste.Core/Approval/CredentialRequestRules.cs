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
    /// <param name="ttlSeconds">The lifetime asked for.</param>
    /// <returns>The argument and its rule, or null when the request is well formed.</returns>
    public static CredentialRequestProblem? Check(string entry, string field, string reason, int ttlSeconds)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(reason);

        if (entry.Length is 0 or > MaximumEntryLength)
        {
            return new("entry", Length(MaximumEntryLength));
        }

        if (!CredentialFields.IsReleasable(field))
        {
            return new("field", $"must be one of: {string.Join(", ", CredentialFields.All)}");
        }

        if (reason.Length is 0 or > MaximumReasonLength)
        {
            return new("reason", Length(MaximumReasonLength));
        }

        if (ttlSeconds is < 1 or > ApprovalLimits.MaximumRequestableTtlSeconds)
        {
            return new(
                "ttl_seconds",
                string.Create(CultureInfo.InvariantCulture, $"must be between 1 and {ApprovalLimits.MaximumRequestableTtlSeconds}"));
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
