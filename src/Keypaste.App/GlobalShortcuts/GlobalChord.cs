namespace Keypaste.App.GlobalShortcuts;

/// <summary>The modifiers a system-wide chord holds.</summary>
[Flags]
internal enum ChordModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,

    /// <summary>⌘ on a Mac; Windows and Linux keep their Windows key, or Super, for the system.</summary>
    Command = 8,
}

/// <summary>The keys a system-wide chord ends in.</summary>
internal enum ChordKey
{
    /// <summary>K, which sits in the same place on QWERTY, QWERTZ and AZERTY keyboards, and is Find's second chord in the window.</summary>
    K,

    Space,
}

/// <summary>
/// A chord that reaches keypaste from any app, written in <c>app.toml</c> as its lowercase parts joined by <c>+</c>,
/// such as <c>ctrl+cmd+k</c>, or <c>off</c>.
/// </summary>
/// <remarks>
/// Only the chords <see cref="Offered"/> names are taken, so a hand edit can never claim one that types a character, as
/// Ctrl+Alt does on Windows where it is AltGr, or one the system keeps, such as the Windows key's.
/// </remarks>
internal sealed record GlobalChord(ChordModifiers Modifiers, ChordKey Key)
{
    /// <summary>What <c>app.toml</c> says for no chord at all.</summary>
    internal const string OffText = "off";

    private static readonly (ChordModifiers Modifier, string Text)[] _written =
    [
        (ChordModifiers.Control, "ctrl"),
        (ChordModifiers.Alt, "alt"),
        (ChordModifiers.Shift, "shift"),
        (ChordModifiers.Command, "cmd"),
    ];

    // Neither macOS nor Windows nor a Linux desktop uses these for itself; ⌃⌘K and Alt+Shift+K come first because
    // the apps people use beside keypaste use them least: Firefox's console is ⌥⌘K and Ctrl+Shift+K, and an editor's
    // delete line ⇧⌘K and Ctrl+Shift+K.
    private static readonly GlobalChord[] _mac =
    [
        new(ChordModifiers.Control | ChordModifiers.Command, ChordKey.K),
        new(ChordModifiers.Control | ChordModifiers.Alt | ChordModifiers.Command, ChordKey.K),
        new(ChordModifiers.Alt | ChordModifiers.Command, ChordKey.K),
        new(ChordModifiers.Shift | ChordModifiers.Command, ChordKey.K),
    ];

    private static readonly GlobalChord[] _others =
    [
        new(ChordModifiers.Alt | ChordModifiers.Shift, ChordKey.K),
        new(ChordModifiers.Alt | ChordModifiers.Shift, ChordKey.Space),
        new(ChordModifiers.Control | ChordModifiers.Shift, ChordKey.K),
        new(ChordModifiers.Control | ChordModifiers.Shift, ChordKey.Space),
    ];

    /// <summary>The chords Settings offers on this platform, the default first.</summary>
    internal static IReadOnlyList<GlobalChord> Offered(bool mac) => mac ? _mac : _others;

    /// <summary>The chord a person who never chose one has.</summary>
    internal static GlobalChord Default(bool mac) => Offered(mac)[0];

    /// <summary>What <c>app.toml</c> says for this chord.</summary>
    internal string Text =>
        string.Join('+', _written.Where(part => Modifiers.HasFlag(part.Modifier)).Select(part => part.Text).Append(Key == ChordKey.K ? "k" : "space"));

    /// <summary>The chord as the platform writes it: ⌃⌥⇧⌘K on a Mac, Ctrl+Alt+Shift+K elsewhere.</summary>
    internal string Shown(bool mac)
    {
        var key = Key == ChordKey.K ? "K" : "Space";

        return mac
            ? string.Concat(Mark(ChordModifiers.Control, "⌃"), Mark(ChordModifiers.Alt, "⌥"), Mark(ChordModifiers.Shift, "⇧"), Mark(ChordModifiers.Command, "⌘"), key)
            : string.Concat(Mark(ChordModifiers.Control, "Ctrl+"), Mark(ChordModifiers.Alt, "Alt+"), Mark(ChordModifiers.Shift, "Shift+"), key);

        string Mark(ChordModifiers modifier, string mark) => Modifiers.HasFlag(modifier) ? mark : string.Empty;
    }

    /// <summary>
    /// The chord <c>app.toml</c>'s text names on this platform: the default where it names none, nothing for <c>off</c>,
    /// and nothing for a chord this platform does not offer, since taking a key from every other app is not to be guessed at.
    /// </summary>
    /// <param name="text">The file's text, or null where it has none.</param>
    /// <param name="mac">Whether this is macOS.</param>
    /// <remarks>A hand edit may order the parts its own way, space them or write capitals.</remarks>
    internal static GlobalChord? Chosen(string? text, bool mac)
    {
        if (text is null)
        {
            return Default(mac);
        }

        var parts = text.ToLowerInvariant().Split('+', StringSplitOptions.TrimEntries);
        var modifiers = ChordModifiers.None;

        foreach (var part in parts[..^1])
        {
            if (Array.Find(_written, written => string.Equals(written.Text, part, StringComparison.Ordinal)) is not { Text: not null } named)
            {
                return null;
            }

            modifiers |= named.Modifier;
        }

        return parts[^1] switch
        {
            "k" => Offered(mac).FirstOrDefault(chord => chord == new GlobalChord(modifiers, ChordKey.K)),
            "space" => Offered(mac).FirstOrDefault(chord => chord == new GlobalChord(modifiers, ChordKey.Space)),
            _ => null,
        };
    }
}
