namespace Keypaste.Cli.Clipboard;

/// <summary>
/// Decides how the clipboard gets cleared after keypaste puts a secret on it.
/// </summary>
/// <remarks>
/// This interface exists so the blocking-versus-detached choice lives in exactly one place.
/// keypaste blocks today (DECISIONS.md D-0011); when holding the terminal for twenty seconds
/// becomes the top complaint, a detached implementation drops in here and no command code
/// changes.
/// </remarks>
internal interface IClipboardClearStrategy
{
    /// <summary>
    /// Clears the clipboard <paramref name="delay"/> after the secret was copied, provided it
    /// still holds <paramref name="expectedHash"/>.
    /// </summary>
    /// <param name="clipboard">The clipboard to clear.</param>
    /// <param name="expectedHash">SHA-256 of the clipboard taken immediately after the copy.</param>
    /// <param name="delay">How long to leave the secret in place.</param>
    /// <param name="status">Receives progress messages; in practice stderr.</param>
    void ClearAfter(IClipboard clipboard, byte[] expectedHash, TimeSpan delay, TextWriter status);
}
