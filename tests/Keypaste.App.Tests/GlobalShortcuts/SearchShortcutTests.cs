using Keypaste.App.GlobalShortcuts;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core.Audit;
using Keypaste.Core.Settings;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.GlobalShortcuts;

#pragma warning disable CA2000 // Each search shortcut owns the system's chord it is given, and disposes it.

/// <summary>
/// The search shortcut keeps the chord <c>app.toml</c> chooses registered with the system: the default until one is
/// chosen, nothing once Off is, and a chord another app holds said in Settings › General rather than thrown.
/// </summary>
public sealed class SearchShortcutTests
{
    private static readonly GlobalChord _macDefault = new(ChordModifiers.Control | ChordModifiers.Command, ChordKey.K);
    private static readonly GlobalChord _otherDefault = new(ChordModifiers.Alt | ChordModifiers.Shift, ChordKey.K);

    [Theory]
    [InlineData(true, "ctrl+cmd+k", "⌃⌘K")]
    [InlineData(false, "alt+shift+k", "Alt+Shift+K")]
    public void The_default_is_registered_at_once_and_a_press_searches(bool mac, string text, string shown)
    {
        using var fixture = new TempVault();
        var system = new FakeGlobalShortcut();
        var searched = 0;

        using var shortcut = new SearchShortcut(system, new DesktopPreferences(fixture.Home), () => searched++, mac);

        Assert.Equal(SearchShortcutState.Registered, shortcut.State);
        Assert.Equal(mac ? _macDefault : _otherDefault, system.Registered);
        Assert.Equal(text, system.Registered!.Text);
        Assert.Equal(shown, system.Registered.Shown(mac));

        system.Press();

        Assert.Equal(1, searched);
    }

    [Fact]
    public void Off_gives_the_chord_back_and_another_chord_takes_its_place()
    {
        using var fixture = new TempVault();
        var system = new FakeGlobalShortcut();
        var preferences = new DesktopPreferences(fixture.Home);
        var searched = 0;
        using var shortcut = new SearchShortcut(system, preferences, () => searched++, mac: true);

        preferences.Update(preferences.Current with { SearchShortcut = GlobalChord.OffText });

        Assert.Equal(SearchShortcutState.Off, shortcut.State);
        Assert.Null(system.Registered);
        system.Press();
        Assert.Equal(0, searched);

        preferences.Update(preferences.Current with { SearchShortcut = "ctrl+alt+cmd+k" });

        Assert.Equal(SearchShortcutState.Registered, shortcut.State);
        Assert.Equal(new GlobalChord(ChordModifiers.Control | ChordModifiers.Alt | ChordModifiers.Command, ChordKey.K), system.Registered);
        Assert.Equal("ctrl+alt+cmd+k", AppSettings.Load(KeypasteHome.SettingsPath(fixture.Home)).SearchShortcut);
    }

    [Fact]
    public void A_chord_another_app_holds_is_said_and_registers_nothing_until_another_is_chosen()
    {
        using var fixture = new TempVault();
        var system = new FakeGlobalShortcut();
        system.Taken.Add(_otherDefault);
        var preferences = new DesktopPreferences(fixture.Home);

        using var shortcut = new SearchShortcut(system, preferences, () => { }, mac: false);

        Assert.Equal(SearchShortcutState.Taken, shortcut.State);
        Assert.Null(system.Registered);

        preferences.Update(preferences.Current with { SearchShortcut = "ctrl+shift+space" });

        Assert.Equal(SearchShortcutState.Registered, shortcut.State);
        Assert.Equal(new GlobalChord(ChordModifiers.Control | ChordModifiers.Shift, ChordKey.Space), system.Registered);
    }

    [Fact]
    public void A_desktop_with_no_system_wide_chord_never_registers_one()
    {
        using var fixture = new TempVault();
        var system = new FakeGlobalShortcut(PlatformShortcut.WaylandReason);

        using var shortcut = new SearchShortcut(system, new DesktopPreferences(fixture.Home), () => { }, mac: false);

        Assert.Equal(SearchShortcutState.Unavailable, shortcut.State);
        Assert.Empty(system.Asked);
    }

    [Fact]
    public void Disposing_it_gives_the_system_s_chord_back()
    {
        using var fixture = new TempVault();
        var system = new FakeGlobalShortcut();
        var shortcut = new SearchShortcut(system, new DesktopPreferences(fixture.Home), () => { }, mac: true);

        shortcut.Dispose();

        Assert.True(system.Disposed);
    }

    /// <summary>A hand edit is read whatever its order, spacing or capitals, but only as a chord this platform offers: none that types a character or that the system keeps.</summary>
    [Theory]
    [InlineData(true, null, "ctrl+cmd+k")]
    [InlineData(false, null, "alt+shift+k")]
    [InlineData(true, "CMD + Ctrl + K", "ctrl+cmd+k")]
    [InlineData(false, "shift+alt+space", "alt+shift+space")]
    [InlineData(true, "off", null)]
    [InlineData(false, "ctrl+alt+k", null)]
    [InlineData(false, "ctrl+cmd+k", null)]
    [InlineData(true, "alt+shift+k", null)]
    [InlineData(false, "win+alt+shift+k", null)]
    [InlineData(true, "ctrl+cmd+j", null)]
    [InlineData(true, "", null)]
    public void A_chord_is_read_only_as_one_this_platform_offers(bool mac, string? text, string? chosen) =>
        Assert.Equal(chosen, GlobalChord.Chosen(text, mac)?.Text);

    [Fact]
    public void No_offered_chord_holds_Ctrl_and_Alt_together_off_a_Mac_or_the_Windows_key()
    {
        Assert.All(GlobalChord.Offered(mac: false), chord =>
        {
            Assert.False(chord.Modifiers.HasFlag(ChordModifiers.Control) && chord.Modifiers.HasFlag(ChordModifiers.Alt), $"{chord.Text} is AltGr on Windows");
            Assert.False(chord.Modifiers.HasFlag(ChordModifiers.Command), $"{chord.Text} holds the Windows key");
        });

        // macOS 15 refuses a hot key of Option alone or Option and Shift, which type characters.
        Assert.All(GlobalChord.Offered(mac: true), chord =>
            Assert.True(chord.Modifiers.HasFlag(ChordModifiers.Command) || chord.Modifiers.HasFlag(ChordModifiers.Control), chord.Text));
    }

    [Theory]
    [InlineData(null, null, ":0", null)]
    [InlineData("x11", null, ":0", null)]
    [InlineData("wayland", "wayland-0", ":0", PlatformShortcut.WaylandReason)]
    [InlineData("tty", "wayland-0", ":1", PlatformShortcut.WaylandReason)]
    [InlineData("x11", null, null, PlatformShortcut.NoDisplayReason)]
    public void Linux_has_the_chord_only_on_an_X11_display(string? session, string? wayland, string? display, string? unavailable) =>
        Assert.Equal(unavailable, PlatformShortcut.LinuxUnavailable(session, wayland, display));

    [Fact]
    public void Settings_offers_Off_and_the_platform_s_chords_and_writes_the_choice_at_once()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home);
        var preferences = new DesktopPreferences(fixture.Home);
        var system = new FakeGlobalShortcut();
        using var shortcut = new SearchShortcut(system, preferences, () => { }, mac: true);
        using var model = new SettingsViewModel(session, fixture.Home, preferences, _ => { }) { SearchShortcut = shortcut };

        Assert.True(model.OffersSearchShortcut);
        Assert.True(model.SearchShortcutChangeable);
        Assert.Equal(["Off", "⌃⌘K", "⌃⌥⌘K", "⌥⌘K", "⇧⌘K"], model.SearchShortcutChoices.Select(choice => choice.Label));
        Assert.Equal("⌃⌘K", model.SearchShortcutChoice?.Label);
        Assert.False(model.HasSearchShortcutNote);

        model.SearchShortcutChoice = model.SearchShortcutChoices[0];

        Assert.Null(system.Registered);
        Assert.Equal("Off", model.SearchShortcutChoice?.Label);
        Assert.Equal(GlobalChord.OffText, AppSettings.Load(KeypasteHome.SettingsPath(fixture.Home)).SearchShortcut);

        model.SearchShortcutChoice = model.SearchShortcutChoices[3];

        Assert.Equal(new GlobalChord(ChordModifiers.Alt | ChordModifiers.Command, ChordKey.K), system.Registered);
        Assert.Equal("alt+cmd+k", AppSettings.Load(KeypasteHome.SettingsPath(fixture.Home)).SearchShortcut);
    }

    [Fact]
    public void Settings_says_a_chord_another_app_holds_and_clears_it_once_another_is_chosen()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home);
        var preferences = new DesktopPreferences(fixture.Home);
        var system = new FakeGlobalShortcut();
        system.Taken.Add(_otherDefault);
        using var shortcut = new SearchShortcut(system, preferences, () => { }, mac: false);
        using var model = new SettingsViewModel(session, fixture.Home, preferences, _ => { }) { SearchShortcut = shortcut };

        Assert.Equal("Alt+Shift+K", model.SearchShortcutChoice?.Label);
        Assert.Equal("Another app or the system already uses Alt+Shift+K, so it does nothing here. Choose other keys.", model.SearchShortcutNote);

        var said = new List<string?>();
        model.PropertyChanged += (_, e) => said.Add(e.PropertyName);
        model.SearchShortcutChoice = model.SearchShortcutChoices.Single(choice => choice.Label == "Ctrl+Shift+K");

        Assert.False(model.HasSearchShortcutNote);
        Assert.Contains(nameof(SettingsViewModel.SearchShortcutNote), said);
    }

    [Fact]
    public void Settings_says_why_a_Wayland_desktop_has_no_shortcut_and_offers_no_change()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home);
        var preferences = new DesktopPreferences(fixture.Home);
        using var shortcut = new SearchShortcut(new FakeGlobalShortcut(PlatformShortcut.WaylandReason), preferences, () => { }, mac: false);
        using var model = new SettingsViewModel(session, fixture.Home, preferences, _ => { }) { SearchShortcut = shortcut };

        Assert.False(model.SearchShortcutChangeable);
        Assert.Equal(PlatformShortcut.WaylandReason, model.SearchShortcutNote);
    }

    [Fact]
    public void Settings_shows_no_row_where_the_app_composes_no_shortcut()
    {
        using var fixture = new TempVault();
        using var session = new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home);
        using var model = new SettingsViewModel(session, fixture.Home, new DesktopPreferences(fixture.Home), _ => { });

        Assert.False(model.OffersSearchShortcut);
        Assert.Empty(model.SearchShortcutChoices);
    }
}
