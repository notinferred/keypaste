using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Keypaste.App.Session;
using Keypaste.App.Tests.Session;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>
/// An agent's limits on Automatic in a real visual tree: the day picker and the time of day hold the
/// end on this computer's clock, the limit in force is read with that clock's offset, and a day picked
/// reaches the panel.
/// </summary>
public sealed class AgentSettingsViewTests
{
    [Fact]
    public Task The_end_of_Automatic_is_a_day_and_a_time_of_day_on_this_computers_clock() => HeadlessSession.On(() =>
    {
        using var fixture = new TempVault();

        using (var vault = Vault.Open(fixture.Path_, TempVault.Password))
        {
            var limited = AgentAccess.Defaults with
            {
                Mode = AgentMode.Automatic,
                Limit = new AccessLimit { Until = new DateTimeOffset(2026, 12, 31, 16, 0, 0, TimeSpan.Zero) },
            };
            Assert.Equal(AgentAccessWrite.Written, new AgentAccessStore(vault).Write("ci-probe", limited));
            vault.Save();
        }

        var clock = new ManualClock(AppClock.Start) { Zone = TestZones.PlusTwo };
        using var authority = new AppAuthority(new AppVaultSession(clock, home: fixture.Home), null, () => new NobodyToAsk());

        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, authority.Session.TryUnlock(fixture.Path_, master.Value));
        }

        using var model = new AgentSettingsViewModel(authority, "ci-probe");
        var window = new Window { Content = new AgentSettingsView { DataContext = model } };
        window.Show();

        var day = Assert.Single(window.GetVisualDescendants().OfType<CalendarDatePicker>());
        var time = Assert.Single(window.GetVisualDescendants().OfType<TextBox>(), box => AutomationProperties.GetName(box) == "Automatic until this time of day");

        Assert.Equal(new DateTime(2026, 12, 31), day.SelectedDate);
        Assert.Equal("18:00", time.Text);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Now: until 2026-12-31 18:00 +02:00.");

        day.SelectedDate = new DateTime(2027, 1, 2);

        Assert.Equal(new DateTime(2027, 1, 2), model.UntilDate);
        window.Close();
    });
}
