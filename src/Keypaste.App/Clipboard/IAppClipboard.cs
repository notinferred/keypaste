namespace Keypaste.App.Clipboard;

/// <summary>
/// The system clipboard, as much of it as the desktop app needs.
/// </summary>
/// <remarks>
/// <para>
/// <b>No member returns clipboard text</b>, which is the rule
/// <c>Keypaste.Cli.Clipboard.IClipboard</c> was written to and the reason both seams look alike.
/// Auto-clear only needs to know whether the clipboard still holds what keypaste put there, so this
/// exposes a hash. Returning the text would pull the user's whole clipboard into a process holding
/// an unlocked vault, for no benefit, and would give some future caller something to log.
/// </para>
/// <para>
/// <b>It is not the CLI's <c>IClipboard</c>, and that is deliberate.</b> The two front ends share
/// the <em>rule</em> — <see cref="Core.Clipboard.ClipboardClear.Should"/>, one function, two callers
/// — and not the transport. A terminal has no window and shells out to <c>clip.exe</c>,
/// <c>pbcopy</c> or <c>wl-copy</c>; this app has a window, so it can hand the windowing system a
/// data object carrying the Windows exclusion formats, which a subprocess cannot express. That
/// closes O-0008 for the app and leaves it open for the CLI, which is a real difference and is
/// written down rather than smoothed over.
/// </para>
/// <para>
/// Asynchronous because the platform is. It exists as an interface so
/// <see cref="ClipboardCountdown"/> can be tested with no window, no display and no clipboard —
/// which is where the security assertions live (CORE.md law 4.5).
/// </para>
/// </remarks>
internal interface IAppClipboard
{
    /// <summary>Puts a secret on the clipboard, asking the platform not to remember it.</summary>
    /// <param name="secret">The value.</param>
    /// <returns>Whether it landed.</returns>
    Task<bool> TrySetSecretAsync(string secret);

    /// <summary>Puts something that is not a secret on the clipboard.</summary>
    /// <param name="text">The text.</param>
    /// <returns>Whether it landed.</returns>
    /// <remarks>
    /// Separate from <see cref="TrySetSecretAsync"/> so the exclusion formats are not applied to a
    /// command line somebody wants in their shell history. Asking Windows to keep
    /// <c>keypaste run billing --</c> out of clipboard history would be a small hostility.
    /// </remarks>
    Task<bool> TrySetPlainAsync(string text);

    /// <summary>Hashes what the clipboard holds now, without exposing it.</summary>
    /// <returns>The hash, or null when the clipboard could not be read.</returns>
    Task<byte[]?> TryReadHashAsync();

    /// <summary>Empties the clipboard.</summary>
    /// <returns>Whether it emptied.</returns>
    Task<bool> TryClearAsync();
}
