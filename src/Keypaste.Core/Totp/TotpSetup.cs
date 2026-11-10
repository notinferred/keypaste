namespace Keypaste.Core.Totp;

/// <summary>What a person gives to set up an item's TOTP, and the <c>otp</c> URI KeePassXC 2.8 keeps it as.</summary>
/// <remarks>
/// A setup is an <c>otpauth://totp/</c> URI, kept as given, or a base32 setup key, kept as KeePassXC writes one with
/// its defaults of SHA-1, 6 digits and 30 seconds. Either is accepted only once <see cref="TotpSpec"/> computes a code
/// from the URI that would be kept, and refused with its refusal, which names a field and never a value.
/// </remarks>
public static class TotpSetup
{
    private const string _scheme = "otpauth:";

    /// <summary>Whether a setup gives a TOTP keypaste computes.</summary>
    /// <param name="setup">An <c>otpauth://totp/</c> URI or a base32 setup key.</param>
    /// <param name="refusal">Why it is refused, naming no value; empty when it is accepted.</param>
    /// <returns><see langword="true"/> when <see cref="Vault.SetTotp"/> would keep it.</returns>
    public static bool IsAccepted(string setup, out string refusal)
    {
        ArgumentNullException.ThrowIfNull(setup);

        var accepted = Compose(setup, string.Empty, out var refused) is not null;
        refusal = refused ?? string.Empty;
        return accepted;
    }

    /// <summary>The <c>otp</c> URI a setup is kept as, or null when it is refused.</summary>
    /// <param name="setup">An <c>otpauth://totp/</c> URI or a base32 setup key.</param>
    /// <param name="title">The item's title, which labels a URI made from a setup key as KeePassXC labels one.</param>
    /// <param name="refusal">Why it is refused, naming no value; null when it is accepted.</param>
    internal static string? Compose(string setup, string title, out string? refusal)
    {
        var text = setup.Trim();

        if (text.Length == 0)
        {
            refusal = "Type or paste the setup key or otpauth://totp/ link the site gave you.";
            return null;
        }

        var uri = text.StartsWith(_scheme, StringComparison.OrdinalIgnoreCase) ? text : FromKey(text, title);

        using var spec = TotpSpec.FromUri(uri, out refusal);

        return spec is null ? null : uri;
    }

    /// <summary>A setup key as a URI in the form KeePassXC 2.8 writes, labelled by the title alone: <c>otpauth://totp/&lt;title&gt;?secret=…&amp;period=30&amp;digits=6&amp;issuer=&lt;title&gt;</c>.</summary>
    private static string FromKey(string key, string title)
    {
        var secret = InvariantUpper.Of(key.Replace(" ", string.Empty, StringComparison.Ordinal).TrimEnd('='));
        var label = Uri.EscapeDataString(title.Length == 0 ? "keypaste" : title);

        return $"otpauth://totp/{label}?secret={Uri.EscapeDataString(secret)}&period=30&digits=6&issuer={label}";
    }
}
