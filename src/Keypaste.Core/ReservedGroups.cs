namespace Keypaste.Core;

/// <summary>The groups keypaste keeps its own records in: never named to an agent, never listed as a secret.</summary>
/// <remarks>Ordinary KDBX groups, so KeePassXC shows and preserves them; keypaste hides them from its own lists.</remarks>
public static class ReservedGroups
{
    /// <summary>The root of every reserved group.</summary>
    public const string Root = ".keypaste";

    /// <summary>Scoped tokens: one entry per token, holding a verifier and never the token.</summary>
    public const string Tokens = Root + "/tokens";

    /// <summary>Share links: one entry per link, holding its revoke token and never its key.</summary>
    public const string Shares = Root + "/shares";

    /// <summary>Agents' access: one entry per agent, titled with its name and holding its ACAF record.</summary>
    public const string Agents = Root + "/agents";

    /// <summary>Browsers' pairings: one entry per pairing, titled with its name and holding a hash of the extension's key, never the key.</summary>
    public const string Browsers = Root + "/browsers";

    /// <summary>Whether a group path is reserved or inside a reserved group.</summary>
    /// <param name="groupPath">A group path as <see cref="VaultEntry.GroupPath"/> spells it.</param>
    /// <returns><see langword="true"/> for <see cref="Root"/> and everything under it, in any case.</returns>
    /// <remarks>Case-insensitive so a <c>.Keypaste</c> group made elsewhere is hidden and never a way to plant records.</remarks>
    public static bool IsReserved(string groupPath)
    {
        ArgumentNullException.ThrowIfNull(groupPath);

        return string.Equals(groupPath, Root, StringComparison.OrdinalIgnoreCase)
            || groupPath.StartsWith(Root + "/", StringComparison.OrdinalIgnoreCase);
    }
}
