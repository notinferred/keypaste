using System.Text;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Keypaste.App.Session;
using Keypaste.App.Tests.Session;
using Keypaste.App.ViewModels;
using Keypaste.App.Views;
using Keypaste.Core.Clients;
using Keypaste.Core.Processes;
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
        using var authority = new AppAuthority(new AppVaultSession(new ManualClock(), home: fixture.Home), null, () => new NobodyToAsk());
        using (var master = TempVault.Secret(TempVault.Password))
        {
            Assert.Equal(UnlockOutcome.Opened, authority.Session.TryUnlock(fixture.Path_, master.Value));
        }

        var connector = new ClientConnector(
            new InstalledRunner(),
            () => new McpServerCommand(Path.Combine(fixture.Home, "keypaste-mcp"), []),
            "nowhere",
            _ => null);
        using var model = new AgentActivityViewModel(authority, fixture.Home, new ManualClock(), connector: connector);
        var window = new Window { Content = new AgentActivityView { DataContext = model } };
        window.Show();

        var clients = Assert.Single(window.GetVisualDescendants().OfType<ComboBox>(), box => box.ItemCount == McpClientCatalog.All.Count);
        Assert.Same(McpClientCatalog.All[0], clients.SelectedItem);

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
