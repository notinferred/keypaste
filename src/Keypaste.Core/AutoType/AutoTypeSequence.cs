using System.Globalization;

namespace Keypaste.Core.AutoType;

/// <summary>
/// Reads an Auto-Type sequence as KeePassXC 2.8's <c>AutoType::parseSequence</c> reads it, and refuses whole what
/// keypaste does not type and what KeePass 2 reads differently (D-0604).
/// </summary>
/// <remarks>
/// <para>
/// <c>{{}</c> and <c>{}}</c> are braces, and the braces balance. Each token is an optional run of the modifiers
/// <c>+</c>, <c>^</c> and <c>%</c>, held for the next key only, then either one placeholder in braces, whose inside
/// may hold balanced braces of its own, none empty, and may end in a count after white space, or one character.
/// Named keys, escaped characters and <c>{DELAY …}</c> match in any case, as KeePassXC lowers them; a <c>{S:…}</c>
/// names a field by all of its inside, in capitals as KeePassXC reads it (D-0531).
/// </para>
/// <para>
/// Refused, naming the token as written: a key that acts beyond the window or leaves a lock key changed, a token
/// KeePass 2 reads differently (a bare <c>#</c>, <c>~</c>, <c>(</c> or <c>)</c>, a modifier before anything but one
/// key or character, a count on anything else, a <c>%…%</c> KeePass 2 fills in from an environment variable, a
/// control character, a <c>{DELAY}</c> with no count), every placeholder keypaste does not type, and KeePassXC's own
/// errors: unbalanced braces, <c>{}</c>, and a count or delay over its limit.
/// </para>
/// </remarks>
internal static class AutoTypeSequence
{
    /// <summary>KeePassXC's most presses of one key a count may ask for.</summary>
    internal const int MostRepeats = 100;

    /// <summary>KeePassXC's longest <c>{DELAY n}</c>, in milliseconds.</summary>
    internal const int LongestPause = 10_000;

    /// <summary>KeePassXC's longest <c>{DELAY=n}</c>, in milliseconds.</summary>
    internal const int LongestKeyDelay = 500;

    private const string _notTyped = "which keypaste does not type";
    private const string _differs = "which KeePass and KeePassXC type differently";
    private const string _tooMany = "which repeats a key more than 100 times";
    private const string _tooLong = "which pauses longer than 10,000 ms";
    private const string _tooSlow = "which waits longer than 500 ms between keys";
    private const string _named = "S:";
    private const string _keyDelay = "delay=";

    private static readonly Dictionary<string, AutoTypeKey> _keys = new(StringComparer.Ordinal)
    {
        ["tab"] = AutoTypeKey.Tab,
        ["enter"] = AutoTypeKey.Enter,
        ["space"] = AutoTypeKey.Space,
        ["up"] = AutoTypeKey.Up,
        ["down"] = AutoTypeKey.Down,
        ["left"] = AutoTypeKey.Left,
        ["right"] = AutoTypeKey.Right,
        ["insert"] = AutoTypeKey.Insert,
        ["ins"] = AutoTypeKey.Insert,
        ["delete"] = AutoTypeKey.Delete,
        ["del"] = AutoTypeKey.Delete,
        ["home"] = AutoTypeKey.Home,
        ["end"] = AutoTypeKey.End,
        ["pgup"] = AutoTypeKey.PageUp,
        ["pgdown"] = AutoTypeKey.PageDown,
        ["pgdn"] = AutoTypeKey.PageDown,
        ["backspace"] = AutoTypeKey.Backspace,
        ["bs"] = AutoTypeKey.Backspace,
        ["bksp"] = AutoTypeKey.Backspace,
        ["break"] = AutoTypeKey.Break,
        ["esc"] = AutoTypeKey.Escape,
        ["help"] = AutoTypeKey.Help,
        ["add"] = AutoTypeKey.Add,
        ["subtract"] = AutoTypeKey.Subtract,
        ["multiply"] = AutoTypeKey.Multiply,
        ["divide"] = AutoTypeKey.Divide,
        ["f1"] = AutoTypeKey.F1,
        ["f2"] = AutoTypeKey.F2,
        ["f3"] = AutoTypeKey.F3,
        ["f4"] = AutoTypeKey.F4,
        ["f5"] = AutoTypeKey.F5,
        ["f6"] = AutoTypeKey.F6,
        ["f7"] = AutoTypeKey.F7,
        ["f8"] = AutoTypeKey.F8,
        ["f9"] = AutoTypeKey.F9,
        ["f10"] = AutoTypeKey.F10,
        ["f11"] = AutoTypeKey.F11,
        ["f12"] = AutoTypeKey.F12,
        ["f13"] = AutoTypeKey.F13,
        ["f14"] = AutoTypeKey.F14,
        ["f15"] = AutoTypeKey.F15,
        ["f16"] = AutoTypeKey.F16,
    };

    // KeePassXC types the numpad's names as the digits; the escapes are the characters themselves.
    private static readonly Dictionary<string, string> _characters = new(StringComparer.Ordinal)
    {
        ["leftbrace"] = "{",
        ["rightbrace"] = "}",
        ["leftparen"] = "(",
        ["rightparen"] = ")",
        ["("] = "(",
        [")"] = ")",
        ["["] = "[",
        ["]"] = "]",
        ["+"] = "+",
        ["%"] = "%",
        ["^"] = "^",
        ["~"] = "~",
        ["#"] = "#",
        ["numpad0"] = "0",
        ["numpad1"] = "1",
        ["numpad2"] = "2",
        ["numpad3"] = "3",
        ["numpad4"] = "4",
        ["numpad5"] = "5",
        ["numpad6"] = "6",
        ["numpad7"] = "7",
        ["numpad8"] = "8",
        ["numpad9"] = "9",
    };

    // Windows, Command and menu keys act beyond the window, lock keys outlast the sequence, and Print Screen takes a screenshot.
    private static readonly HashSet<string> _refusedKeys = new(StringComparer.Ordinal)
    {
        "win", "lwin", "rwin", "apps", "capslock", "numlock", "scrolllock", "ptrsc",
    };

    private static readonly Dictionary<string, string> _fields = new(StringComparer.Ordinal)
    {
        ["title"] = "Title",
        ["username"] = "UserName",
        ["password"] = "Password",
        ["url"] = "URL",
        ["notes"] = "Notes",
    };

    private static readonly string[] _refusedPrefixes = ["pickchars", "t-conv:", "t-replace-rx:", "mode=", "vkey", "appactivate"];

    /// <summary>Reads a sequence into its tokens, or refuses it whole.</summary>
    /// <param name="sequence">The sequence as the vault holds it.</param>
    /// <returns>The tokens, or the refusal completing <c>its Auto-Type sequence …</c>.</returns>
    internal static SequenceReading Read(string sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);

        if (sequence.Any(char.IsControl))
        {
            return SequenceReading.Refused("holds a control character, " + _differs);
        }

        if (EnvironmentVariable(sequence) is { } variable)
        {
            return SequenceReading.Refused(Holds(variable, _differs));
        }

        var spelled = Spelled.Of(sequence);
        var text = spelled.Text;

        if (text.Count(c => c == '{') != text.Count(c => c == '}'))
        {
            return SequenceReading.Refused("has unbalanced braces");
        }

        var braces = new Braces(text);
        List<SequenceToken> tokens = [];

        for (var at = 0; at < text.Length;)
        {
            var modifiers = AutoTypeModifiers.None;
            var next = at;

            for (; next < text.Length && Modifier(text[next]) is { } modifier; next++)
            {
                modifiers |= modifier;
            }

            if (next < text.Length && text[next] == '#')
            {
                return SequenceReading.Refused(Holds("#", _notTyped));
            }

            if (next == text.Length)
            {
                return SequenceReading.Refused($"ends with {Shown(spelled.Written(at, next - 1))}, {_differs}");
            }

            SequenceToken? token;
            string? refusal;

            switch (text[next])
            {
                case '{':
                    if (braces.Close(next) is not { } close)
                    {
                        return SequenceReading.Refused(braces.Partner(next) is { } partner
                            ? Holds(spelled.Written(next, partner), _notTyped)
                            : "has unbalanced braces");
                    }

                    token = Placeholder(text[(next + 1)..close], spelled.Written(at, close), modifiers, out refusal);
                    at = close + 1;
                    break;
                case '}':
                    return SequenceReading.Refused("has unbalanced braces");
                case '~' or '(' or ')':
                    return SequenceReading.Refused(Holds(spelled.Written(at, next), _differs));
                default:
                    var length = char.IsSurrogatePair(text, next) ? 2 : 1;
                    token = new CharacterToken(spelled.Written(at, next + length - 1), text.Substring(next, length), modifiers, 1);
                    refusal = null;
                    at = next + length;
                    break;
            }

            if (refusal is not null)
            {
                return SequenceReading.Refused(refusal);
            }

            if (modifiers != AutoTypeModifiers.None && token is not (KeyToken or CharacterToken))
            {
                return SequenceReading.Refused(Holds(token!.Written, _differs));
            }

            tokens.Add(token!);
        }

        return SequenceReading.Read(tokens);
    }

    /// <summary>What one placeholder's inside asks for, or why it is refused.</summary>
    /// <remarks>Checked in KeePassXC's order: named keys and escapes, delays, clearing, TOTP, the tokens it ignores, then fields.</remarks>
    private static SequenceToken? Placeholder(string inside, string written, AutoTypeModifiers modifiers, out string? refusal)
    {
        var (name, count) = Split(inside);
        var lowered = name.ToLowerInvariant();
        refusal = null;

        if (_keys.TryGetValue(lowered, out var key))
        {
            return Repeats(count, written, out var times, out refusal) ? new KeyToken(written, key, modifiers, times) : null;
        }

        if (_characters.TryGetValue(lowered, out var character))
        {
            return Repeats(count, written, out var times, out refusal) ? new CharacterToken(written, character, modifiers, times) : null;
        }

        if (_refusedKeys.Contains(lowered))
        {
            refusal = Holds(written, _notTyped);
            return null;
        }

        if (lowered.StartsWith(_keyDelay, StringComparison.Ordinal))
        {
            var milliseconds = Number(lowered[_keyDelay.Length..]);
            refusal = count is not null || milliseconds is null ? Holds(written, _differs)
                : milliseconds > LongestKeyDelay ? Holds(written, _tooSlow)
                : null;
            return refusal is null ? new KeyDelayToken(written, milliseconds!.Value) : null;
        }

        if (lowered == "delay")
        {
            var milliseconds = count is null ? null : Number(count);
            refusal = milliseconds is null ? Holds(written, _differs)
                : milliseconds > LongestPause ? Holds(written, _tooLong)
                : null;
            return refusal is null ? new PauseToken(written, milliseconds!.Value) : null;
        }

        if (lowered is "clearfield" or "totp" or "timeotp")
        {
            if (count is not null)
            {
                refusal = Holds(written, _differs);
                return null;
            }

            return lowered == "clearfield" ? new ClearFieldToken(written) : new TotpToken(written);
        }

        if (lowered.StartsWith("c:", StringComparison.Ordinal) || lowered == "beep" || (lowered.StartsWith("beep", StringComparison.Ordinal) && char.IsWhiteSpace(lowered[4])))
        {
            return new SilentToken(written);
        }

        if (_refusedPrefixes.Any(prefix => lowered.StartsWith(prefix, StringComparison.Ordinal)))
        {
            refusal = Holds(written, _notTyped);
            return null;
        }

        if (inside.StartsWith(_named, StringComparison.Ordinal))
        {
            return new FieldToken(written, inside[_named.Length..]);
        }

        if (count is null && _fields.TryGetValue(lowered, out var field))
        {
            return new FieldToken(written, field);
        }

        refusal = Holds(written, _notTyped);
        return null;
    }

    /// <summary>
    /// A placeholder's inside as KeePassXC's pattern splits it: the shortest name, ending after a character that is
    /// no brace or after a closed pair, after which only white space and a count remain; and that count, if any.
    /// </summary>
    private static (string Name, string? Count) Split(string inside)
    {
        var depth = 0;

        for (var end = 1; end <= inside.Length; end++)
        {
            var last = inside[end - 1];

            if (last == '{')
            {
                depth++;
                continue;
            }

            if (last == '}')
            {
                depth--;
            }

            if (depth != 0)
            {
                continue;
            }

            if (end == inside.Length)
            {
                break;
            }

            if (CountAfterSpace(inside.AsSpan(end)) is { } count)
            {
                return (inside[..end], count);
            }
        }

        return (inside, null);
    }

    /// <summary>The digits of a rest that is white space then ASCII digits and nothing else, or null.</summary>
    private static string? CountAfterSpace(ReadOnlySpan<char> rest)
    {
        var spaces = 0;

        while (spaces < rest.Length && char.IsWhiteSpace(rest[spaces]))
        {
            spaces++;
        }

        var digits = rest[spaces..];

        return spaces > 0 && digits.Length > 0 && !digits.ContainsAnyExceptInRange('0', '9') ? digits.ToString() : null;
    }

    /// <summary>How many times a count presses a key, one with none, or why it is refused.</summary>
    private static bool Repeats(string? count, string written, out int times, out string? refusal)
    {
        times = count is null ? 1 : Number(count) ?? int.MaxValue;
        refusal = times > MostRepeats ? Holds(written, _tooMany) : null;
        return refusal is null;
    }

    /// <summary>A run of ASCII digits as a number, a run too long for one as the largest, or null for anything else.</summary>
    private static int? Number(string digits) =>
        digits.Length == 0 || digits.AsSpan().ContainsAnyExceptInRange('0', '9') ? null
        : digits.Length > 9 ? int.MaxValue
        : int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);

    private static AutoTypeModifiers? Modifier(char c) => c switch
    {
        '+' => AutoTypeModifiers.Shift,
        '^' => AutoTypeModifiers.Control,
        '%' => AutoTypeModifiers.Alt,
        _ => null,
    };

    /// <summary>
    /// The first <c>%…%</c> whose inside holds no brace or <c>=</c>, which KeePass 2 fills in from the environment
    /// variable of that name, if one is set, before it reads the sequence, while KeePassXC reads its <c>%</c> as Alt.
    /// </summary>
    private static string? EnvironmentVariable(string sequence)
    {
        for (var open = sequence.IndexOf('%', StringComparison.Ordinal); open >= 0; open = sequence.IndexOf('%', open + 1))
        {
            var close = sequence.IndexOf('%', open + 1);

            if (close < 0)
            {
                return null;
            }

            if (close > open + 1 && sequence.AsSpan(open + 1, close - open - 1).IndexOfAny('{', '}', '=') < 0)
            {
                return sequence[open..(close + 1)];
            }
        }

        return null;
    }

    /// <summary>A token as written, drawn safely: the sequence is the vault's text, which anyone who can edit the vault wrote.</summary>
    /// <param name="written">The token as the sequence holds it.</param>
    /// <returns>The token, at most 100 characters, with every character that could misrepresent it removed.</returns>
    internal static string Shown(string written) => DisplayTextSanitizer.Sanitize(written, 100).Text;

    private static string Holds(string written, string why) => $"holds {Shown(written)}, {why}";

    /// <summary>The sequence with <c>{{}</c> and <c>{}}</c> spelled as KeePassXC spells them before it reads, and where each character came from.</summary>
    private sealed class Spelled
    {
        private readonly string _original;
        private readonly int[] _starts;
        private readonly int[] _ends;

        private Spelled(string original, string text, int[] starts, int[] ends)
        {
            _original = original;
            Text = text;
            _starts = starts;
            _ends = ends;
        }

        internal string Text { get; }

        /// <summary>The sequence as written that the characters from <paramref name="first"/> to <paramref name="last"/> came from.</summary>
        internal string Written(int first, int last) => _original[_starts[first].._ends[last]];

        /// <summary>Replaces every <c>{{}</c> and then every <c>{}}</c>, left to right, as KeePassXC's <c>QString::replace</c> does.</summary>
        internal static Spelled Of(string sequence)
        {
            var starts = Enumerable.Range(0, sequence.Length).ToArray();
            var ends = Enumerable.Range(1, sequence.Length).ToArray();
            var once = Replace(new Spelled(sequence, sequence, starts, ends), "{{}", "{LEFTBRACE}");

            return Replace(once, "{}}", "{RIGHTBRACE}");
        }

        private static Spelled Replace(Spelled from, string what, string with)
        {
            var text = new StringBuilder(from.Text.Length);
            List<int> starts = [];
            List<int> ends = [];

            for (var at = 0; at < from.Text.Length;)
            {
                if (from.Text.AsSpan(at).StartsWith(what, StringComparison.Ordinal))
                {
                    text.Append(with);
                    starts.AddRange(Enumerable.Repeat(from._starts[at], with.Length));
                    ends.AddRange(Enumerable.Repeat(from._ends[at + what.Length - 1], with.Length));
                    at += what.Length;
                }
                else
                {
                    text.Append(from.Text[at]);
                    starts.Add(from._starts[at]);
                    ends.Add(from._ends[at]);
                    at++;
                }
            }

            return new Spelled(from._original, text.ToString(), [.. starts], [.. ends]);
        }
    }

    /// <summary>Each opening brace's partner, and whether KeePassXC's pattern matches from it: no pair from it to its partner is empty.</summary>
    private sealed class Braces
    {
        private readonly Dictionary<int, int> _partners = [];
        private readonly int[] _empties;

        internal Braces(string text)
        {
            var opens = new Stack<int>();
            _empties = new int[text.Length + 1];

            for (var at = 0; at < text.Length; at++)
            {
                _empties[at + 1] = _empties[at] + (text[at] == '}' && at > 0 && text[at - 1] == '{' ? 1 : 0);

                if (text[at] == '{')
                {
                    opens.Push(at);
                }
                else if (text[at] == '}' && opens.Count > 0)
                {
                    _partners[opens.Pop()] = at;
                }
            }
        }

        /// <summary>The brace that closes the one at <paramref name="open"/>, whether or not the pattern matches there.</summary>
        internal int? Partner(int open) => _partners.TryGetValue(open, out var close) ? close : null;

        /// <summary>Where the placeholder opening at <paramref name="open"/> closes, or null when the pattern does not match there.</summary>
        internal int? Close(int open) =>
            _partners.TryGetValue(open, out var close) && _empties[close + 1] == _empties[open + 1] ? close : null;
    }
}

/// <summary>A sequence's tokens, or why it is refused.</summary>
/// <param name="Tokens">Each token in order; empty when refused.</param>
/// <param name="Refusal">Why, completing <c>its Auto-Type sequence …</c>; null when read.</param>
internal sealed record SequenceReading(IReadOnlyList<SequenceToken> Tokens, string? Refusal)
{
    internal static SequenceReading Read(IReadOnlyList<SequenceToken> tokens) => new(tokens, null);

    internal static SequenceReading Refused(string refusal) => new([], refusal);
}

/// <summary>One token of a sequence.</summary>
/// <param name="Written">The sequence's text it was read from, its modifiers included.</param>
internal abstract record SequenceToken(string Written);

/// <summary>A named key, pressed as many times as its count says.</summary>
internal sealed record KeyToken(string Written, AutoTypeKey Key, AutoTypeModifiers Modifiers, int Count) : SequenceToken(Written);

/// <summary>A character the sequence holds, an escaped one or a numpad digit, typed as many times as its count says.</summary>
internal sealed record CharacterToken(string Written, string Character, AutoTypeModifiers Modifiers, int Count) : SequenceToken(Written);

/// <summary>A field's value, named as the vault names it: <c>Password</c> for <c>{PASSWORD}</c>, or a <c>{S:…}</c>'s whole inside.</summary>
internal sealed record FieldToken(string Written, string Field) : SequenceToken(Written);

/// <summary><c>{TOTP}</c> or <c>{TIMEOTP}</c>.</summary>
internal sealed record TotpToken(string Written) : SequenceToken(Written);

/// <summary><c>{DELAY n}</c>.</summary>
internal sealed record PauseToken(string Written, int Milliseconds) : SequenceToken(Written);

/// <summary><c>{DELAY=n}</c>.</summary>
internal sealed record KeyDelayToken(string Written, int Milliseconds) : SequenceToken(Written);

/// <summary><c>{CLEARFIELD}</c>.</summary>
internal sealed record ClearFieldToken(string Written) : SequenceToken(Written);

/// <summary><c>{C:…}</c> or <c>{BEEP …}</c>, which type nothing.</summary>
internal sealed record SilentToken(string Written) : SequenceToken(Written);
