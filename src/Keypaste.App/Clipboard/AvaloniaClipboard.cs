using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Keypaste.Core;

namespace Keypaste.App.Clipboard;

/// <summary>
/// The window's clipboard, and the one place in this app that reads one.
/// </summary>
/// <remarks>
/// <para>
/// <b>The secret markers are the reason this is not a subprocess.</b> Windows Clipboard History and
/// Cloud Clipboard keep a copy of everything copied, which clearing does not remove — O-0008. A
/// clipboard owner can opt out by putting three well-known formats on the data object, and
/// <c>clip.exe</c> has no way to express them, so <c>keypaste get</c> cannot. This can, and does,
/// and on the same item asks macOS pasteboard managers and KDE's Klipper to keep no copy (O-0019).
/// </para>
/// <para>
/// <b>All of it in one <c>SetDataAsync</c>.</b> The history service acts on the notification
/// raised when the clipboard closes, so a second pass to add the markers arrives after the copy has
/// already been recorded. One call, or the opt-out is theatre.
/// </para>
/// <para>
/// <b>What this closes and what it does not.</b> It closes first-party Clipboard History and Cloud
/// Clipboard, and asks the managers that honour the nspasteboard.org types or Klipper's hint to skip
/// the value. Other managers decide independently, RDP or Citrix redirection hands the value to
/// another machine's history, and no marker stops a process reading the clipboard. THREATS.md T-19
/// says so in those words.
/// </para>
/// <para>
/// <b>This file is the only place a clipboard string enters this process.</b>
/// Avalonia's clipboard has <c>TryGetTextAsync</c>, which is exactly the member
/// <c>Keypaste.Cli.Clipboard.IClipboard</c> refused to declare (D-0011). Two callers need it and
/// neither hands it on: <see cref="TryReadHashAsync"/>, where the auto-clear equality guard has no
/// ownership API to use instead, hashes it at once; and <see cref="TryPasteIntoAsync"/>, where the
/// characters go straight into the caller's buffer through <see cref="SecretInput.Accept"/>. Both
/// drop the reference in the method that made it. A test greps this app's sources and fails if
/// <c>TryGetTextAsync</c> appears in any other file.
/// </para>
/// </remarks>
internal sealed class AvaloniaClipboard(TopLevel topLevel) : IAppClipboard
{
    /// <summary>The formats that ask clipboard monitors and managers to skip a secret, each with what it holds.</summary>
    /// <remarks>
    /// <para>
    /// The Windows names are the registered clipboard format names, spelled exactly. KeePassXC
    /// shipped one of these with a trailing space for three releases (O-0008), which is the kind of
    /// defect no review catches in a string literal — <c>ClipboardSourceRulesTests</c> is why this
    /// one will not last three releases. Four zero bytes are the documented "no" for
    /// CanIncludeInClipboardHistory and CanUploadToCloudClipboard, a DWORD of zero; the
    /// monitor-processing format is read for presence rather than content.
    /// </para>
    /// <para>
    /// The two nspasteboard.org types are read for presence, so each holds nothing: KeePassXC puts
    /// the secret itself in ConcealedType, which a manager that stores what it skips would keep.
    /// Klipper reads <c>x-kde-passwordManagerHint</c> for the word <c>secret</c>. A platform that
    /// does not know a name never asks for it, so each costs nothing elsewhere.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<(string Name, byte[] Content)> SecretMarkers =>
    [
        ("ExcludeClipboardContentFromMonitorProcessing", new byte[4]),
        ("CanIncludeInClipboardHistory", new byte[4]),
        ("CanUploadToCloudClipboard", new byte[4]),
        ("org.nspasteboard.ConcealedType", []),
        ("org.nspasteboard.TransientType", []),
        ("x-kde-passwordManagerHint", "secret"u8.ToArray()),
    ];

    /// <inheritdoc/>
    public async Task<bool> TrySetSecretAsync(string secret)
    {
        if (topLevel.Clipboard is not { } clipboard)
        {
            return false;
        }

        // Disposed as soon as the platform has taken the data: holding it for the countdown would
        // keep the secret in an item for the whole window, which is what ClipboardCountdown refuses
        // to do. A platform needing delayed rendering would paste empty — the copy-and-paste check
        // on desktop.md's manual checklist, because CI has no clipboard.
        using var transfer = new DataTransfer();
        var item = DataTransferItem.CreateText(secret);

        foreach (var (name, content) in SecretMarkers)
        {
            item.Set(DataFormat.CreateBytesPlatformFormat(name), content);
        }

        transfer.Add(item);

        try
        {
            await clipboard.SetDataAsync(transfer).ConfigureAwait(true);
            return true;
        }
        catch (Exception e) when (e is PlatformNotSupportedException or InvalidOperationException or TimeoutException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> TrySetPlainAsync(string text)
    {
        if (topLevel.Clipboard is not { } clipboard)
        {
            return false;
        }

        try
        {
            await clipboard.SetTextAsync(text).ConfigureAwait(true);
            return true;
        }
        catch (Exception e) when (e is PlatformNotSupportedException or InvalidOperationException or TimeoutException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<byte[]?> TryReadHashAsync()
    {
        if (topLevel.Clipboard is not { } clipboard)
        {
            return null;
        }

        try
        {
            var text = await clipboard.TryGetTextAsync().ConfigureAwait(true);
            return text is null ? null : SHA256.HashData(Encoding.UTF8.GetBytes(text));
        }
        catch (Exception e) when (e is PlatformNotSupportedException or InvalidOperationException or TimeoutException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The string Avalonia hands back cannot be wiped, which is the limit SECURITY.md already
    /// states for a pasted password — it arrives whole in one immutable string whichever way it
    /// gets here. This narrows how many objects hold it, not how long the characters live.
    /// </remarks>
    public async Task<PasteOutcome> TryPasteIntoAsync(SecretBuffer destination)
    {
        if (topLevel.Clipboard is not { } clipboard)
        {
            return PasteOutcome.Unavailable;
        }

        try
        {
            var text = await clipboard.TryGetTextAsync().ConfigureAwait(true);
            return text is null ? PasteOutcome.Empty : SecretInput.Accept(text, destination);
        }
        catch (Exception e) when (e is PlatformNotSupportedException or InvalidOperationException or TimeoutException)
        {
            return PasteOutcome.Unavailable;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> TryClearAsync()
    {
        if (topLevel.Clipboard is not { } clipboard)
        {
            return false;
        }

        try
        {
            await clipboard.ClearAsync().ConfigureAwait(true);
            return true;
        }
        catch (Exception e) when (e is PlatformNotSupportedException or InvalidOperationException or TimeoutException)
        {
            return false;
        }
    }
}
