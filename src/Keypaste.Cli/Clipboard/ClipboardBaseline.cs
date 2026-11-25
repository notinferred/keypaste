namespace Keypaste.Cli.Clipboard;

/// <summary>What the clipboard holds straight after keypaste copied a secret, for the clear to compare against.</summary>
internal static class ClipboardBaseline
{
    /// <summary>
    /// Hashes the clipboard; call it straight after <see cref="IClipboard.TrySet"/>, so whatever the
    /// platform's read-back does to the bytes it does identically at both ends of the wait.
    /// </summary>
    /// <returns>The hash, or <see langword="null"/> when the read failed, which the clear takes as "clear anyway".</returns>
    internal static byte[]? Take(IClipboard clipboard) =>
        clipboard.TryReadHash(out var sha256, out _) == ClipboardStatus.Ok ? sha256 : null;
}
