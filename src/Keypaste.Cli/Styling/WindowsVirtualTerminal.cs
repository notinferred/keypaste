using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Keypaste.Cli.Styling;

/// <summary>A Windows console, which interprets escapes only once <c>ENABLE_VIRTUAL_TERMINAL_PROCESSING</c> is set on it.</summary>
/// <remarks>
/// Off the secret path: a failure here means plain text, never a refusal. <c>DllImport</c> for the
/// reason <c>Win32Clipboard</c> gives: <c>LibraryImport</c> would need unsafe code project-wide.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class WindowsVirtualTerminal : IVirtualTerminal
{
    private const uint _enableVirtualTerminalProcessing = 0x0004;

    /// <inheritdoc/>
    public bool Truecolor => true;

    /// <inheritdoc/>
    public bool TryEnable(StandardStream stream)
    {
        var handle = GetStdHandle((int)stream);

        if (handle == IntPtr.Zero || handle == new IntPtr(-1) || !GetConsoleMode(handle, out var mode))
        {
            return false;
        }

        return (mode & _enableVirtualTerminalProcessing) != 0
            || SetConsoleMode(handle, mode | _enableVirtualTerminalProcessing);
    }

    /// <inheritdoc/>
    public void WriteLine(TextWriter writer, string text, ConsoleColor colour)
    {
        ArgumentNullException.ThrowIfNull(writer);

        var previous = Console.ForegroundColor;
        Console.ForegroundColor = colour;
        try
        {
            writer.WriteLine(text);
            writer.Flush();
        }
        finally
        {
            Console.ForegroundColor = previous;
        }
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
}
