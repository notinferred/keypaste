using System.Text;

namespace Keypaste.Cli.Clipboard;

/// <summary>
/// Writes to the Windows clipboard with the formats that opt a value out of Clipboard History
/// and Cloud Clipboard.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything goes on inside one open/close session, and that is the whole point.</b> The
/// clipboard-update notification the history service acts on is raised at <c>CloseClipboard</c>.
/// Writing the text in one session and adding the opt-out markers in a second means history has
/// already recorded the value by the time the markers arrive — the code would look correct, the
/// markers would genuinely be set, and the password would still be sitting in Win+V.
/// </para>
/// <para>
/// <b>The format names are asserted, never trusted.</b> Every shipped KeePassXC release — 2.7.10
/// through 2.7.12 — spells the third name <c>"CanUploadToCloudClipboard "</c> with a trailing
/// space, which silently registers a different and meaningless format. It is fixed on their
/// develop branch and in no release. A string literal handed to <c>RegisterClipboardFormat</c>
/// cannot be checked by reading it, which is why the test round-trips it through
/// <c>GetClipboardFormatName</c> instead (O-0008).
/// </para>
/// <para>
/// <b>What this closes, and what it does not.</b> These formats are a request to well-behaved
/// consumers, not an enforcement boundary — Windows does not restrict who may read the clipboard.
/// First-party Clipboard History and Cloud Clipboard honour them. Third-party clipboard managers
/// each decide independently and most do not, and RDP or Citrix redirection hands the value to a
/// peer machine nothing here can reach.
/// </para>
/// </remarks>
internal sealed class WindowsClipboardWriter
{
    /// <summary>Unicode text. The only format here a paste target actually reads.</summary>
    private const uint _cfUnicodeText = 13;

    /// <summary>
    /// The opt-out format names, exactly as Windows must see them.
    /// </summary>
    /// <remarks>
    /// Per Microsoft's documentation the first covers both history and cloud sync on its own; the
    /// other two are finer-grained and redundant beside it. All three are set anyway, because
    /// "redundant" is a statement about today's implementation of a service we do not control.
    /// </remarks>
    internal static readonly string[] OptOutFormatNames =
    [
        "ExcludeClipboardContentFromMonitorProcessing",
        "CanIncludeInClipboardHistory",
        "CanUploadToCloudClipboard",
    ];

    private readonly IWin32Clipboard _win32;

    internal WindowsClipboardWriter(IWin32Clipboard win32)
    {
        _win32 = win32;
    }

    /// <summary>Puts <paramref name="text"/> on the clipboard, opted out of history.</summary>
    internal bool TrySet(string text, out string error)
    {
        ArgumentNullException.ThrowIfNull(text);
        error = string.Empty;

        // Registration happens before the clipboard is opened. It does not need the clipboard,
        // and doing it inside would hold the clipboard open across calls that can fail for
        // reasons having nothing to do with us.
        var formats = new uint[OptOutFormatNames.Length];
        for (var i = 0; i < OptOutFormatNames.Length; i++)
        {
            formats[i] = _win32.RegisterFormat(OptOutFormatNames[i]);
            if (formats[i] == 0)
            {
                error = $"could not register the clipboard format {OptOutFormatNames[i]}";
                return false;
            }
        }

        if (!_win32.Open())
        {
            error = "another program is holding the clipboard";
            return false;
        }

        try
        {
            if (!_win32.Empty())
            {
                error = "could not empty the clipboard";
                return false;
            }

            // Null-terminated UTF-16, which is what CF_UNICODETEXT means by a string.
            var bytes = Encoding.Unicode.GetBytes(text + '\0');
            if (!_win32.SetData(_cfUnicodeText, bytes))
            {
                error = "could not write the text to the clipboard";
                return false;
            }

            // A DWORD 1 for the exclusion, DWORD 0 for the two opt-outs. Microsoft's contract is
            // presence for the first and a zero value for the others.
            if (!_win32.SetData(formats[0], BitConverter.GetBytes(1u))
                || !_win32.SetData(formats[1], BitConverter.GetBytes(0u))
                || !_win32.SetData(formats[2], BitConverter.GetBytes(0u)))
            {
                // The text is already on the clipboard and the markers are not. Empty it rather
                // than leaving a recordable password behind: a failed copy is an inconvenience,
                // and a copy that lands in Win+V is the defect this class exists to prevent.
                _win32.Empty();
                error = "could not mark the clipboard value as excluded from history";
                return false;
            }

            return true;
        }
        finally
        {
            // Close is what raises the notification, so it must happen on every path — including
            // the failure paths above, which have just emptied the clipboard and need that
            // emptying to actually take effect.
            _win32.Close();
        }
    }

    /// <summary>Empties the clipboard.</summary>
    /// <remarks>
    /// Emptying rather than overwriting with a blank value, which is what KeePassXC changed to in
    /// 2.7.5. Overwriting puts a new entry on the clipboard, and on a machine with history that
    /// is one more thing to have recorded.
    /// </remarks>
    internal bool TryClear(out string error)
    {
        error = string.Empty;

        if (!_win32.Open())
        {
            error = "another program is holding the clipboard";
            return false;
        }

        try
        {
            if (!_win32.Empty())
            {
                error = "could not empty the clipboard";
                return false;
            }

            return true;
        }
        finally
        {
            _win32.Close();
        }
    }
}
