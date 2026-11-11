using System.Globalization;
using Avalonia.Controls;
using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>The usernames and email addresses this vault already uses, offered as a username box is typed in; read once through Core, which reads no other field.</summary>
internal sealed class UsernameSuggestions
{
    private const int _most = 8;

    private readonly IReadOnlyList<string> _used;

    /// <param name="vault">The unlocked vault, or null for none to offer.</param>
    internal UsernameSuggestions(Vault? vault) =>
        _used = vault is null ? [] : [.. vault.ReadUsernames().Where(username => !DisplayTextSanitizer.Sanitize(username).WasAltered)];

    private string? _matchedText;
    private IReadOnlyList<string> _matched = [];

    /// <summary>Every username the vault uses, which a username box holds as its items and narrows with a <see cref="Filter"/>.</summary>
    internal IReadOnlyList<string> Used => _used;

    /// <summary>A username box's filter, which shows exactly what <paramref name="suggested"/> offers for its text, so the list and its spoken count follow one rule.</summary>
    internal static AutoCompleteFilterPredicate<object?> Filter(Func<string, IReadOnlyList<string>> suggested)
    {
        ArgumentNullException.ThrowIfNull(suggested);

        return (typed, item) => item is string username && suggested(typed ?? string.Empty).Contains(username, StringComparer.Ordinal);
    }

    /// <summary>At most eight used usernames holding what is typed, whatever its case, leaving out the one typed exactly.</summary>
    internal IReadOnlyList<string> Matching(string typed)
    {
        ArgumentNullException.ThrowIfNull(typed);

        var text = typed.Trim();

        // A box's filter asks once per used username, so one text's matches are found once.
        if (text != _matchedText)
        {
            _matchedText = text;
            _matched = [.. _used.Where(username => Holds(username, text)).Take(_most)];
        }

        return _matched;
    }

    private static bool Holds(string username, string text) =>
        text.Length > 0 && username.Contains(text, StringComparison.OrdinalIgnoreCase) && !string.Equals(username, text, StringComparison.Ordinal);

    /// <summary>The match a username box completes what is typed with, in grey inside the field: the first that starts with it, whatever its case, or null.</summary>
    internal static string? Completion(string typed, IReadOnlyList<string> matching)
    {
        ArgumentNullException.ThrowIfNull(typed);
        ArgumentNullException.ThrowIfNull(matching);

        return typed.Length == 0 ? null : matching.FirstOrDefault(username => username.Length > typed.Length && username.StartsWith(typed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>What a screen reader is told as the box completes or offers matches, or null while it offers nothing.</summary>
    internal static string? Spoken(string typed, IReadOnlyList<string> matching)
    {
        ArgumentNullException.ThrowIfNull(matching);

        if (Completion(typed ?? string.Empty, matching) is { } completion)
        {
            return matching.Count == 1
                ? string.Format(CultureInfo.CurrentCulture, "Completes to {0}, used in this vault. Tab accepts it; Escape dismisses it.", completion)
                : string.Format(CultureInfo.CurrentCulture, "Completes to {0}, used in this vault. Tab accepts it; Down arrow lists all {1} matches; Escape dismisses it.", completion, matching.Count);
        }

        return matching.Count switch
        {
            0 => null,
            1 => "1 username used in this vault matches. Down arrow lists it; Escape closes the list.",
            var count => string.Format(CultureInfo.CurrentCulture, "{0} usernames used in this vault match. Down arrow lists them; Escape closes the list.", count),
        };
    }
}
