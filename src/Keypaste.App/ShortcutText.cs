using Keypaste.App.Resources;

namespace Keypaste.App;

/// <summary>Copy that names a chord, written with this platform's key: ⌘ on macOS and Ctrl elsewhere.</summary>
internal static class ShortcutText
{
    /// <summary>A chord as the copy writes it, such as ⌘L or Ctrl+L.</summary>
    public static string Chord(string key) => OperatingSystem.IsMacOS() ? $"⌘{key}" : $"Ctrl+{key}";

    /// <summary>A chord with Shift too, such as ⇧⌘C or Ctrl+Shift+C.</summary>
    public static string ShiftChord(string key) => OperatingSystem.IsMacOS() ? $"⇧⌘{key}" : $"Ctrl+Shift+{key}";

    public static string CopyUsername => $"{Strings.CopyUsername}, {ShiftChord("C")}";

    public static string CopyPassword => $"{Strings.CopyThePassword}, {Chord("C")}";

    public static string CopyOneTimeCode => $"Copy the code, {Chord("T")}; the clipboard clears itself";

    public static string IdleLock =>
        $"Counted from the last key or click. A machine that sleeps for longer than this wakes locked. {Chord("L")} locks now.";

    public static string Paste => $"Type it or press {Chord("V")} to paste.";
}
