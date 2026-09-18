namespace Keypaste.Core;

/// <summary>What became of an attempt to paste a secret.</summary>
/// <remarks>
/// A closed set rather than an exception, for the reason <see cref="VaultCreationOutcome"/> gives:
/// every one of these is an ordinary thing a person does, and each front end says it in its own
/// words rather than rendering somebody else's sentence.
/// </remarks>
public enum PasteOutcome
{
    /// <summary>The value was appended.</summary>
    Pasted = 0,

    /// <summary>There was nothing on the clipboard to paste.</summary>
    Empty = 1,

    /// <summary>The text held something no keyboard could have typed. Nothing was appended.</summary>
    Refused = 2,

    /// <summary>The clipboard could not be read at all.</summary>
    Unavailable = 3,
}

/// <summary>
/// The one rule deciding what a pasted secret may contain.
/// </summary>
/// <remarks>
/// <para>
/// <b>It lives here so the real clipboard and the test double cannot disagree.</b> If the rule were
/// written inside the Avalonia clipboard, every headless test that plants a value through a fake
/// would be asserting against the fake's own copy of it, and the two would drift the first time one
/// was edited.
/// </para>
/// <para>
/// <b>One trailing line break is dropped and anything else is refused.</b> Copying a token out of a
/// terminal, a file or a web page brings a trailing newline with it more often than not, and
/// storing a password that ends in one produces a credential that fails to authenticate somewhere
/// else, later, for reasons nobody connects back to the paste. That case is common enough to
/// handle and harmless to handle, because a secret cannot usefully end in a line break.
/// </para>
/// <para>
/// <b>Everything else is refused rather than stripped, and that is the important half.</b> Silently
/// removing an interior control character stores a value that differs from the one on the
/// clipboard, and the person pasting has no way to see it — they would be told the secret was
/// saved, and it would be the wrong secret. Refusing says so while there is still a form open to
/// fix it. It is also what <c>MaskedInput</c> already does with typed input, where a control
/// character is a keystroke rather than a password character.
/// </para>
/// <para>
/// <b>What is deliberately not refused:</b> a bidi override or a zero-width space. Those are display
/// trickery, and <see cref="DisplayTextSanitizer"/> answers them where text is drawn. In a secret
/// they are simply characters of the secret, and dropping one would store a value that does not
/// open the account.
/// </para>
/// </remarks>
public static class SecretInput
{
    /// <summary>Appends pasted text to a buffer, if the text is something a person could have typed.</summary>
    /// <param name="text">The clipboard's text.</param>
    /// <param name="destination">The buffer to append to. Untouched unless the result is <see cref="PasteOutcome.Pasted"/>.</param>
    /// <returns>What happened.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is null.</exception>
    public static PasteOutcome Accept(ReadOnlySpan<char> text, SecretBuffer destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var candidate = WithoutOneTrailingLineBreak(text);

        if (candidate.IsEmpty)
        {
            return PasteOutcome.Empty;
        }

        foreach (var c in candidate)
        {
            if (char.IsControl(c))
            {
                return PasteOutcome.Refused;
            }
        }

        destination.Append(candidate);
        return PasteOutcome.Pasted;
    }

    /// <summary>
    /// Drops one trailing line break, in any of the three spellings, and no more than one.
    /// </summary>
    /// <remarks>
    /// One, not all: a value ending in two blank lines was not produced by copying a line, and
    /// trimming until nothing is left would quietly turn a very odd paste into a plausible one.
    /// </remarks>
    private static ReadOnlySpan<char> WithoutOneTrailingLineBreak(ReadOnlySpan<char> text)
    {
        if (text.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return text[..^2];
        }

        return text.Length > 0 && (text[^1] == '\n' || text[^1] == '\r') ? text[..^1] : text;
    }
}
