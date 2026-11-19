namespace Keypaste.App;

/// <summary>Copy that names a chord, written with this platform's key: ⌘ on macOS and Ctrl elsewhere (Q.2).</summary>
internal static class ShortcutText
{
    /// <summary>A chord as the copy writes it, such as ⌘L or Ctrl+L.</summary>
    public static string Chord(string key) => OperatingSystem.IsMacOS() ? $"⌘{key}" : $"Ctrl+{key}";

    public static string IdleLock =>
        $"Counted from the last key or click. A machine that sleeps for longer than this wakes locked. {Chord("L")} locks now.";

    public static string Paste => $"Type it or press {Chord("V")} to paste.";
}
