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

    /// <summary>Every username the vault uses, which a username box holds as its items and narrows with <see cref="Offers"/>.</summary>
    internal IReadOnlyList<string> Used => _used;

    /// <summary>A username box's filter, public for XAML: the same rule as <see cref="Matching"/>, so the box keeps one list while its text changes.</summary>
    public static AutoCompleteFilterPredicate<object?> Offers { get; } =
        (typed, item) => item is string username && Holds(username, typed?.Trim() ?? string.Empty);

    /// <summary>The used usernames holding what is typed, whatever its case, leaving out the one typed exactly.</summary>
    internal IReadOnlyList<string> Matching(string typed)
    {
        ArgumentNullException.ThrowIfNull(typed);

        var text = typed.Trim();

        return [.. _used.Where(username => Holds(username, text)).Take(_most)];
    }

    private static bool Holds(string username, string text) =>
        text.Length > 0 && username.Contains(text, StringComparison.OrdinalIgnoreCase) && !string.Equals(username, text, StringComparison.Ordinal);

    /// <summary>What a screen reader is told as the list changes, or null while it offers nothing.</summary>
    internal static string? Spoken(IReadOnlyList<string> matching)
    {
        ArgumentNullException.ThrowIfNull(matching);

        return matching.Count switch
        {
            0 => null,
            1 => "1 username used in this vault matches. Down arrow chooses it; Escape closes the list.",
            var count => string.Format(CultureInfo.CurrentCulture, "{0} usernames used in this vault match. Down arrow chooses one; Escape closes the list.", count),
        };
    }
}
