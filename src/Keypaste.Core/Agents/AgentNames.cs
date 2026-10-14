using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Keypaste.Core.Agents;

/// <summary>
/// The names agents' settings are kept under: the <c>--client-label</c> a bridge was started with,
/// never the name the agent calls itself (THREATS.md T-3).
/// </summary>
public static class AgentNames
{
    /// <summary>The name that stands for every agent with no settings of its own, which only ever takes away from the defaults.</summary>
    public const string Any = "*";

    /// <summary>The longest name, as <c>keypaste mcp --client-label</c> accepts it.</summary>
    public const int MaximumLength = 64;

    /// <summary>Whether settings may be kept under a name.</summary>
    /// <param name="name">The name.</param>
    /// <param name="error">Why not, otherwise empty.</param>
    /// <returns>True for <see cref="Any"/> and for 1 to 64 characters the entry-name sanitizer leaves unaltered, with no quote or backslash.</returns>
    public static bool IsValid(string name, out string error)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (string.Equals(name, Any, StringComparison.Ordinal))
        {
            error = string.Empty;
            return true;
        }

        if (name.Length is 0 or > MaximumLength
            || EntryNameSanitizer.Sanitize(name, MaximumLength).WasAltered
            || name.Contains('"', StringComparison.Ordinal)
            || name.Contains('\\', StringComparison.Ordinal))
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"an agent's name is \"*\" or 1 to {MaximumLength} characters with no quote, backslash, slash or control character");
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>Whether a bridge's label names one agent: valid and not <see cref="Any"/>.</summary>
    /// <param name="label">The raw label, or null for a bridge started without one.</param>
    /// <returns>Whether settings kept under the label are the agent's own.</returns>
    public static bool IsNamed([NotNullWhen(true)] string? label) =>
        label is { Length: > 0 } && !string.Equals(label, Any, StringComparison.Ordinal) && IsValid(label, out _);
}
