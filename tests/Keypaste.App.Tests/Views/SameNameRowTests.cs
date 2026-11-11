using Avalonia.Controls;
using Avalonia.VisualTree;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>What the list draws to tell two items of one name apart, once at each width.</summary>
public sealed class SameNameRowTests
{
    [Fact]
    public Task The_tell_shows_after_the_title_of_each_item_sharing_a_name() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell(seed: Seed);
        shell.Show<EntriesViewModel>(DestinationKind.Entries);
        RenderedShell.Drain();

        Assert.Equal(["alice@example.test", "bob@example.test"], Tells(shell).Select(tell => tell.Text).Order());
        Assert.All(Tells(shell), tell => Assert.Equal(tell.Text, ToolTip.GetTip(tell)));
    });

    [Fact]
    public Task The_username_cell_holds_the_whole_username_in_its_tooltip() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell(seed: Seed);
        shell.Show<EntriesViewModel>(DestinationKind.Entries);
        RenderedShell.Drain();

        var cell = shell.Window.GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == "alice@example.test" && block.IsEffectivelyVisible);

        Assert.Equal("alice@example.test", ToolTip.GetTip(cell));
    });

    private static void Seed(Vault vault)
    {
        vault.AddEntry(new VaultEntry { Title = "Mail", Username = "alice@example.test", GroupPath = "services" });
        vault.AddEntry(new VaultEntry { Title = "Mail", Username = "bob@example.test", GroupPath = "services" });
        vault.AddEntry(new VaultEntry { Title = "Forum", Username = "carol@example.test", GroupPath = "services" });
    }

    private static List<TextBlock> Tells(RenderedShell shell) =>
        [.. shell.Window.GetVisualDescendants().OfType<TextBlock>().Where(block => block.Name == "SharedNameTell" && block.IsEffectivelyVisible)];
}
