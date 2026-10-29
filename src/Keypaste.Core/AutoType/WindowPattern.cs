using System.Text.RegularExpressions;

namespace Keypaste.Core.AutoType;

/// <summary>Whether an association's window pattern, its placeholders already filled in, fits a window's title (D-0606).</summary>
/// <remarks>
/// <para>
/// As KeePassXC 2.8's <c>Entry::autoTypeSequences</c> matches: a pattern that starts and ends with <c>//</c>, four
/// characters or more, is a regular expression searched anywhere in the title, ignoring case; any other matches the
/// whole title, ignoring case, <c>*</c> matching any run of characters and nothing else being special.
/// </para>
/// <para>
/// .NET's <see cref="Regex"/> runs both under <see cref="Timeout"/>, the wildcard on its non-backtracking engine. A
/// regular expression .NET cannot read, or one that runs out of time, matches nothing, and the caller names it.
/// </para>
/// </remarks>
internal static class WindowPattern
{
    /// <summary>How long one pattern may take to match one title.</summary>
    internal static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);

    private const string _fence = "//";

    /// <summary>Matches one pattern against one title.</summary>
    /// <param name="pattern">The pattern, its placeholders filled in.</param>
    /// <param name="title">The window's title.</param>
    /// <param name="timeout">How long the match may take.</param>
    /// <returns>Whether it fits, or why it could not be matched.</returns>
    internal static PatternMatch Match(string pattern, string title, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(title);

        Regex regex;

        try
        {
            regex = IsRegularExpression(pattern)
                ? new Regex(pattern[_fence.Length..^_fence.Length], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, timeout)
                : new Regex(Wildcard(pattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, timeout);
        }
        catch (ArgumentException)
        {
            return PatternMatch.Unreadable;
        }

        try
        {
            return regex.IsMatch(title) ? PatternMatch.Fits : PatternMatch.Differs;
        }
        catch (RegexMatchTimeoutException)
        {
            return PatternMatch.TimedOut;
        }
    }

    private static bool IsRegularExpression(string pattern) =>
        pattern.Length >= 2 * _fence.Length
        && pattern.StartsWith(_fence, StringComparison.Ordinal)
        && pattern.EndsWith(_fence, StringComparison.Ordinal);

    /// <summary>The whole title, every character as itself but <c>*</c>, as KeePassXC's <c>Tools::convertToRegex</c> builds it.</summary>
    private static string Wildcard(string pattern) =>
        "^(?:" + Regex.Escape(pattern).Replace(@"\*", ".*", StringComparison.Ordinal) + ")$";
}

/// <summary>What matching one window pattern against one title found.</summary>
internal enum PatternMatch
{
    /// <summary>The pattern fits the title.</summary>
    Fits,

    /// <summary>The pattern does not fit the title.</summary>
    Differs,

    /// <summary>The pattern is a regular expression .NET cannot read.</summary>
    Unreadable,

    /// <summary>Matching ran out of time.</summary>
    TimedOut,
}
