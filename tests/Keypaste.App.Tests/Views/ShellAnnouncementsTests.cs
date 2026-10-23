using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Keypaste.App.Session;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>
/// The idle warning as a screen reader is told it: its first line said once, as the warning comes, while the seconds
/// it draws tick unannounced (F.68).
/// </summary>
public sealed class ShellAnnouncementsTests
{
    [Fact]
    public Task The_idle_warning_is_announced_once_while_its_seconds_tick() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();
        var clock = new ManualClock(AppClock.Start);
        using var session = new AppVaultSession(clock, TimeSpan.FromMinutes(5), home: fixture.Home);
        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, session.TryUnlock(fixture.Path_, master.Value));
        }

        using var shell = new ShellViewModel(session, fixture.Home, authority: null, clock: clock);
        var window = new MainWindow();
        window.FindControl<ContentControl>("Root")!.Content = new ShellView { DataContext = shell };
        window.Show();
        WindowInput.Drain();
        var live = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(), text => AutomationProperties.GetLiveSetting(text) != AutomationLiveSetting.Off);
        var peer = ControlAutomationPeer.CreatePeerForElement(live);
        var announced = new List<string?>();
        peer.PropertyChanged += (_, e) =>
        {
            if (e.Property == AutomationElementIdentifiers.NameProperty)
            {
                announced.Add(e.NewValue as string);
            }
        };

        clock.Advance(TimeSpan.FromMinutes(5) - AppVaultSession.WarningWindow + TimeSpan.FromMilliseconds(100));
        WindowInput.Drain();

        for (var tick = 0; tick < 3; tick++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            WindowInput.Drain();
        }

        Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Locking in 27 seconds." && text.IsEffectivelyVisible);
        Assert.Equal("Locking in 30 seconds.", peer.GetName());
        Assert.Equal(["Locking in 30 seconds."], announced);
        window.Close();
    });
}
