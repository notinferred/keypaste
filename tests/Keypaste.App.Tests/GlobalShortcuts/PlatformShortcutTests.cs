using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Keypaste.App.GlobalShortcuts;
using Xunit;

namespace Keypaste.App.Tests.GlobalShortcuts;

/// <summary>
/// Each platform's own call registers a chord, refuses one another registration holds, and gives it back. Nobody
/// presses keys on a runner, so a press is the message Windows would send; macOS's and X11's arrive only from a keyboard.
/// </summary>
/// <remarks>The chord holds every modifier, which no app uses, so a run on a person's machine takes nothing from them.</remarks>
public sealed class PlatformShortcutTests
{
    private static readonly GlobalChord _everyModifier =
        new(ChordModifiers.Control | ChordModifiers.Alt | ChordModifiers.Shift | ChordModifiers.Command, ChordKey.K);

    [Fact]
    public void This_platform_gets_its_own_call_or_says_why_it_has_none()
    {
        using var shortcut = PlatformShortcut.ForThisProcess();

        if (OperatingSystem.IsMacOS())
        {
            Assert.IsType<MacGlobalShortcut>(shortcut);
        }
        else if (OperatingSystem.IsWindows())
        {
            Assert.IsType<WindowsGlobalShortcut>(shortcut);
        }
        else if (shortcut is NoGlobalShortcut)
        {
            Assert.NotNull(shortcut.Unavailable);
        }
    }

    [Fact]
    public void A_Mac_hot_key_another_registration_holds_is_refused_until_it_is_given_back()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Skip("Carbon's hot keys are macOS's.");
            return;
        }

        using var first = new MacGlobalShortcut();
        using var second = new MacGlobalShortcut();
        AssertHeldThenFree(first, second);
    }

    [Fact]
    public void A_Windows_hot_key_another_registration_holds_is_refused_and_its_message_is_a_press()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("RegisterHotKey is Windows's.");
            return;
        }

        using var first = new WindowsGlobalShortcut();
        using var second = new WindowsGlobalShortcut();
        var pressed = 0;
        first.Pressed += (_, _) => pressed++;
        Assert.True(first.TryRegister(_everyModifier));

        _ = SendMessage(first.Window, 0x0312, 1, 0);
        Assert.Equal(1, pressed);

        _ = SendMessage(first.Window, 0x0312, 2, 0);
        Assert.Equal(1, pressed);

        first.Unregister();
        AssertHeldThenFree(first, second);
    }

    [Fact]
    public void An_X11_grab_another_connection_holds_is_refused_until_it_is_given_back()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Skip("XGrabKey is X11's.");
            return;
        }

        using var first = X11GlobalShortcut.Open();
        using var second = X11GlobalShortcut.Open();
        Assert.SkipWhen(first is NoGlobalShortcut || PlatformShortcut.LinuxUnavailable(
            Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
            Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"),
            Environment.GetEnvironmentVariable("DISPLAY")) is not null, "There is no X11 display outside Wayland here.");

        AssertHeldThenFree(first, second);
    }

    private static void AssertHeldThenFree(IGlobalShortcut first, IGlobalShortcut second)
    {
        Assert.True(first.TryRegister(_everyModifier), "the first registration was refused");
        Assert.False(second.TryRegister(_everyModifier), "a chord another registration holds was registered twice");

        first.Unregister();

        Assert.True(second.TryRegister(_everyModifier), "a chord given back stayed held");
        second.Unregister();
    }

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern IntPtr SendMessage(IntPtr window, uint message, nint wParam, nint lParam);
}
