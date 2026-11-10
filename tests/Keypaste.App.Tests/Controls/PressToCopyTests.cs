using Avalonia.Automation;
using Avalonia.Headless;
using Avalonia.Input;
using Keypaste.App.Controls;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Controls;

/// <summary>Pressing a value copies it through the existing clipboard path, by Tab and a key as by a pointer, shows a check for about a second, and never names the value.</summary>
public sealed class PressToCopyTests
{
    private static PressToCopy Open(RenderedShell shell, ManualClock clock, string name)
    {
        shell.Window.SetValue(RevealedValue.ClockProperty, clock);
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        entries.Selected = entries.Rows.Single(row => row.Title == "github");
        RenderedShell.Drain();
        shell.Frame();
        return shell.Named<PressToCopy>(name);
    }

    [Fact]
    public Task A_press_copies_the_secret_through_the_clearing_clipboard_and_shows_a_check_for_a_second() => HeadlessSession.On(() =>
    {
        var clock = new ManualClock();
        using var shell = new RenderedShell("keypaste-press-copy-");
        var value = Open(shell, clock, "CopyPasswordValue");

        Assert.DoesNotContain(":copied", value.Classes);
        shell.Tap(value);
        RenderedShell.Drain();

        Assert.True(shell.Shell.Clipboard.IsCounting, "the copy did not go through the clearing clipboard");
        Assert.Equal(RenderedShell.Current, shell.Clipboard.Content);
        Assert.True(shell.Clipboard.ContentWasSetAsASecret);
        Assert.Contains(":copied", value.Classes);

        clock.Advance(PressToCopy.CheckShownFor);
        RenderedShell.Drain();

        Assert.DoesNotContain(":copied", value.Classes);
    });

    [Theory]
    [InlineData(PhysicalKey.Enter)]
    [InlineData(PhysicalKey.Space)]
    public Task Tab_reaches_it_and_a_key_presses_it(PhysicalKey key) => HeadlessSession.On(() =>
    {
        var clock = new ManualClock();
        using var shell = new RenderedShell("keypaste-press-key-");
        var value = Open(shell, clock, "CopyUsername");

        Assert.True(value.Focusable && value.IsTabStop);
        value.Focus(NavigationMethod.Tab);
        RenderedShell.Drain();
        shell.Window.KeyPressQwerty(key, RawInputModifiers.None);
        shell.Window.KeyReleaseQwerty(key, RawInputModifiers.None);
        RenderedShell.Drain();

        Assert.Equal("me", shell.Clipboard.Content);
    });

    [Fact]
    public Task Its_accessible_name_is_fixed_words_never_the_value() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-press-name-");
        var value = Open(shell, new ManualClock(), "CopyPasswordValue");

        Assert.Equal("Copy the password", AutomationProperties.GetName(value));
        Assert.All(RenderedShell.Secrets, secret => AutomationSurface.AssertNothingExposes(shell.Window, secret));
    });
}
