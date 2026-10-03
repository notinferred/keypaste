using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Keypaste.App.Navigation;
using Keypaste.App.ViewModels;
using Xunit;

namespace Keypaste.App.Tests.Rendering;

/// <summary>
/// An item's title is words, drawn in Instrument Sans; a key is machine text, drawn in Fragment Mono (N.7).
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

    private static FontFamily Sans => (FontFamily)Avalonia.Application.Current!.FindResource("KpSans")!;

    private static FontFamily Mono => (FontFamily)Avalonia.Application.Current!.FindResource("KpMono")!;

    private static TextStyle Heading(FontFamily family) => new(new Typeface(family, weight: FontWeight.SemiBold), 22, -0.66);

    private static TextStyle Row(FontFamily family) => new(new Typeface(family, weight: FontWeight.Medium), 13.5);

    private static RenderedShell Open(string title)
    {
        var shell = new RenderedShell("keypaste-typography-");
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
