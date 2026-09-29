using System.Reflection;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Tests.Rendering;
using Xunit;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// Drives the app as launch composes it (D-0342): controls pressed where the window draws them and
/// keys typed into it, so a control that is hidden, covered or unbound fails; no command is called.
/// </summary>
internal static class JourneyDriver
{
    internal static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    private static readonly RawInputModifiers _command =
        OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    internal static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// Attaches the lifetime through its internal <c>SubscribeGlobalEvents</c>, because this assembly
    /// shares one dispatcher and cannot start one.
    /// </summary>
    internal static void Attach(ClassicDesktopStyleApplicationLifetime lifetime)
    {
        var subscribe = typeof(ClassicDesktopStyleApplicationLifetime).GetMethod(
            "SubscribeGlobalEvents",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.True(subscribe is not null, "Avalonia no longer has SubscribeGlobalEvents; attach the lifetime another way");
        subscribe.Invoke(lifetime, null);
    }

    internal static T Named<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name && control.IsEffectivelyVisible);

    internal static ListBoxItem Row(Window window, string list, Func<object?, bool> which) =>
        Named<ListBox>(window, list).GetVisualDescendants().OfType<ListBoxItem>().Single(item => which(item.DataContext));

    internal static void Press(Window window, Control control)
    {
        WindowInput.Reveal(window, control);
        WindowInput.Press(window, control);
        WindowInput.Release(window, control);
        WindowInput.Drain();
    }

    internal static void Chord(Window window, PhysicalKey key) => Key(window, key, _command);

    internal static void Key(Window window, PhysicalKey key, RawInputModifiers modifiers)
    {
        window.KeyPressQwerty(key, modifiers);
        window.KeyReleaseQwerty(key, modifiers);
        WindowInput.Drain();
    }

    /// <summary>The names of every visible element of the window's automation tree, as a screen reader reaches them.</summary>
    internal static List<string> AutomationNames(Window window)
    {
        WindowInput.Drain();
        List<string> names = [];
        Walk(ControlAutomationPeer.CreatePeerForElement(window));
        return names;

        void Walk(AutomationPeer peer)
        {
            foreach (var child in peer.GetChildren())
            {
                if (child is ControlAutomationPeer { Owner.IsEffectivelyVisible: true })
                {
                    names.Add(child.GetName() ?? string.Empty);
                    Walk(child);
                }
            }
        }
    }

    internal static async Task Until(Func<bool> condition, Func<string>? state = null)
    {
        var deadline = DateTime.UtcNow + Wait;

        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"the app never reached that state{(state is null ? string.Empty : ": " + state())}");
            WindowInput.Drain();
            await Task.Delay(20, Token);
        }
    }
}
