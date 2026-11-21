namespace Keypaste.Core.Agents;

/// <summary>
/// The sites a vault's browser allowances name, as its rules file keeps them: each encrypted under a key derived from the vault's
/// agent-rules key, so the file names no site the person filled a login on, and only the process holding the unlocked vault reads them.
/// </summary>
/// <remarks>A site an earlier keypaste wrote in the clear is read as it is and encrypted by the owner's next sealed write.</remarks>
internal static class FillSites
{
    private const int _nonceBytes = 12;
    private const int _tagBytes = 16;

    // One block holds any origin a domain name allows, so hidden sites show no length, and once encrypted still fits the file's longest quoted value.
    private const int _block = 448;

    /// <summary>Rules whose every site is encrypted under the vault's key.</summary>
    /// <param name="rules">Rules naming their sites as origins.</param>
    /// <param name="key">The vault's agent-rules key.</param>
    /// <returns>The rules as the file keeps them.</returns>
    internal static AgentRules Hidden(AgentRules rules, byte[] key)
    {
        using var aes = new AesGcm(SiteKey(key), _tagBytes);
        return rules.WithBrowserAllowances(rules.BrowserAllowances.Select(allowance => IsHidden(allowance.Origin) ? allowance : allowance with { Origin = Hide(aes, allowance.Origin) }));
    }

    /// <summary>Rules whose every site is an origin again, an allowance whose site does not open under the key dropped, so its next fill is asked.</summary>
    /// <param name="rules">Rules as the file keeps them.</param>
    /// <param name="key">The vault's agent-rules key.</param>
    /// <returns>The rules as the owner decides by them.</returns>
    internal static AgentRules Shown(AgentRules rules, byte[] key)
    {
        using var aes = new AesGcm(SiteKey(key), _tagBytes);
        return rules.WithBrowserAllowances(rules.BrowserAllowances
            .Select(allowance => IsHidden(allowance.Origin) ? Show(aes, allowance.Origin) is { } origin ? allowance with { Origin = origin } : null : allowance)
            .OfType<BrowserAllowance>());
    }

    /// <summary>Whether a value is a site as this class hides it: base64 of a nonce, whole blocks and a tag.</summary>
    /// <param name="value">The value.</param>
    /// <returns>Whether it is.</returns>
    internal static bool IsHidden(string value)
    {
        var bytes = new byte[(value.Length * 3 / 4) + 3];
        return Convert.TryFromBase64String(value, bytes, out var length) && length > _nonceBytes + _tagBytes && (length - _nonceBytes - _tagBytes) % _block == 0;
    }

    private static string Hide(AesGcm aes, string origin)
    {
        var text = Encoding.UTF8.GetBytes(origin);
        var plain = new byte[((text.Length / _block) + 1) * _block];
        text.CopyTo(plain, 0);

        var sealedSite = new byte[_nonceBytes + plain.Length + _tagBytes];
        var nonce = sealedSite.AsSpan(0, _nonceBytes);
        RandomNumberGenerator.Fill(nonce);
        aes.Encrypt(nonce, plain, sealedSite.AsSpan(_nonceBytes, plain.Length), sealedSite.AsSpan(_nonceBytes + plain.Length));

        return Convert.ToBase64String(sealedSite);
    }

    private static string? Show(AesGcm aes, string hidden)
    {
        var sealedSite = Convert.FromBase64String(hidden);
        var plain = new byte[sealedSite.Length - _nonceBytes - _tagBytes];

        try
        {
            aes.Decrypt(sealedSite.AsSpan(0, _nonceBytes), sealedSite.AsSpan(_nonceBytes, plain.Length), sealedSite.AsSpan(_nonceBytes + plain.Length), plain);
        }
        catch (AuthenticationTagMismatchException)
        {
            return null;
        }

        var end = Array.IndexOf(plain, (byte)0);
        var origin = Encoding.UTF8.GetString(plain, 0, end < 0 ? plain.Length : end);
        return Browser.WebOrigin.IsOrigin(origin) ? origin : null;
    }

    private static byte[] SiteKey(byte[] key) => HKDF.DeriveKey(HashAlgorithmName.SHA256, key, 32, info: "keypaste agents.toml site"u8.ToArray());
}
