using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Keypaste.App.Navigation;
using Keypaste.App.ViewModels;
using Xunit;

namespace Keypaste.App.Tests.Rendering;

/// <summary>
/// An item's title is words, drawn in the system's interface face; a key is machine text, drawn in its monospace (N.7).
/// </summary>
/// <remarks>
/// Read from the frame: each claim that a text is drawn in one face sits beside the claim that the
/// same text in the other face is not, in the same place.
/// </remarks>
public sealed class TitleTypographyTests
{
    [Fact]
    public Task A_login_is_titled_in_sans_and_its_keys_are_mono() => HeadlessSession.On(() =>
    {
        using var shell = Open("github");
        var frame = shell.Frame();
        var title = frame.Of(shell.Named<TextBlock>("EntryTitle"), shell.Window);

        Assert.True(frame.Shows("github", Heading(Sans), title), "the pane's title is not drawn in sans");
        Assert.False(frame.Shows("github", Heading(Mono), title), "the pane's title is drawn in mono");

        var row = frame.Of(RowTitle(shell, "github"), shell.Window);
        Assert.True(frame.Shows("github", Row(Sans), row), "the list row's title is not drawn in sans");
        Assert.False(frame.Shows("github", Row(Mono), row), "the list row's title is drawn in mono");

        Assert.True(frame.Shows("API_TOKEN", new TextStyle(new Typeface(Mono), 12.5), frame.Everywhere), "a field's key is not drawn in mono");
        Assert.False(frame.Shows("API_TOKEN", new TextStyle(new Typeface(Sans), 12.5), frame.Everywhere), "a field's key is drawn in sans");
    });

    [Fact]
    public Task The_sidebar_heads_the_vault_by_its_title_in_sans_and_the_title_bar_locks_by_its_icon() => HeadlessSession.On(() =>
    {
        using var shell = Open("github", secondVault: "acme");
        var frame = shell.Frame();
        var vault = shell.Named<ListBox>("Nav").GetVisualDescendants().OfType<TextBlock>().Single(block => block.Classes.Contains("vault") && block.Text != "acme");
        var heading = frame.Of(vault, shell.Window);

        Assert.Equal("vault", vault.Text);
        Assert.True(frame.Shows("vault", new TextStyle(new Typeface(Sans, weight: FontWeight.SemiBold), 13), heading), "the vault's title is not drawn in sans");
        Assert.False(frame.Shows("vault", new TextStyle(new Typeface(Mono), 12), heading), "the vault's title is drawn in mono");
        Assert.Equal(Keypaste.App.Resources.Strings.LockNow, Avalonia.Automation.AutomationProperties.GetName(shell.Named<Button>("LockButton")));
    });

    private static FontFamily Sans => (FontFamily)Avalonia.Application.Current!.FindResource("KpSans")!;

    private static FontFamily Mono => (FontFamily)Avalonia.Application.Current!.FindResource("KpMono")!;

    private static TextStyle Heading(FontFamily family) => new(new Typeface(family, weight: FontWeight.Bold), 20, -0.2);

    private static TextStyle Row(FontFamily family) => new(new Typeface(family, weight: FontWeight.SemiBold), 13);

    private static RenderedShell Open(string title, string? secondVault = null)
    {
        var shell = new RenderedShell("keypaste-typography-", secondVault: secondVault);
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        entries.Selected = entries.Rows.Single(row => row.Title == title);
        RenderedShell.Drain();
        return shell;
    }

    private static TextBlock RowTitle(RenderedShell shell, string title) =>
        shell.Window.GetVisualDescendants()
            .OfType<ListBoxItem>()
            .Single(item => item.DataContext is EntryRow row && row.Title == title)
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .First(block => block.Text == title);
}
