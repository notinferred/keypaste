namespace Keypaste.Core.Totp;

/// <summary>What a person gives to set up an item's TOTP, and the <c>otp</c> URI keypaste keeps it as, which KeePassXC 2.8 reads.</summary>
/// <remarks>
/// A setup is an <c>otpauth://totp/</c> URI, kept as given, or a base32 setup key, kept as a URI with KeePassXC's
/// defaults of SHA-1, 6 digits and 30 seconds. Either is accepted only once <see cref="TotpSpec"/> computes a code
/// from the URI that would be kept, and refused in words about the setup key or the link, never naming a value.
/// </remarks>
public static class TotpSetup
{
    private const string _scheme = "otpauth:";

    private static readonly string _otpFieldRefusal = $"The {TotpSpec.OtpField} field";

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

    /// <summary>Whether <see cref="Vault.RemoveTotp"/> removes a custom field of this name: <c>otp</c>, <c>TOTP Seed</c> or <c>TOTP Settings</c>.</summary>
    /// <param name="fieldName">The field's name.</param>
    public static bool Removes(string fieldName) => TotpSpec.KeePassXcFields.Contains(fieldName, StringComparer.Ordinal);

    /// <summary>The <c>otp</c> URI a setup is kept as, or null when it is refused.</summary>
    /// <param name="setup">An <c>otpauth://totp/</c> URI or a base32 setup key.</param>
    /// <param name="title">The item's title, which labels a URI made from a setup key.</param>
    /// <param name="refusal">Why it is refused, naming no value; null when it is accepted.</param>
    internal static string? Compose(string setup, string title, out string? refusal)
    {
        var text = setup.Trim();

        if (text.Length == 0)
        {
            refusal = "Type or paste the setup key or otpauth://totp/ link the site gave you.";
            return null;
        }

        var isLink = text.StartsWith(_scheme, StringComparison.OrdinalIgnoreCase);
        var uri = isLink ? text : FromKey(text, title);

        using var spec = TotpSpec.FromUri(uri, out refusal);

        if (spec is not null)
        {
            return uri;
        }

        // The person typed a key or a link, never a field, so the refusal speaks of what they typed.
        refusal = isLink
            ? refusal?.Replace(_otpFieldRefusal, "The link", StringComparison.Ordinal)
            : "The setup key must be base32, the letters A to Z and the digits 2 to 7, or an otpauth://totp/ link.";
        return null;
    }

    /// <summary>
    /// A setup key as the URI keypaste writes for it, labelled by the title alone, which KeePassXC 2.8 reads:
    /// <c>otpauth://totp/&lt;title&gt;?secret=…&amp;period=30&amp;digits=6&amp;issuer=&lt;title&gt;</c>.
    /// </summary>
    private static string FromKey(string key, string title)
    {
        var grouped = key.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        var secret = InvariantUpper.Of(grouped.TrimEnd('='));
        var label = Uri.EscapeDataString(title.Length == 0 ? "keypaste" : title);

        return $"otpauth://totp/{label}?secret={Uri.EscapeDataString(secret)}&period=30&digits=6&issuer={label}";
    }
}
