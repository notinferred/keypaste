namespace Keypaste.Core.Approval;

/// <summary>The one rule for which fields leave by name: to an agent, a run's reference, a policy rule or a share.</summary>
/// <remarks>
/// A custom field leaves only when it is named like an environment variable
/// (<see cref="EnvConvention.IsEnvNamedField"/>) and is no standard name in any case, so a field such
/// as <c>Recovery codes</c>, <c>otp</c> or <c>KP2A_URL_1</c> never does.
/// </remarks>
public static class CredentialFields
{
    /// <summary>The standard releasable field names, lower-case, in the order the schema lists them.</summary>
    public static IReadOnlyList<string> All { get; } = ["password", "username", "url", "notes"];

    /// <summary>What a releasable field is, completing a sentence such as "the field must be …".</summary>
    public const string Rule = "password, username, url, notes or a custom field named like an environment variable";

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
    /// <returns><see langword="true"/> when it is env-named and no standard name in any case.</returns>
    public static bool IsCustom(string field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return EnvConvention.IsEnvNamedField(field) && !FieldNameRules.IsStandard(field);
    }
}
