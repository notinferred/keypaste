namespace Keypaste.Core.Approval;

/// <summary>The fields keypaste will release to an agent, and nothing else.</summary>
/// <remarks>
/// The list lives in the core because it is a product rule, not a protocol detail: the MCP tool
/// schema advertises it, the server re-validates against it, the approval prompt names it, and
/// <c>keypaste log</c> renders it. docs/PRODUCT.md law 4.3 does not allow that written down four times.
/// <para>
/// Custom KDBX string fields are deliberately absent. They are where users keep recovery codes and
/// notes-to-self, and widening the release surface to "whatever the entry happens to have" is a
/// decision that needs its own argument rather than an <c>else</c> branch.
/// </para>
/// </remarks>
public static class CredentialFields
{
    /// <summary>The releasable field names, lower-case, in the order the schema lists them.</summary>
    public static IReadOnlyList<string> All { get; } = ["password", "username", "url", "notes"];

    /// <summary>Whether a field name is one keypaste releases.</summary>
    /// <param name="field">The <c>field</c> argument exactly as it arrived.</param>
    /// <returns><see langword="true"/> when it is one of <see cref="All"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="field"/> is null.</exception>
    /// <remarks>
    /// Ordinal and case-sensitive. The schema spells these in lower case and the tool re-validates
    /// against the same list, so accepting <c>Password</c> here would only widen what one half of
    /// the pair believes is legal.
    /// </remarks>
    public static bool IsReleasable(string field)
    {
        ArgumentNullException.ThrowIfNull(field);

        for (var i = 0; i < All.Count; i++)
        {
            if (string.Equals(All[i], field, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
