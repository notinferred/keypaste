using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Keypaste.Cli.Clipboard;

/// <summary>
/// The four Win32 clipboard calls, as a seam.
/// </summary>
/// <remarks>
/// <para>
/// This exists so <see cref="WindowsClipboardWriter"/>'s <b>ordering</b> can be asserted on any
/// operating system. The order is the security property here — see that class — and a test that
/// could only run on Windows would not run in CI's Linux matrix at all, which is where a
/// reordering would otherwise land unnoticed.
/// </para>
/// <para>
/// What this seam cannot check is whether a format name registered under the name we passed.
/// That needs the real API and <see cref="GetFormatName"/>, and the test for it is Windows-only
/// on purpose.
/// </para>
/// </remarks>
internal interface IWin32Clipboard
{
    /// <summary>Registers a clipboard format name and returns its id, or 0 on failure.</summary>
    uint RegisterFormat(string name);

    /// <summary>Reads back the name Windows holds for <paramref name="id"/>, or null.</summary>
    /// <remarks>
    /// Only a test calls this. It is on the interface rather than the concrete type because the
    /// round-trip assertion is the one thing standing between us and KeePassXC's shipped defect.
    /// </remarks>
    string? GetFormatName(uint id);

    /// <summary>Takes ownership of the clipboard. False if another process holds it.</summary>
    bool Open();

    /// <summary>Empties the clipboard. Only valid while open.</summary>
    bool Empty();

    /// <summary>Places <paramref name="data"/> on the clipboard under <paramref name="format"/>.</summary>
    bool SetData(uint format, byte[] data);

    /// <summary>Releases the clipboard, which is what notifies every listener.</summary>
    bool Close();
}

/// <summary>The real Win32 clipboard.</summary>
/// <remarks>
/// <para>
/// <b>This narrows D-0005 rather than contradicting it.</b> That decision chose subprocesses over
/// P/Invoke to keep three native surfaces away from the trim and AOT analyzers, and it still holds
/// for macOS and Linux, which keep their tools. Windows is the exception D-0056 carved out for one
/// reason: <c>clip.exe</c> cannot express the opt-out formats at all, so on Windows the choice was
/// never subprocess-versus-P/Invoke, it was P/Invoke-versus-leaving-the-password-in-Win+V.
/// </para>
/// <para>
/// <b><c>DllImport</c> rather than <c>LibraryImport</c>, and the reason is not style.</b>
/// <c>LibraryImport</c>'s generated marshalling is built on pointers, so it requires
/// <c>AllowUnsafeBlocks</c> — which is a project-wide property, not a per-call one. Turning unsafe
/// on for the whole of a binary that handles master passwords, to save the marshalling of two
/// strings, is a bad trade. These signatures are blittable apart from one string and one
/// <c>char</c> buffer, both of which ILC marshals at compile time, so nothing here needs a runtime
/// stub and <c>scripts/verify-aot-trim.sh</c> stays quiet either way.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class Win32Clipboard : IWin32Clipboard
{
    private const int _gmemMoveable = 0x0002;

    /// <inheritdoc/>
    public uint RegisterFormat(string name) => RegisterClipboardFormatW(name);

    /// <inheritdoc/>
    public string? GetFormatName(uint id)
    {
        // 260 is longer than any name here and Windows truncates rather than overflowing.
        var buffer = new char[260];
        var written = GetClipboardFormatNameW(id, buffer, buffer.Length);
        return written > 0 ? new string(buffer, 0, written) : null;
    }

    /// <inheritdoc/>
    public bool Open()
    {
        // Another process can hold the clipboard for a few milliseconds at a time. Retrying is
        // the documented approach; failing on the first attempt would make copying flaky in a
        // way the user would read as keypaste being broken.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return false;
    }

    /// <inheritdoc/>
    public bool Empty() => EmptyClipboard();

    /// <inheritdoc/>
    public bool SetData(uint format, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var handle = GlobalAlloc(_gmemMoveable, (UIntPtr)data.Length);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var target = GlobalLock(handle);
        if (target == IntPtr.Zero)
        {
            GlobalFree(handle);
            return false;
        }

        try
        {
            Marshal.Copy(data, 0, target, data.Length);
        }
        finally
        {
            GlobalUnlock(handle);
        }

        // On success the system owns the block and freeing it here would be a use-after-free for
        // whoever pastes. On failure we still own it and must not leak it.
        if (SetClipboardData(format, handle) == IntPtr.Zero)
        {
            GlobalFree(handle);
            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public bool Close() => CloseClipboard();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint RegisterClipboardFormatW(string lpszFormat);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern int GetClipboardFormatNameW(uint format, [Out] char[] lpszFormatName, int cchMaxCount);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);
}
