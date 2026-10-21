namespace Keypaste.Core.Totp;

/// <summary>The HMAC an item's TOTP codes are computed with.</summary>
public enum TotpAlgorithm
{
    /// <summary>HMAC-SHA-1, the default of RFC 6238, KeePassXC and KeePass 2.</summary>
    Sha1,

    /// <summary>HMAC-SHA-256.</summary>
    Sha256,

    /// <summary>HMAC-SHA-512.</summary>
    Sha512,
}
