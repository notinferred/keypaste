using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
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
/// An agent's settings panel in a real visual tree: the day picker and the time of day hold Automatic's
/// end on this computer's clock, the limit in force is read with that clock's offset, a day picked
/// reaches the panel, and a chip keeps the keyboard when the panel rebuilds it.
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

    /// <summary>A chip pressed from the keyboard keeps it once the panel reads the change back and rebuilds its chips (N.3).</summary>
    [Fact]
    public Task A_chip_keeps_the_keyboard_after_its_change() => HeadlessSession.On(async () =>
    {
        using var panel = new SettingsPanel();
        var ask = panel.Chip("mode", "Ask every time");
        ask.Focus(NavigationMethod.Tab);
        Assert.Same(ask, panel.Focused);

        panel.Press(PhysicalKey.Space);
        await panel.Model.Changing;
        Dispatcher.UIThread.RunJobs();

        var now = panel.Chip("mode", "Ask every time");
        Assert.NotSame(ask, now);
        Assert.True(Assert.IsType<SettingChoice>(now.DataContext).IsChosen);
        Assert.Same(now, panel.Focused);
    });

    /// <summary>A pick keeps the keyboard as it is toggled, and a pick whose environment is gone hands it to its group's first chip.</summary>
    [Fact]
    public Task A_pick_keeps_the_keyboard_and_one_that_goes_hands_it_to_the_first_pick() => HeadlessSession.On(async () =>
    {
        using var panel = new SettingsPanel();
        await panel.Model.ChooseAsync(panel.Model.SeeScopes.Single(choice => choice.Words == "Only these"));
        Dispatcher.UIThread.RunJobs();

        var dev = panel.Chip("pick", "billing:dev");
        dev.Focus(NavigationMethod.Tab);
        panel.Press(PhysicalKey.Space);
        Dispatcher.UIThread.RunJobs();

        var picked = panel.Chip("pick", "billing:dev");
        Assert.NotSame(dev, picked);
        Assert.True(Assert.IsType<SettingChoice>(picked.DataContext).IsChosen);
        Assert.Same(picked, panel.Focused);

        await panel.Model.ChooseAsync(panel.Model.SeeChoices.Single(choice => choice.Words == "billing:prod"));
        await panel.Model.ApplyPicksAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.True(panel.Model.IsOnly);

        panel.Chip("pick", "billing:prod").Focus(NavigationMethod.Tab);
        panel.Vault.RemoveEntry(ProjectVariables.Home("billing", "prod"), out _);
        panel.Model.Read();
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(panel.Model.SeeChoices, choice => choice.Words == "billing:prod");
        Assert.Same(panel.Chip("pick", "billing"), panel.Focused);
    });

    /// <summary>One agent's settings panel in a window, over a vault holding billing's dev and prod.</summary>
    private sealed class SettingsPanel : IDisposable
    {
        private readonly TempVault _fixture = new();
        private readonly AppAuthority _authority;

        internal SettingsPanel()
        {
            using (var vault = Vault.Open(_fixture.Path_, TempVault.Password))
            {
                ProjectVariables.Set(vault, "billing", "STRIPE_KEY", "dev-value");
                ProjectVariables.Set(vault, "billing", "prod", "STRIPE_KEY", "prod-value");
                vault.Save();
            }

#pragma warning disable CA2000
            _authority = new AppAuthority(new AppVaultSession(new ManualClock(AppClock.Start), home: _fixture.Home), null, () => new NobodyToAsk());
#pragma warning restore CA2000

            using (var master = TempVault.Secret(TempVault.Password))
            {
                Assert.Equal(UnlockOutcome.Opened, _authority.Session.TryUnlock(_fixture.Path_, master.Value));
            }

            Model = new AgentSettingsViewModel(_authority, "ci-probe");
            Window = new Window { Content = new AgentSettingsView { DataContext = Model } };
            Window.Show();
            Dispatcher.UIThread.RunJobs();
        }

        internal AgentSettingsViewModel Model { get; }

        internal Window Window { get; }

        internal Vault Vault => _authority.Session.Unlocked!;

        internal IInputElement? Focused => Window.FocusManager?.GetFocusedElement();

        /// <summary>The chip on screen that sets <paramref name="setting"/> and says <paramref name="words"/>, outside any project row.</summary>
        internal Button Chip(string setting, string words) =>
            Window.GetVisualDescendants().OfType<Button>().Single(button =>
                button.IsEffectivelyVisible && button.DataContext is SettingChoice { Project: null } choice && choice.Setting == setting && choice.Words == words);

        internal void Press(PhysicalKey key)
        {
            Window.KeyPressQwerty(key, RawInputModifiers.None);
            Window.KeyReleaseQwerty(key, RawInputModifiers.None);
        }

        public void Dispose()
        {
            Window.Close();
            Model.Dispose();
            _authority.Dispose();
            _fixture.Dispose();
        }
    }
}
