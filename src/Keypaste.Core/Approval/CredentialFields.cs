using Keypaste.Core.Totp;

namespace Keypaste.Core.Approval;

/// <summary>The one rule for which fields leave by name: to an agent, a run's reference or a share.</summary>
/// <remarks>
/// A custom field leaves only when it is named like an environment variable
/// (<see cref="EnvConvention.IsEnvNamedField"/>), is no standard name in any case and is none a TOTP is read
/// from, so a field such as <c>Recovery codes</c>, <c>otp</c>, <c>TimeOtp-Secret</c> or <c>KP2A_URL_1</c> never
/// does. A credential request may also name <see cref="Totp"/>, which is no field.
/// </remarks>
public static class CredentialFields
{
    /// <summary>What a credential request names to be given the item's current TOTP code, never its seed (D-0480).</summary>
    /// <remarks>Lower-case, so no custom field that leaves by name can be called this.</remarks>
    public const string Totp = "totp";

    /// <summary>The standard releasable field names, lower-case, in the order the schema lists them.</summary>
    public static IReadOnlyList<string> All { get; } = ["password", "username", "url", "notes"];

    /// <summary>What a releasable field is, completing a sentence such as "the field must be …".</summary>
    public const string Rule = "password, username, url, notes or a custom field named like an environment variable";

    /// <summary>What a credential request may name, completing a sentence such as "the field must be …".</summary>
    public const string RequestRule = "password, username, url, notes, totp or a custom field named like an environment variable";

    /// <summary>Whether a field name is one keypaste releases.</summary>
    /// <param name="field">The field name exactly as it arrived.</param>
    /// <returns><see langword="true"/> for one of <see cref="All"/> or a custom field <see cref="IsCustom"/> accepts.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="field"/> is null.</exception>
    /// <remarks>Ordinal: <c>Password</c> and <c>URL</c> are neither a standard name as spelled here nor a custom one.</remarks>
    public static bool IsReleasable(string field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return IsStandard(field) || IsCustom(field);
    }

    /// <summary>Whether a credential request may name this: a field <see cref="IsReleasable"/> accepts, or <see cref="Totp"/>.</summary>
    /// <param name="field">The field name exactly as it arrived.</param>
    /// <returns><see langword="true"/> when the request may go on to be decided.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="field"/> is null.</exception>
    public static bool IsRequestable(string field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return IsReleasable(field) || string.Equals(field, Totp, StringComparison.Ordinal);
    }

    /// <summary>Whether a field name is one of <see cref="All"/>, spelled exactly.</summary>
    /// <param name="field">The field name.</param>
    /// <returns><see langword="true"/> for <c>password</c>, <c>username</c>, <c>url</c> or <c>notes</c>.</returns>
    public static bool IsStandard(string field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return All.Contains(field, StringComparer.Ordinal);
    }

    /// <summary>Whether a field name is a custom field keypaste releases.</summary>
    /// <param name="field">The field name.</param>
    /// <returns><see langword="true"/> when it is env-named, no standard name in any case and no field a TOTP seed is read from.</returns>
    public static bool IsCustom(string field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return EnvConvention.IsEnvNamedField(field) && !FieldNameRules.IsStandard(field) && !TotpSpec.IsSourceField(field);
    }
}
