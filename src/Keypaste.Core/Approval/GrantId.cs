namespace Keypaste.Core.Approval;

/// <summary>The short name a person types to revoke one grant: eight lowercase hex characters.</summary>
/// <remarks>
/// Derived from the grant's key rather than minted, so the terminal and the desktop name the same
/// grant the same way without either remembering anything. It is a label, not a secret: whoever can
/// reach the owner to revoke by it can already list it.
/// </remarks>
public static class GrantId
{
    /// <summary>How many characters an id has.</summary>
    public const int Length = 8;

    /// <summary>The id of a credential grant.</summary>
    /// <param name="key">The connection, entry and field the grant answers.</param>
    /// <returns>Eight lowercase hex characters.</returns>
    public static string Of(GrantKey key)
    {
        ArgumentNullException.ThrowIfNull(key.ConnectionId);
        ArgumentNullException.ThrowIfNull(key.Handle);
        ArgumentNullException.ThrowIfNull(key.Field);

        return Hash($"credential\0{key.ConnectionId}\0{key.Handle}\0{key.Field}");
    }

    /// <summary>The id of a timed grant given to a repeated <c>keypaste run --session</c>.</summary>
    /// <param name="envKey">The request identity the grant answers.</param>
    /// <returns>Eight lowercase hex characters.</returns>
    public static string OfEnv(string envKey)
    {
        ArgumentNullException.ThrowIfNull(envKey);

        return Hash($"env\0{envKey}");
    }

    /// <summary>Whether text has the shape of an id, in any case.</summary>
    /// <param name="text">What was typed.</param>
    /// <returns><see langword="true"/> for exactly eight hex characters.</returns>
    public static bool IsId(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.Length == Length && text.All(char.IsAsciiHexDigit);
    }

    private static string Hash(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..Length];
}
