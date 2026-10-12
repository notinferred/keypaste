using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Keypaste.App.Controls;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Linking;
using Keypaste.Core.Ownership;
using Xunit;
using static Keypaste.App.Tests.Session.JourneyDriver;

namespace Keypaste.App.Tests.Session;

/// <summary>
/// G.5: each start keeps the terminal's link current once, before the app runs, a start the running app takes included,
/// and Settings links it from the button the install exercise presses.
/// </summary>
public sealed class TerminalLinkJourneyTests
{
    private const string _olderImage = "/home/person/Downloads/keypaste-0.4.0.AppImage";

    [Fact]
    public void A_start_keeps_the_link_current_once_before_the_app_runs()
    {
        using var home = new TempHome();
        var link = FakeCliLink.At(CliLinkState.OtherCopy, linkedTo: _olderImage);
        var keptBeforeTheApp = 0;

        Assert.Equal(7, Program.Run([], home.Path, link, _ => { keptBeforeTheApp = link.KeepCurrents; return 7; }, TextWriter.Null, Wait));

        Assert.Equal(1, keptBeforeTheApp);
        Assert.Equal(1, link.KeepCurrents);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_start_the_running_app_takes_still_keeps_the_link_current(bool atLogin)
    {
        using var home = new TempHome();
        using var claim = Claim(home);
        using var listening = claim.Listen(() => Task.CompletedTask, _ => { });
        var link = FakeCliLink.At(CliLinkState.OtherCopy, linkedTo: _olderImage);

        Assert.Equal(0, Program.Run(atLogin ? ["--background"] : [], home.Path, link, NoSecondApp, TextWriter.Null, Wait));

        Assert.Equal(atLogin ? 0 : 1, claim.Requests);
        Assert.Equal(1, link.KeepCurrents);
    }

    [Fact]
    public Task Settings_links_the_terminals_keypaste() =>
        HeadlessSession.On(async () =>
        {
            using var fixture = new TempVault();
            var link = FakeCliLink.At(CliLinkState.Absent);

            using var lifetime = new ClassicDesktopStyleApplicationLifetime();
            Attach(lifetime);
            using var app = new App { CliLinkFor = () => link };
            app.Launch(lifetime);
            lifetime.ShutdownRequested += (_, e) => e.Cancel = true;

            Assert.Equal(0, link.Links);

            var main = lifetime.MainWindow!;
            main.Width = 1280;
            main.Height = 800;
            main.Show();
            var root = main.FindControl<ContentControl>("Root")!;

            var unlock = Assert.IsType<UnlockViewModel>(Assert.IsType<UnlockView>(root.Content).DataContext);
            Assert.True(unlock.Offer(fixture.Path_, null));
            WindowInput.Drain();
            WindowInput.Click(main, Named<MaskedInput>(main, "Password"));
            WindowInput.Type(main, TempVault.Password);
            Key(main, PhysicalKey.Enter, RawInputModifiers.None);
            await Until(() => root.Content is ShellView);
            var shell = Assert.IsType<ShellViewModel>(Assert.IsType<ShellView>(root.Content).DataContext);

            Chord(main, PhysicalKey.Digit4);
            Assert.IsType<SettingsViewModel>(shell.Content);
            Assert.StartsWith("Link this app's keypaste at", Named<TextBlock>(main, "CliLinkLine").Text, StringComparison.Ordinal);
            Assert.Contains("Link the keypaste command", AutomationNames(main));

            Press(main, Named<Button>(main, "LinkCli"));
            await Until(
                () => Named<TextBlock>(main, "CliLinkLine").Text?.StartsWith("A terminal runs this app's keypaste", StringComparison.Ordinal) == true,
                () => Named<TextBlock>(main, "CliLinkLine").Text ?? string.Empty);

            Assert.Equal(1, link.Links);
            Assert.DoesNotContain("Link the keypaste command", AutomationNames(main));

            Chord(main, PhysicalKey.L);
            await Until(() => root.Content is UnlockView);
            main.Close();
        });

    private static int NoSecondApp(AppClaim _) =>
        throw new InvalidOperationException("a start ran a second app beside the running one");

    private static AppClaim Claim(TempHome home)
    {
        Assert.True(AppClaim.TryAcquire(home.Path, out var claim));
        return claim;
    }
}
