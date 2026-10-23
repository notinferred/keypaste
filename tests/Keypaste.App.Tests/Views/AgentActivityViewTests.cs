using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.App.Session;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.Tests.Session;
using Keypaste.App.Tests.ViewModels;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>
/// Agents in a real visual tree: a found tool's row offers Connect by the tool's name, its pane shows the
/// plan text the model holds, and a row's Settings disclosure hosts the agent's settings panel.
/// </summary>
public sealed class AgentActivityViewTests
{
    [Fact]
    public Task A_tools_pane_shows_the_plan_it_will_run_and_its_Settings_host_the_panel() => HeadlessSession.On(async () =>
    {
        using var fixture = new TempVault();
        using var authority = new AppAuthority(new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home), null, () => new NobodyToAsk());
        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, authority.Session.TryUnlock(fixture.Path_, master.Value));
        }

        var tools = new FakeTools(Path.Combine(fixture.Home, "computer")).ClaudeCode(connected: false);
        using var model = new AgentActivityViewModel(authority, fixture.Home, new ManualClock(AppClock.Start), action => Dispatcher.UIThread.Post(action), tools.Connector());
        var window = new Window { Content = new AgentActivityView { DataContext = model } };
        window.Show();
        await model.Looking;
        WindowInput.Drain();

        var row = model.Rows.Single(row => row.Label == "claude-code");
        var connect = window.GetVisualDescendants().OfType<Button>().Single(button => button is { Name: "ConnectTool", IsEffectivelyVisible: true } && button.DataContext == row);
        Assert.Equal("Connect Claude Code", AutomationProperties.GetName(connect));

        connect.Command!.Execute(connect.CommandParameter);
        await model.Opening;
        WindowInput.Drain();

        var shown = window.GetVisualDescendants().OfType<SelectableTextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text).ToList();
        Assert.Contains(model.Connection!.Preview, shown);
        Assert.Contains("claude mcp add --scope user --transport stdio keypaste --", model.Connection.Preview, StringComparison.Ordinal);
        var plan = window.GetVisualDescendants().OfType<SelectableTextBlock>().Single(block => block.Name == "ConnectPlan");
        Assert.Equal(model.Connection.Preview, ControlAutomationPeer.CreatePeerForElement(plan).GetName());

        var settings = window.GetVisualDescendants().OfType<Expander>().Single(expander => expander.IsEffectivelyVisible && expander.DataContext == row);
        Assert.Equal("Settings for Claude Code", AutomationProperties.GetName(settings));
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<AgentSettingsView>(), panel => panel.IsEffectivelyVisible);

        settings.IsExpanded = true;
        WindowInput.Drain();

        Assert.True(row.IsOpen);
        var panel = Assert.Single(window.GetVisualDescendants().OfType<AgentSettingsView>(), panel => panel.IsEffectivelyVisible);
        Assert.Same(row.Settings, panel.DataContext);
        Assert.Equal("claude-code", row.Settings!.Agent);
        window.Close();
    });
}
