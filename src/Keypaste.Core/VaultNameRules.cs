namespace Keypaste.Core;

/// <summary>
/// The names keypaste is willing to give a group or an entry.
/// </summary>
/// <remarks>
/// <para>
/// Enforced on write and never on read. A name KeePassXC put in the file is always listed, because
/// keypaste and KeePassXC disagreeing about the contents of one file is the failure
/// docs/PRODUCT.md law 4.6 exists to prevent — the same split <see cref="EnvConvention.IsValidKey"/>
/// already describes for variable names.
/// </para>
/// <para>
/// The rules are <see cref="EnvConvention.IsValidProject"/>'s, generalized: that method was the
/// first place they were needed and is now one caller of them. A separator is refused because
/// <see cref="VaultEntry.Path"/> joins with one and nothing escapes it, so a name containing one
/// makes a path that answers to two things.
/// </para>
/// </remarks>
public static class VaultNameRules
{
    /// <summary>Whether a group name is one keypaste is willing to create.</summary>
    /// <param name="name">The name to check.</param>
    /// <param name="error">A message naming the problem, or empty when the name is valid.</param>
    /// <returns><see langword="true"/> if the name is valid.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public static bool IsValidGroupName(string name, out string error) =>
        IsValidName(name, "group name", out error);

    /// <summary>Whether an entry title is one keypaste is willing to create.</summary>
    /// <param name="title">The title to check.</param>
    /// <param name="error">A message naming the problem, or empty when the title is valid.</param>
    /// <returns><see langword="true"/> if the title is valid.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="title"/> is null.</exception>
    public static bool IsValidTitle(string title, out string error) =>
        IsValidName(title, "entry title", out error);

    /// <summary>The shared rule, worded for whatever is being named.</summary>
    /// <param name="name">The name to check.</param>
    /// <param name="noun">What the name names, for the message.</param>
    /// <param name="error">A message naming the problem, or empty when the name is valid.</param>
    /// <returns><see langword="true"/> if the name is valid.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="noun"/> is null.</exception>
    internal static bool IsValidName(string name, string noun, out string error)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(noun);

        if (name.Length == 0)
        {
            error = $"the {noun} cannot be empty";
            return false;
        }

        foreach (char c in name)
        {
            if (c is '/' or '\\')
            {
                error = $"the {noun} cannot contain '{c}'";
                return false;
            }

            if (char.IsControl(c))
            {
                error = $"the {noun} cannot contain control characters";
                return false;
            }
        }

        if (name.Trim().Length != name.Length)
        {
            error = $"the {noun} cannot begin or end with whitespace";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
