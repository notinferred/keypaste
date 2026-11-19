using Avalonia.Controls;
using Avalonia.VisualTree;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>What the list draws to tell two items of one title apart.</summary>
public sealed class SameNameRowTests
{
    [Fact]
    public Task Items_sharing_a_title_are_told_apart_by_their_second_line_alone() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell(seed: Seed);
        shell.Show<EntriesViewModel>(DestinationKind.Entries);
        RenderedShell.Drain();

        var mail = shell.Window.GetVisualDescendants().OfType<TextBlock>()
            .Where(block => block.Name == "RowSecondLine" && block.IsEffectivelyVisible && block.DataContext is EntryRow { Title: "Mail" })
            .Select(line => line.Text);

        Assert.Equal(["alice@example.test", "bob@example.test"], mail.Order());
        Assert.Equal(2, shell.Window.GetVisualDescendants().OfType<TextBlock>().Count(block => block.IsEffectivelyVisible && block.Text is "alice@example.test" or "bob@example.test"));
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

}
