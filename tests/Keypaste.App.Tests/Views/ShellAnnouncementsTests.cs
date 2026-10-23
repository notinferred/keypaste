using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Session;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>
/// What a screen reader is told by the shell's text that is named apart from what it shows: the idle warning,
/// announced once while its seconds tick, and the Projects heading, said as it is written rather than in capitals.
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
        var countdown = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(text => AutomationProperties.GetLiveSetting(text) == AutomationLiveSetting.Polite);
        var peer = ControlAutomationPeer.CreatePeerForElement(countdown);
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

        Assert.Equal("Locking in 27 seconds.", countdown.Text);
        Assert.Equal("Locking in 30 seconds.", peer.GetName());
        Assert.Equal(["Locking in 30 seconds."], announced);
        window.Close();
    });

    [Fact]
    public Task The_projects_heading_is_said_as_it_is_written() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-heading-name-");
        var heading = shell.Window.GetVisualDescendants().OfType<NamedTextBlock>().Single(text => text.Text == "PROJECTS");

        Assert.Equal("Projects", ControlAutomationPeer.CreatePeerForElement(heading).GetName());
    });
}
