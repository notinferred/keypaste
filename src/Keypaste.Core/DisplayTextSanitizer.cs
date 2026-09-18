using System.Globalization;

namespace Keypaste.Core;

/// <summary>
/// Makes an untrusted vault field safe to put on a screen, without changing what it says.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not a fourth method on <see cref="EntryNameSanitizer"/>.</b> That type removes ten
/// structural characters from every string it touches, and its own remarks argue for doing so:
/// a title containing a slash can impersonate a group path, and a name reaching a model's context
/// window carries no fence, no tag and no pipe. Neither argument reaches a URL, a username or a
/// notes body drawn in a detail pane. Those are read by a person, address nothing — an entry is
/// addressed by <see cref="EntryName"/> (D-0091) — and are never sent to a model. A mode on
/// <c>EntryNameSanitizer</c> that kept all ten would make that file argue against itself, so the
/// rules live apart and <c>DisplayTextSanitizerTests</c> drives the difference from both sides.
/// </para>
/// <para>
/// <b>What it still refuses.</b> Everything that can make text misrepresent itself: bidi overrides,
/// zero-width characters, the Unicode tag block, private use, and the line and paragraph separators.
/// That is the protection <c>HostileNameRenderingTests</c> asserts over these same fields, and it is
/// unchanged. What comes back is the difference between a URL a person can read and
/// <c>https: example.test path</c>, which is what the app drew before (docs/ui-review.md).
/// </para>
/// <para>
/// <b>It removes mechanism, not meaning</b>, exactly as <see cref="EntryNameSanitizer"/> says of
/// itself: a note that reads like an instruction passes through unchanged, because no filter can
/// decide what a sentence means and keypaste never acts on the text.
/// </para>
/// </remarks>
public static class DisplayTextSanitizer
{
    /// <summary>The longest single-line field drawn, in UTF-16 code units.</summary>
    public const int MaximumLength = 512;

    /// <summary>The longest notes body drawn. Notes is the largest free-text field in a vault.</summary>
    public const int MaximumNotesLength = 8192;

    /// <summary>Sanitizes one field for display.</summary>
    /// <param name="raw">The untrusted text.</param>
    /// <param name="maximumLength">The longest result, in UTF-16 code units.</param>
    /// <returns>The safe text, and whether anything changed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="raw"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumLength"/> is not positive.</exception>
    /// <remarks>
    /// Unlike <see cref="EntryNameSanitizer.Sanitize"/> there is no placeholder: a field nobody
    /// filled in is empty, and drawing <c>(unnamed)</c> under a URL heading — which is what the
    /// detail pane did for every entry without one — says something untrue about the vault.
    /// </remarks>
    public static SanitizedName Sanitize(string raw, int maximumLength = MaximumLength)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLength);

        var text = Scrub(raw, maximumLength);

        return new SanitizedName(text, !string.Equals(text, raw, StringComparison.Ordinal));
    }

    /// <summary>
    /// The scan. Iteration is over runes for the reason <see cref="EntryNameSanitizer"/> gives: the
    /// tag block U+E0000–U+E007F carries an ASCII sentence inside one glyph and every character in
    /// it is astral, so a loop over <see cref="char"/> misses all of them.
    /// </summary>
    private static string Scrub(string raw, int maximumLength)
    {
        var builder = new StringBuilder(Math.Min(raw.Length, maximumLength));
        Span<char> utf16 = stackalloc char[2];
        var units = 0;

        // Whether the last thing written was a space this method substituted, rather than whether
        // the last rune was a space. Two real spaces in somebody's notes are theirs to keep; two
        // substitutions in a row are one space. A line break resets it, so a rejected rune sitting
        // against a newline cannot indent the line after it.
        var lastWasSubstituted = false;

        var runes = raw.EnumerateRunes();

        while (runes.MoveNext())
        {
            var rune = runes.Current;

            // A carriage return is a line break, not a control character to blank out: CRLF from a
            // Windows editor becomes one newline and a lone CR becomes one too, so notes written on
            // any platform read the same here.
            if (rune.Value == '\r')
            {
                continue;
            }

            if (units + rune.Utf16SequenceLength > maximumLength)
            {
                break;
            }

            units += rune.Utf16SequenceLength;

            if (IsSafe(rune))
            {
                var written = rune.EncodeToUtf16(utf16);
                builder.Append(utf16[..written]);
                lastWasSubstituted = false;
                continue;
            }

            // Replaced with a space, never deleted, for the reason EntryNameSanitizer gives:
            // deleting would reassemble "ig\0nore" into a word somebody split on purpose.
            if (!lastWasSubstituted)
            {
                builder.Append(' ');
                lastWasSubstituted = true;
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>Whether a rune may be drawn as it is.</summary>
    /// <remarks>
    /// The whole difference from <see cref="EntryNameSanitizer"/> is what is missing here: no
    /// structural set. A URL needs its slashes, a Windows login needs its backslash, and a
    /// configuration example in a notes field needs its brackets and braces.
    /// </remarks>
    private static bool IsSafe(Rune rune)
    {
        // The three whitespace controls a person's own editor produces. Everything else
        // Rune.IsControl covers is a character no keyboard puts in a URL or a note on purpose.
        if (rune.Value is '\n' or '\t')
        {
            return true;
        }

        if (rune == Rune.ReplacementChar || Rune.IsControl(rune))
        {
            return false;
        }

        return Rune.GetUnicodeCategory(rune) is not (
            UnicodeCategory.Format                      // zero-width, bidi overrides, BOM, tags
            or UnicodeCategory.PrivateUse
            or UnicodeCategory.LineSeparator            // U+2028, which IsControl does not catch
            or UnicodeCategory.ParagraphSeparator);     // U+2029, likewise
    }
}
