using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace Keypaste.App.Clipboard;

/// <summary>
/// The window's clipboard, and the one place in this app that reads one.
/// </summary>
/// <remarks>
/// <para>
/// <b>The exclusion formats are the reason this is not a subprocess.</b> Windows Clipboard History
/// and Cloud Clipboard keep a copy of everything copied, which clearing does not remove — O-0008.
/// A clipboard owner can opt out by putting three well-known formats on the data object, and
/// <c>clip.exe</c> has no way to express them, so <c>keypaste get</c> cannot. This can, and does.
/// </para>
/// <para>
/// <b>All of it in one <c>SetDataObjectAsync</c>.</b> The history service acts on the notification
/// raised when the clipboard closes, so a second pass to add the markers arrives after the copy has
/// already been recorded. One call, or the opt-out is theatre.
/// </para>
/// <para>
/// <b>What this closes and what it does not.</b> It closes first-party Clipboard History and Cloud
/// Clipboard. It does not touch third-party clipboard managers, which decide independently and
/// mostly ignore the formats, and it does not touch RDP or Citrix redirection, which hands the value
/// to another machine's history. THREATS.md T-19 says so in those words.
/// </para>
/// <para>
/// <b><see cref="TryReadHashAsync"/> is the one place a clipboard string enters this process.</b>
/// Avalonia's clipboard has <c>TryGetTextAsync</c>, which is exactly the member
/// <c>Keypaste.Cli.Clipboard.IClipboard</c> refused to declare (D-0011). The equality guard needs a
/// read-back and there is no ownership API to use instead, so the call is made here, hashed at once,
/// and the reference dropped. A test greps this app's sources and fails if it appears anywhere else.
/// </para>
/// </remarks>
internal sealed class AvaloniaClipboard(TopLevel topLevel) : IAppClipboard
{
    /// <summary>Asks clipboard monitors to skip this item.</summary>
    /// <remarks>
    /// The names are the registered Windows clipboard format names, spelled exactly. KeePassXC
    /// shipped one of these with a trailing space for three releases (O-0008), which is the kind of
    /// defect no review catches in a string literal — <c>ClipboardFormatNamesTests</c> is why this
    /// one will not last three releases.
    /// </remarks>
    internal static string[] ExclusionFormats { get; } =
    [
        "ExcludeClipboardContentFromMonitorProcessing",
        "CanIncludeInClipboardHistory",
        "CanUploadToCloudClipboard",
    ];

    /// <inheritdoc/>
    public async Task<bool> TrySetSecretAsync(string secret)
    {
        if (topLevel.Clipboard is not { } clipboard)
        {
            return false;
        }

        // Disposed as soon as the platform has taken the data. Holding it for the countdown would
        // mean holding the secret in an item for the whole window, which is exactly what
        // ClipboardCountdown refuses to do — it keeps a hash. If a platform ever needed delayed
        // rendering, a paste would come back empty, and that is item 3 on docs/desktop.md's manual
        // checklist because CI has no clipboard to check it with.
        using var transfer = new DataTransfer();
        var item = DataTransferItem.CreateText(secret);

        foreach (var format in ExclusionFormats)
        {
            // Four zero bytes: the documented "no" for CanIncludeInClipboardHistory and
            // CanUploadToCloudClipboard is a DWORD of zero, and the monitor-processing format is
            // read for presence rather than content. On platforms that do not know these names the
            // format is simply never asked for, so this costs nothing off Windows.
            item.Set(DataFormat.CreateBytesPlatformFormat(format), new byte[4]);
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
