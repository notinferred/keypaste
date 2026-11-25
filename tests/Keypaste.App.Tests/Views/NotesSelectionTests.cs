using Avalonia.Controls;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>An item's notes can be selected, and a copied selection is cleared after the countdown, as a copied password is.</summary>
public sealed class NotesSelectionTests
{
    private const string _notes = "Recovery codes: 4821-7730-1195";

    [Fact]
    public Task A_selection_copied_from_the_notes_goes_on_the_clipboard_as_a_secret_and_counts_down() => HeadlessSession.On(async () =>
    {
        using var shell = new RenderedShell(seed: vault => vault.AddEntry(new VaultEntry { Title = "recovery codes", GroupPath = "services", Notes = _notes }));
        var items = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        items.Selected = items.Rows.Single(row => row.Title == "recovery codes");
        RenderedShell.Drain();

        var notes = shell.Named<SelectableTextBlock>("NotesText");
        notes.SelectionStart = _notes.IndexOf("4821", StringComparison.Ordinal);
        notes.SelectionEnd = _notes.Length;
        notes.Copy();
        await items.Detail!.Clipboard.SettledAsync();

        Assert.Equal("4821-7730-1195", shell.Clipboard.Content);
        Assert.True(shell.Clipboard.ContentWasSetAsASecret);
        Assert.True(items.Detail.Clipboard.IsCounting);
    });
}
