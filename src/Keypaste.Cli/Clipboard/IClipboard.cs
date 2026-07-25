namespace Keypaste.Cli.Clipboard;

/// <summary>Why a clipboard operation could not be performed.</summary>
internal enum ClipboardStatus
{
    /// <summary>The operation succeeded.</summary>
    Ok,

    /// <summary>There is no graphical session at all — a headless server or bare SSH.</summary>
    NoDisplay,

    /// <summary>There is a session, but none of the known clipboard tools is installed.</summary>
    NoTool,

    /// <summary>A tool was found and failed.</summary>
    Failed,
}

/// <summary>
/// The system clipboard, reached through whatever tool the platform ships.
/// </summary>
/// <remarks>
/// <b>No member returns clipboard text.</b> Auto-clear only needs to know whether the clipboard
/// still holds what keypaste put there, so the seam exposes a hash instead. Returning the text
/// would pull the user's entire clipboard — including secrets keypaste never wrote — into this
/// process for no benefit, and would give some future caller something to accidentally log.
/// </remarks>
internal interface IClipboard
{
    /// <summary>Puts <paramref name="text"/> on the clipboard.</summary>
    ClipboardStatus TrySet(string text, out string error);

    /// <summary>
    /// Hashes the clipboard's current contents with SHA-256, without exposing them.
    /// </summary>
    /// <remarks>
    /// The comparison baseline is taken by calling this immediately after
    /// <see cref="TrySet"/>, which makes the scheme robust rather than merely tidy: whatever a
    /// platform's read-back does to the bytes — appending a newline, re-encoding — it does
    /// identically both times, so the read-back need only be deterministic, never faithful.
    /// </remarks>
    ClipboardStatus TryReadHash(out byte[] sha256, out string error);

    /// <summary>Empties the clipboard.</summary>
    ClipboardStatus TryClear(out string error);
}
