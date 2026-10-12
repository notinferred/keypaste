using System.Text;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Keypaste.App.Session;
using Keypaste.App.Tests.Session;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Clients;
using Keypaste.Core.Processes;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>
/// The Connect section in a real visual tree: its nested data context resolves, the clients are
/// offered by name and the preview a person confirms is the text the model holds.
/// </summary>
public sealed class AgentActivityViewTests
{
    [Fact]
    public Task The_connect_section_offers_the_clients_and_shows_the_preview_it_will_run() => HeadlessSession.On(async () =>
    {
        using var fixture = new TempVault();
        using var authority = new AppAuthority(new AppVaultSession(new ManualClock(AppClock.Start), home: fixture.Home), null, () => new NobodyToAsk());
        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, authority.Session.TryUnlock(fixture.Path_, master.Value));
        }

        var bin = Directory.CreateDirectory(Path.Combine(fixture.Home, "bin")).FullName;
        var claude = Path.Combine(bin, OperatingSystem.IsWindows() ? "claude.exe" : "claude");
        File.WriteAllText(claude, "#!/bin/sh\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(claude, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }

        var server = new McpServerCommand(Path.Combine(fixture.Home, "keypaste"), [McpServerLocator.BridgeArgument]);
        var connector = new ClientConnector(
            new InstalledRunner(),
            McpClientMachine.From(name => name is "PATH" ? bin : name is "HOME" or "USERPROFILE" ? fixture.Home : null, McpClientMachine.CurrentPlatform),
            ApproverOverride: null,
            () => new McpBridgeTargetResult(new McpBridgeTarget(server, Link: null, Note: null), Error: null),
            _ => null);
        using var model = new AgentActivityViewModel(authority, fixture.Home, new ManualClock(AppClock.Start), connector: connector);
        model.ToggleConnectCommand.Execute(null);
        var window = new Window { Content = new AgentActivityView { DataContext = model } };
        window.Show();

        var offered = McpClientCatalog.For(McpClientMachine.CurrentPlatform);
        var clients = Assert.Single(window.GetVisualDescendants().OfType<ComboBox>(), box => box.ItemCount == offered.Count);
        Assert.Equal(offered, clients.Items.Cast<McpClient>());
        Assert.Same(offered[0], clients.SelectedItem);

        await model.Connect!.PreviewConnectCommand.ExecuteAsync();
        window.UpdateLayout();

        var shown = window.GetVisualDescendants().OfType<SelectableTextBlock>().Select(block => block.Text).ToList();
        Assert.Contains(model.Connect.Preview, shown);
        Assert.Contains("claude mcp add --scope user --transport stdio keypaste --", model.Connect.Preview, StringComparison.Ordinal);
    });

    private sealed class InstalledRunner : IProcessRunner
    {
        public ProcessResult Run(string fileName, IReadOnlyList<string> arguments, string? stdin, Encoding stdinEncoding, TimeSpan timeout) =>
            new(ToolFound: true, ExitCode: 0, string.Empty, string.Empty);
    }
}
