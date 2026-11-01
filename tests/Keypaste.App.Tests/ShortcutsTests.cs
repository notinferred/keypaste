using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Keypaste.App.Navigation;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests;

/// <summary>
/// The window's chords fire on the command modifier alone (Q.1). Windows reports AltGr as Ctrl+Alt,
/// so a chord that also holds Alt is a character being typed, "ł" on a Polish layout or "²" on a
/// German one, and must reach the focused field instead of locking, opening or moving anything.
/// </summary>
public sealed class ShortcutsTests : IDisposable
{
    private static readonly RawInputModifiers _command =
        OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private static readonly RawInputModifiers _altGr = _command | RawInputModifiers.Alt;

    private readonly TempVault _vault = new();
    private readonly AppVaultSession _session;

    public ShortcutsTests() => _session = new AppVaultSession(new ManualClock(AppClock.Start), home: _vault.Home);

    public void Dispose()
    {
        _session.Dispose();
        _vault.Dispose();
    }

    [Fact]
    public Task A_chord_with_Alt_reaches_the_unlocked_window_and_locks_searches_and_moves_nothing() => HeadlessSession.On(() =>
    {
        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_vault.Path_, master.Value));
        }

        using var shell = new ShellViewModel(_session, _vault.Home, null, clock: new ManualClock(AppClock.Start), picker: new FakeVaultFilePicker());
        var window = new Window();
        using var shortcuts = new Shortcuts(window, _session, () => null, () => shell);
        var reached = Reached(window);
        window.Show();
        var searched = 0;
        shell.SearchFocusRequested += (_, _) => searched++;
        var before = shell.Current;

        window.KeyPressQwerty(PhysicalKey.L, _altGr);
        window.KeyPressQwerty(PhysicalKey.K, _altGr);
        window.KeyPressQwerty(PhysicalKey.Digit2, _altGr);
        window.KeyPressQwerty(PhysicalKey.Digit3, _altGr);

        Assert.Equal([Key.L, Key.K, Key.D2, Key.D3], reached);
        Assert.Equal(0, searched);
        Assert.Same(before, shell.Current);
        Assert.True(_session.IsUnlocked);

        window.KeyPressQwerty(PhysicalKey.Digit2, _command);

        Assert.NotSame(before, shell.Current);
        Assert.Equal(4, reached.Count);
        window.Close();
    });

    [Fact]
    public Task A_chord_with_Alt_on_the_unlock_screen_opens_no_file_picker() => HeadlessSession.On(() =>
    {
        var picker = new FakeVaultFilePicker();
        using var unlock = new UnlockViewModel(_session, _vault.Home, picker, () => { });
        var window = new Window();
        using var shortcuts = new Shortcuts(window, _session, () => unlock, () => null);
        var reached = Reached(window);
        window.Show();

        window.KeyPressQwerty(PhysicalKey.O, _altGr);

        Assert.Equal([Key.O], reached);
        Assert.Equal(0, picker.ExistingCalls);

        window.KeyPressQwerty(PhysicalKey.O, _command);

        Assert.Equal(1, picker.ExistingCalls);
        Assert.Single(reached);
        window.Close();
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task A_chord_runs_its_command_under_its_platforms_key_and_not_the_other(bool mac) => HeadlessSession.On(() =>
    {
        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, _session.TryUnlock(_vault.Path_, master.Value));
        }

        using var shell = new ShellViewModel(_session, _vault.Home, null, clock: new ManualClock(AppClock.Start), picker: new FakeVaultFilePicker());
        var window = new Window();
        var commands = new AppCommands(new AppCommandHost { Session = _session, Shell = () => shell, Unlock = () => null, Window = () => window }, mac);
        using var shortcuts = new Shortcuts(window, commands);
        var reached = Reached(window);
        window.Show();
        var searched = 0;
        shell.SearchFocusRequested += (_, _) => searched++;
        var key = mac ? RawInputModifiers.Meta : RawInputModifiers.Control;
        var other = mac ? RawInputModifiers.Control : RawInputModifiers.Meta;

        window.KeyPressQwerty(PhysicalKey.K, other);
        window.KeyPressQwerty(PhysicalKey.Comma, other);

        Assert.Equal([Key.K, Key.OemComma], reached);
        Assert.Equal(0, searched);
        Assert.Equal(DestinationKind.Entries, shell.Current.Kind);

        window.KeyPressQwerty(PhysicalKey.K, key);
        window.KeyPressQwerty(PhysicalKey.F, key);
        Assert.Equal(2, searched);

        window.KeyPressQwerty(PhysicalKey.Digit3, key);
        Assert.Equal(DestinationKind.Trash, shell.Current.Kind);

        window.KeyPressQwerty(PhysicalKey.Comma, key);
        Assert.Equal(DestinationKind.Settings, shell.Current.Kind);
        Assert.Equal(2, reached.Count);
        window.Close();
    });

    /// <summary>The key presses the window's chords left unhandled, which a focused field then receives.</summary>
    private static List<Key> Reached(Window window)
    {
        var reached = new List<Key>();
        window.AddHandler(InputElement.KeyDownEvent, (object? _, KeyEventArgs e) => reached.Add(e.Key), RoutingStrategies.Bubble);
        return reached;
    }
}
