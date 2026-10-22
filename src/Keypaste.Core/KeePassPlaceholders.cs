namespace Keypaste.Core;

/// <summary>
/// The placeholders KeePass and KeePassXC replace in a value where they use it, such as <c>{PASSWORD}</c>,
/// and keypaste's filling of the ones naming the value's own item wherever the value leaves keypaste (D-0465).
/// </summary>
/// <remarks>
/// <para>
/// Keywords and prefixes match in any case, as in KeePass 2; a <c>{S:…}</c> field's name matches exactly.
/// Any other text in braces, such as a JSON value or <c>${HOME}</c>, is text, and so is an unclosed brace.
/// </para>
/// <para>
/// KeePassXC's escape, which it reads from 2.7.11 on, is honoured where KeePassXC honours it (D-NEW-X31): a value,
/// a field filled into one, or the inside of braces naming no placeholder, wholly wrapped in <c>\{</c> and
/// <c>\}</c>, fills in as itself with those two made braces, and nothing in it is filled in or refused.
/// </para>
/// <para>
/// <c>{TITLE}</c>, <c>{USERNAME}</c>, <c>{PASSWORD}</c>, <c>{URL}</c>, <c>{NOTES}</c> and <c>{S:…}</c> fill in
/// from the item's own fields, each filled in its turn; filled text is not scanned again, as in KeePassXC. A
/// <c>{S:…}</c> reads a standard field or a custom one <see cref="Approval.CredentialFields.IsCustom"/> accepts,
/// and an empty field fills in as empty. Any other placeholder, a field that cannot be read, a loop, nesting
/// deeper than <see cref="MaximumDepth"/> and a result longer than <see cref="MaximumLength"/> refuse the value
/// whole, naming the placeholder and never a value.
/// </para>
/// </remarks>
public static class KeePassPlaceholders
{
    /// <summary>The most placeholders one chain may nest, each in the field the one before it fills in from.</summary>
    public const int MaximumDepth = 10;

    /// <summary>The longest a filled value may be, in UTF-16 code units: 1 MiB.</summary>
    public const int MaximumLength = 1024 * 1024;

    private const string _named = "S:";
    private const string _namedShown = "{S:…}";
    private const string _dateTime = "DT_";
    private const string _dateTimeShown = "{DT_…}";
    private const string _escapeOpen = @"\{";
    private const string _escapeClose = @"\}";

    private const string _notFilled = "keypaste does not fill in";
    private const string _neverReleased = "names a field keypaste never releases";
    private const string _noField = "names no field of its item";
    private const string _loops = "leads back to itself";
    private const string _deep = "nests more than 10 deep";
    private const string _long = "fills it past 1 MiB";

    private static readonly char[] _braces = ['{', '}'];

    private static readonly KeyValuePair<string, string>[] _fills =
    [
        new("TITLE", "Title"),
        new("USERNAME", "UserName"),
        new("PASSWORD", "Password"),
        new("URL", "URL"),
        new("NOTES", "Notes"),
    ];

    private static readonly string[] _unfilled =
    [
        "UUID", "TOTP", "TIMEOTP", "DB_DIR",
        "DT_SIMPLE", "DT_YEAR", "DT_MONTH", "DT_DAY", "DT_HOUR", "DT_MINUTE", "DT_SECOND",
        "DT_UTC_SIMPLE", "DT_UTC_YEAR", "DT_UTC_MONTH", "DT_UTC_DAY", "DT_UTC_HOUR", "DT_UTC_MINUTE", "DT_UTC_SECOND",
    ];

    private static readonly string[] _unfilledPrefixes = ["REF:", "URL:", "T-CONV:", "T-REPLACE-RX:"];

    /// <summary>Whether a value holds any placeholder, filled in or not, or an escape: whether it leaves keypaste other than as it is held.</summary>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> when <see cref="Fill"/> would change or refuse it.</returns>
    internal static bool Holds(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return Scan(value).Any();
    }

    /// <summary>Whether a value holds a placeholder, filled in or not, an escape aside: the test made before any field is read.</summary>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> when <see cref="Fill"/> could read its item's fields or would refuse it.</returns>
    internal static bool HoldsPlaceholder(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return Scan(value).Any(token => token.Placeholder is not null);
    }

    /// <summary>Fills in every placeholder a value holds from its own item's fields, or refuses it whole.</summary>
    /// <param name="value">The value as the vault holds it.</param>
    /// <param name="fields">Reads the item's fields, called at most once and only when a placeholder could fill in.</param>
    /// <returns>The filled value and what was filled in, or the refusal.</returns>
    internal static PlaceholderFill Fill(string value, Func<PlaceholderFields> fields)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(fields);

        var filling = new Filling(fields);

        return filling.Text(value, 1) is { } filled
            ? PlaceholderFill.Filled(filled.Text, filling.Parts)
            : PlaceholderFill.Refused(filling.Refusal!);
    }

    /// <summary>The placeholders a value an edit writes plants, judged by name before any value is read.</summary>
    /// <param name="value">The value the edit writes.</param>
    /// <param name="held">The name of each custom field its item has once the edit is written.</param>
    /// <param name="field">The field the value is written to, such as <c>Password</c> or <c>API_KEY</c>, so a value naming it is a loop; null when none.</param>
    /// <returns>What fills in when the value leaves, or the first placeholder that refuses it.</returns>
    /// <remarks>
    /// Only the value's own placeholders are judged; what the fields they name hold is judged when the value leaves.
    /// An escape plants nothing, so a value that is wholly one fills in nothing and is refused nowhere.
    /// </remarks>
    internal static PlantedPlaceholders Planted(string value, IEnumerable<string> held, string? field = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(held);

        var fields = PlaceholderFields.Named(held);
        List<string> fills = [];

        foreach (var placeholder in Scan(value).Select(token => token.Placeholder).OfType<Placeholder>())
        {
            var shown = placeholder.Shown(fields);
            var refused = placeholder.Field is not { } named ? _notFilled
                : fields.Value(named) is null ? Unreadable(named, fields)
                : string.Equals(named, field, StringComparison.Ordinal) ? _loops
                : null;

            if (refused is not null)
            {
                return new PlantedPlaceholders(fills, new PlaceholderRefusal(shown, refused));
            }

            if (!fills.Contains(shown))
            {
                fills.Add(shown);
            }
        }

        return new PlantedPlaceholders(fills, null);
    }

    /// <summary>Whether a value holds a <c>{S:…}</c> naming this field exactly, which renaming the field would leave naming nothing.</summary>
    /// <param name="value">The value.</param>
    /// <param name="field">The field's name.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    internal static bool Names(string value, string field)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(field);

        return Scan(value).Any(token =>
            token.Placeholder is { Keyword: null } named && string.Equals(named.Field, field, StringComparison.Ordinal));
    }

    /// <summary>Each placeholder and escape in a text, left to right.</summary>
    /// <remarks>
    /// <para>
    /// A brace opens a placeholder only when the next brace after it closes one; text between them that is no
    /// placeholder leaves the opening brace as text, and the scan goes on after it. A conversion or reference
    /// that another brace opens inside, such as <c>{T-CONV:/{USERNAME}/Upper/}</c>, which KeePass fills in before
    /// converting, is a placeholder keypaste does not fill in, so the value is refused rather than half filled.
    /// </para>
    /// <para>
    /// A text wholly wrapped in <c>\{</c> and <c>\}</c> is one escape and holds nothing else. Braces whose inside
    /// is wholly one, such as <c>{\{PASSWORD\}}</c>, are a placeholder KeePassXC does not know, whose inside it
    /// fills in as it fills in a value, so that inside is an escape too.
    /// </para>
    /// </remarks>
    private static IEnumerable<Token> Scan(string text)
    {
        if (Escaped(text))
        {
            yield return new Token(0, text.Length - 1, null);
            yield break;
        }

        for (var open = text.IndexOf('{', StringComparison.Ordinal); open >= 0;)
        {
            if (EscapeInside(text, open) is { } close)
            {
                yield return new Token(open + 1, close - 1, null);
                open = text.IndexOf('{', close + 1);
                continue;
            }

            var next = text.IndexOfAny(_braces, open + 1);

            if (next < 0)
            {
                yield break;
            }

            if (text[next] == '{')
            {
                if (Prefixed(text[(open + 1)..next]) is { } enclosing && text.IndexOf('}', next + 1) >= 0)
                {
                    yield return new Token(open, next - 1, enclosing);
                }

                open = next;
                continue;
            }

            if (Classify(text[(open + 1)..next]) is { } placeholder)
            {
                yield return new Token(open, next, placeholder);
            }

            open = text.IndexOf('{', next + 1);
        }
    }

    /// <summary>Whether a text is wholly KeePassXC's escape, starting <c>\{</c> and ending <c>\}</c>.</summary>
    private static bool Escaped(string text) =>
        text.StartsWith(_escapeOpen, StringComparison.Ordinal) && text.EndsWith(_escapeClose, StringComparison.Ordinal);

    /// <summary>
    /// Where the braces opening at <paramref name="open"/> close when their inside is wholly an escape, matched as
    /// KeePassXC's pattern matches braces, every pair balanced and none empty; null when they do not.
    /// </summary>
    private static int? EscapeInside(string text, int open)
    {
        if (!text.AsSpan(open + 1).StartsWith(_escapeOpen, StringComparison.Ordinal))
        {
            return null;
        }

        var depth = 0;
        var empty = true;

        for (var at = open; at < text.Length; at++)
        {
            if (text[at] == '{')
            {
                depth++;
                empty = true;
            }
            else if (text[at] != '}')
            {
                empty = false;
            }
            else if (empty)
            {
                return null;
            }
            else if (--depth == 0)
            {
                return Escaped(text[(open + 1)..at]) ? at : null;
            }
        }

        return null;
    }

    /// <summary>The placeholder the text between two braces names, or null when it is text.</summary>
    /// <remarks>
    /// Exact keywords are matched before prefixes, so <c>{URL}</c> fills in and <c>{URL:RMVSCM}</c> does not. A
    /// <c>{DT_…}</c> that is not one of KeePass's date and time names is shown without its letters, which a value
    /// could have supplied.
    /// </remarks>
    private static Placeholder? Classify(string inner)
    {
        foreach (var (keyword, field) in _fills)
        {
            if (string.Equals(keyword, inner, StringComparison.OrdinalIgnoreCase))
            {
                return new Placeholder("{" + keyword + "}", field);
            }
        }

        if (_unfilled.FirstOrDefault(keyword => string.Equals(keyword, inner, StringComparison.OrdinalIgnoreCase)) is { } unfilled)
        {
            return new Placeholder("{" + unfilled + "}", null);
        }

        if (inner.StartsWith(_dateTime, StringComparison.OrdinalIgnoreCase)
            && inner.Length > _dateTime.Length
            && inner.All(c => char.IsAsciiLetter(c) || c == '_'))
        {
            return new Placeholder(_dateTimeShown, null);
        }

        if (inner.StartsWith(_named, StringComparison.OrdinalIgnoreCase))
        {
            return new Placeholder(null, inner[_named.Length..]);
        }

        return Prefixed(inner);
    }

    /// <summary>The placeholder keypaste does not fill in that text after an opening brace starts, such as <c>{REF:…}</c>, or null.</summary>
    private static Placeholder? Prefixed(string inner) =>
        _unfilledPrefixes.FirstOrDefault(prefix => inner.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) is { } found
            ? new Placeholder("{" + found + "…}", null)
            : null;

    /// <summary>Why a field a placeholder names cannot be read.</summary>
    private static string Unreadable(string field, PlaceholderFields fields) => fields.Has(field) ? _neverReleased : _noField;

    /// <summary>
    /// One placeholder in a text, from its opening brace to its closing one, or to just before a brace that opens
    /// inside it; or, with no placeholder, an escape from its backslash to its closing brace.
    /// </summary>
    private readonly record struct Token(int Open, int Close, Placeholder? Placeholder);

    /// <summary>One placeholder as written.</summary>
    /// <param name="Keyword">How it is shown, or null for a <c>{S:…}</c>, whose field is named only when its item has one by that name.</param>
    /// <param name="Field">The field it fills in from, matched exactly, or null when keypaste does not fill it in.</param>
    private sealed record Placeholder(string? Keyword, string? Field)
    {
        /// <summary>How a refusal or a prompt shows it.</summary>
        /// <param name="fields">The item's fields, or null when they were not read.</param>
        internal string Shown(PlaceholderFields? fields) =>
            Keyword ?? (Field is { } name && fields?.Has(name) == true ? "{S:" + EntryNameSanitizer.Sanitize(name).Text + "}" : _namedShown);
    }

    /// <summary>A text with its placeholders filled in, and how many more placeholders nest beneath it.</summary>
    private sealed record FilledText(string Text, int Height)
    {
        /// <summary>Its type only, so a debugger never shows what was filled in.</summary>
        /// <returns>The type's name.</returns>
        public override string ToString() => nameof(FilledText);
    }

    /// <summary>One value's filling: each field filled in once, whatever fans out to it, and the first refusal.</summary>
    private sealed class Filling(Func<PlaceholderFields> read)
    {
        private readonly Dictionary<string, FilledText> _filled = new(StringComparer.Ordinal);
        private readonly HashSet<string> _open = new(StringComparer.Ordinal);
        private readonly List<string> _parts = [];
        private PlaceholderFields? _fields;

        /// <summary>Why the value was refused, once it was.</summary>
        internal PlaceholderRefusal? Refusal { get; private set; }

        /// <summary>Each distinct non-empty text filled in, innermost first.</summary>
        internal IReadOnlyList<string> Parts => _parts;

        private PlaceholderFields Fields => _fields ??= read();

        /// <summary>Fills in a text whose placeholders sit at this level, the value's own at 1.</summary>
        /// <returns>The filled text, or null once <see cref="Refusal"/> is set.</returns>
        internal FilledText? Text(string text, int level)
        {
            var builder = new StringBuilder();
            var height = 0;
            var position = 0;
            var scanned = false;
            Placeholder? last = null;

            foreach (var token in Scan(text))
            {
                builder.Append(text, position, token.Open - position);
                position = token.Close + 1;
                scanned = true;

                if (token.Placeholder is not { } placeholder)
                {
                    builder.Append('{').Append(text, token.Open + 2, token.Close - token.Open - 3).Append('}');
                    continue;
                }

                last = placeholder;

                if (Resolve(placeholder, level) is not { } filled)
                {
                    return null;
                }

                if (builder.Length + filled.Text.Length > MaximumLength)
                {
                    return Refuse(placeholder, _long);
                }

                builder.Append(filled.Text);
                height = Math.Max(height, filled.Height + 1);
            }

            if (!scanned)
            {
                return new FilledText(text, 0);
            }

            builder.Append(text, position, text.Length - position);

            return last is not null && builder.Length > MaximumLength ? Refuse(last, _long) : new FilledText(builder.ToString(), height);
        }

        private FilledText? Resolve(Placeholder placeholder, int level)
        {
            if (placeholder.Field is not { } field)
            {
                return Refuse(placeholder, _notFilled);
            }

            if (Fields.Value(field) is not { } value)
            {
                return Refuse(placeholder, Unreadable(field, Fields));
            }

            if (_filled.TryGetValue(field, out var done))
            {
                return level + done.Height > MaximumDepth ? Refuse(placeholder, _deep) : done;
            }

            if (level > MaximumDepth)
            {
                return Refuse(placeholder, _deep);
            }

            if (!_open.Add(field))
            {
                return Refuse(placeholder, _loops);
            }

            var filled = Text(value, level + 1);
            _open.Remove(field);

            if (filled is not null)
            {
                _filled[field] = filled;

                if (filled.Text.Length > 0 && !_parts.Contains(filled.Text))
                {
                    _parts.Add(filled.Text);
                }
            }

            return filled;
        }

        private FilledText? Refuse(Placeholder placeholder, string ending)
        {
            Refusal = new PlaceholderRefusal(placeholder.Shown(_fields), ending);
            return null;
        }
    }
}
