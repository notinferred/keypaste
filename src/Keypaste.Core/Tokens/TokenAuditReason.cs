using System.Diagnostics.CodeAnalysis;

namespace Keypaste.Core.Tokens;

/// <summary>How an owner's token audit line words its reason, and how a reader gets the token's name back.</summary>
/// <remarks>One format, written and read here, so a screen naming "ci-staging · token" cannot drift from the line.</remarks>
public static class TokenAuditReason
{
    private const string _prefix = "token ";

    /// <summary>The reason a token line carries.</summary>
    /// <param name="id">The token's id.</param>
    /// <param name="name">Its name, sanitized for display.</param>
    /// <param name="said">What happened.</param>
    /// <returns><c>token &lt;id&gt; '&lt;name&gt;': &lt;said&gt;</c>.</returns>
    public static string Format(string id, string name, string said)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(said);

        return $"{_prefix}{id} '{name}': {said}";
    }

    /// <summary>The token's name in a reason <see cref="Format"/> wrote.</summary>
    /// <param name="reason">A line's reason.</param>
    /// <param name="name">The name, when the reason has one.</param>
    /// <returns>Whether it did.</returns>
    public static bool TryName(string reason, [NotNullWhen(true)] out string? name)
    {
        ArgumentNullException.ThrowIfNull(reason);

        name = null;

        if (!reason.StartsWith(_prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var open = reason.IndexOf(" '", StringComparison.Ordinal);
        var close = reason.IndexOf("': ", StringComparison.Ordinal);

        if (open < 0 || close <= open + 2)
        {
            return false;
        }

        name = reason[(open + 2)..close];
        return true;
    }
}
