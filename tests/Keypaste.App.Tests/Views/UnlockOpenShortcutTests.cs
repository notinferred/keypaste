using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Keypaste.App.Session;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>
/// <c>Ctrl/Cmd+O</c> on the locked screen, delivered as a key event to the window launch builds.
/// </summary>
/// <remarks>
/// The chord is bound by <see cref="App.Bind"/>, the method launch calls, so a test that armed a
/// handler of its own could not pass against an app that never binds it. Every assertion that
/// nothing happened is made after asserting the picker was reached, or that it was not.
/// </remarks>
public sealed class UnlockOpenShortcutTests
{
    private static readonly RawInputModifiers _command =
        OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    [Fact]
    public Task The_chord_opens_the_picker_and_lands_in_the_vault_it_returns() => HeadlessSession.On(async () =>
    {
        using var fixture = new TempVault();
        var picker = new FakeVaultFilePicker { ExistingPath = fixture.Path_ };
        using var screen = new LockedScreen(fixture, picker);

        Assert.False(screen.Model.HasSelection);

        screen.PressOpen();

        Assert.Equal(1, picker.ExistingCalls);
        Assert.Equal(Path.GetFullPath(fixture.Path_), screen.Model.SelectedPath);

        foreach (var c in TempVault.Password)
        {
            screen.Model.Type(c);
        }

        await screen.Model.UnlockAsync();

        Assert.Equal(1, screen.Landed);
        Assert.Equal(Path.GetFullPath(fixture.Path_), screen.Session.VaultPath);
    });

    [Fact]
    public Task Cancelling_the_picker_leaves_the_screen_and_every_file_alone() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        fixture.RememberSelf();

        var picker = new FakeVaultFilePicker { ExistingPath = null };
        using var screen = new LockedScreen(fixture, picker);

        var recentPath = KeypasteHome.RecentPath(fixture.Home);
        var selected = screen.Model.SelectedPath;
        var recent = File.ReadAllBytes(recentPath);
        var vault = File.ReadAllBytes(fixture.Path_);
        var listing = Listing(fixture.Home);

        screen.PressOpen();

        Assert.Equal(1, picker.ExistingCalls);
        Assert.Equal(selected, screen.Model.SelectedPath);
        Assert.False(screen.Model.HasMessage);
        Assert.Equal(recent, File.ReadAllBytes(recentPath));
        Assert.Equal(vault, File.ReadAllBytes(fixture.Path_));
        Assert.Equal(listing, Listing(fixture.Home));
    });

    [Fact]
    public Task A_damaged_vault_it_returns_is_offered_for_restore() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();

        using (var vault = Vault.Open(fixture.Path_, TempVault.Password))
        {
            vault.AddEntry(new VaultEntry { Title = "second", Password = "another-secret" });
            vault.Save();
        }

        Assert.NotEmpty(VaultBackups.List(fixture.Path_));
        File.WriteAllText(fixture.Path_, "a sync tool's conflict note, where a vault was");

        var picker = new FakeVaultFilePicker { ExistingPath = fixture.Path_ };
        using var screen = new LockedScreen(fixture, picker);

        screen.PressOpen();

        Assert.Equal(1, picker.ExistingCalls);
        Assert.Equal(Path.GetFullPath(fixture.Path_), screen.Model.SelectedPath);
        Assert.True(screen.Model.IsRestoreOnly);
        Assert.True(screen.Model.OffersRestore);
        Assert.Contains("kept beside it", screen.Model.Message, StringComparison.Ordinal);
    });

    [Fact]
    public Task The_chord_does_nothing_while_the_create_form_is_showing() => HeadlessSession.On(async () =>
    {
        using var fixture = new TempVault();
        var picker = new FakeVaultFilePicker
        {
            NewPath = Path.Combine(fixture.Home, "new.kdbx"),
            ExistingPath = fixture.Path_,
        };
        using var screen = new LockedScreen(fixture, picker);

        await screen.Model.StartCreateAsync();
        Assert.True(screen.Model.IsCreating);

        screen.PressOpen();

        Assert.Equal(0, picker.ExistingCalls);
        Assert.True(screen.Model.IsCreating);
        Assert.False(screen.Model.HasSelection);
    });

    [Fact]
    public Task Typing_an_o_into_the_password_does_not_open_the_picker() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        fixture.RememberSelf();

        var picker = new FakeVaultFilePicker { ExistingPath = fixture.ImposterPath };
        using var screen = new LockedScreen(fixture, picker);

        screen.Window.KeyPressQwerty(PhysicalKey.O, RawInputModifiers.None);
        screen.Window.KeyTextInput("o");
        screen.Window.KeyReleaseQwerty(PhysicalKey.O, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, screen.Model.MaskedLength);
        Assert.Equal(0, picker.ExistingCalls);
    });

    private static string[] Listing(string directory) =>
        [.. Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];

    /// <summary>The unlock screen in a shown window, with the chords launch binds.</summary>
    private sealed class LockedScreen : IDisposable
    {
        private readonly Shortcuts _shortcuts;

        internal LockedScreen(TempVault fixture, FakeVaultFilePicker picker)
        {
            Session = new AppVaultSession(new ManualClock());
            Model = new UnlockViewModel(Session, fixture.Home, picker, () => Landed++);
            Window = new Window { Content = new UnlockView { DataContext = Model } };
            _shortcuts = App.Bind(Window, Session, () => Model, () => null);
            Window.Show();
            Dispatcher.UIThread.RunJobs();
        }

        internal AppVaultSession Session { get; }

        internal UnlockViewModel Model { get; }

        internal Window Window { get; }

        internal int Landed { get; private set; }

        internal void PressOpen()
        {
            Window.KeyPressQwerty(PhysicalKey.O, _command);
            Window.KeyReleaseQwerty(PhysicalKey.O, _command);
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            _shortcuts.Dispose();
            Window.Close();
            Model.Dispose();
            Session.Dispose();
        }
    }
}
