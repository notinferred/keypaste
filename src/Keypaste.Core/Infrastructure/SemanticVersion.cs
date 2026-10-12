using System.Globalization;

namespace Keypaste.Core.Infrastructure;

/// <summary>A release's version, ordered by Semantic Versioning 2.0's precedence.</summary>
/// <remarks>Build metadata is refused rather than ignored, because the versions compared are the ones a release prints, which carry none.</remarks>
public readonly record struct SemanticVersion : IComparable<SemanticVersion>
{
    private SemanticVersion(int major, int minor, int patch, string? prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
    }

    /// <summary>Gets the major version.</summary>
    public int Major { get; }

    /// <summary>Gets the minor version.</summary>
    public int Minor { get; }

    /// <summary>Gets the patch version.</summary>
    public int Patch { get; }

    /// <summary>Gets the dot-separated identifiers after the <c>-</c>, or null for a release.</summary>
    public string? Prerelease { get; }

    /// <summary>Whether <paramref name="left"/> precedes <paramref name="right"/>.</summary>
    /// <param name="left">One version.</param>
    /// <param name="right">The other.</param>
    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

    /// <summary>Whether <paramref name="left"/> follows <paramref name="right"/>.</summary>
    /// <param name="left">One version.</param>
    /// <param name="right">The other.</param>
    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

    /// <summary>Whether <paramref name="left"/> precedes or equals <paramref name="right"/>.</summary>
    /// <param name="left">One version.</param>
    /// <param name="right">The other.</param>
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;

    /// <summary>Whether <paramref name="left"/> follows or equals <paramref name="right"/>.</summary>
    /// <param name="left">One version.</param>
    /// <param name="right">The other.</param>
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    /// <summary>Reads <c>major.minor.patch</c> with an optional <c>-prerelease</c>.</summary>
    /// <param name="text">The version as a release prints it.</param>
    /// <param name="version">The version, when this returns true.</param>
    /// <returns>Whether <paramref name="text"/> is a version with no build metadata.</returns>
    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = default;

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var dash = text.IndexOf('-', StringComparison.Ordinal);
        var parts = (dash < 0 ? text : text[..dash]).Split('.');
        var prerelease = dash < 0 ? null : text[(dash + 1)..];

        if (parts.Length != 3
            || !TryNumber(parts[0], out var major)
            || !TryNumber(parts[1], out var minor)
            || !TryNumber(parts[2], out var patch)
            || (prerelease is not null && !prerelease.Split('.').All(IsIdentifier)))
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch, prerelease);
        return true;
    }

    /// <inheritdoc/>
    public int CompareTo(SemanticVersion other)
    {
        var core = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));

        if (core != 0)
        {
            return core;
        }

        if (Prerelease is null || other.Prerelease is null)
        {
            return (Prerelease is null).CompareTo(other.Prerelease is null);
        }

        var mine = Prerelease.Split('.');
        var theirs = other.Prerelease.Split('.');

        for (var i = 0; i < Math.Min(mine.Length, theirs.Length); i++)
        {
            var order = CompareIdentifiers(mine[i], theirs[i]);

            if (order != 0)
            {
                return order;
            }
        }

        return mine.Length.CompareTo(theirs.Length);
    }

    /// <summary>The version as it was read.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => Prerelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Prerelease}";

    private static bool TryNumber(string text, out int number)
    {
        number = 0;
        return !HasLeadingZero(text) && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    private static bool IsIdentifier(string identifier) =>
        identifier.Length > 0
        && identifier.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
        && !(IsNumeric(identifier) && HasLeadingZero(identifier));

    private static bool IsNumeric(string identifier) => identifier.All(char.IsAsciiDigit);

    private static bool HasLeadingZero(string digits) => digits.Length > 1 && digits[0] == '0';

    // Numeric identifiers carry no leading zero, so the longer one is the larger.
    private static int CompareIdentifiers(string left, string right) => (IsNumeric(left), IsNumeric(right)) switch
    {
        (true, true) => left.Length != right.Length ? left.Length.CompareTo(right.Length) : string.CompareOrdinal(left, right),
        (true, false) => -1,
        (false, true) => 1,
        _ => string.CompareOrdinal(left, right),
    };
}
