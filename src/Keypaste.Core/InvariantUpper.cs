namespace Keypaste.Core;

/// <summary>Upper-cases text the same way in the desktop, which has ICU, and the CLI, which runs invariant.</summary>
/// <remarks>
/// <c>string.ToUpperInvariant</c> and its kin are banned in this project and Infrastructure: with ICU it turns U+017F, the long s, into S, and without it leaves it, so a name would mean one thing in the app and another in the CLI.
/// </remarks>
public static class InvariantUpper
{
    private const char _longS = '\u017F';

    /// <summary>The upper case of a character, the long s left as it is.</summary>
    /// <param name="c">The character.</param>
    /// <returns>Its upper case.</returns>
#pragma warning disable RS0030
    public static char Of(char c) => c == _longS ? c : char.ToUpperInvariant(c);
#pragma warning restore RS0030

    /// <summary>The upper case of text, each long s left as it is.</summary>
    /// <param name="text">The text.</param>
    /// <returns>Its upper case.</returns>
    public static string Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return string.Create(text.Length, text, static (upper, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                upper[i] = Of(source[i]);
            }
        });
    }
}
