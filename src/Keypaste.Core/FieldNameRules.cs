namespace Keypaste.Core;

/// <summary>The custom-field names keypaste is willing to write.</summary>
/// <remarks>
/// Enforced on write and never on read, as <see cref="VaultNameRules"/> is: a field KeePassXC wrote
/// under any name stays listed and readable. A standard name is refused because KeePass keeps one
/// of each and they are written through <see cref="Vault.UpdateEntry"/>. KeePassXC's own attributes
/// are refused because KeePassXC gives them a meaning — a TOTP seed, a command to run, browser
/// settings — that a value written here would change behind the person's back.
/// </remarks>
public static class FieldNameRules
{
    private static readonly string[] _standard = ["Title", "UserName", "Password", "URL", "Notes"];
    private static readonly string[] _keePassXcNames = ["otp", "TOTP Seed", "TOTP Settings", "_EXEC_CMD"];
    private static readonly string[] _keePassXcPrefixes = ["KP2A_URL", "KPEX_", "KPXC_"];

    /// <summary>Whether a name is one of KeePass's five standard fields, in any case.</summary>
    /// <param name="name">The name to check.</param>
    /// <returns><see langword="true"/> for Title, UserName, Password, URL or Notes however cased.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public static bool IsStandard(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return _standard.Any(standard => string.Equals(standard, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether a name is one KeePassXC gives its own meaning to, in any case.</summary>
    /// <param name="name">The name to check.</param>
    /// <returns><see langword="true"/> for <c>otp</c>, <c>TOTP Seed</c>, <c>TOTP Settings</c>, <c>_EXEC_CMD</c> and names starting <c>KP2A_URL</c>, <c>KPEX_</c> or <c>KPXC_</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public static bool IsKeePassXcAttribute(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return _keePassXcNames.Any(reserved => string.Equals(reserved, name, StringComparison.OrdinalIgnoreCase))
            || _keePassXcPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether keypaste will write a custom field with this name.</summary>
    /// <param name="name">The name to check.</param>
    /// <param name="error">A message naming the problem, or empty when the name is writable.</param>
    /// <returns><see langword="true"/> if the name is writable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public static bool IsWritable(string name, out string error)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name.Length == 0)
        {
            error = "the field name cannot be empty";
            return false;
        }

        if (name.Any(char.IsControl))
        {
            error = "the field name cannot contain control characters";
            return false;
        }

        if (name.Trim().Length != name.Length)
        {
            error = "the field name cannot begin or end with whitespace";
            return false;
        }

        if (IsStandard(name))
        {
            error = $"'{name}' is one of the entry's standard fields, not a custom field";
            return false;
        }

        if (IsKeePassXcAttribute(name))
        {
            error = $"'{name}' is KeePassXC's own attribute, which keypaste leaves to KeePassXC";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
