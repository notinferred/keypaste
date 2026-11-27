using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>Copying the chosen item's password, username and one-time code from the keyboard, from the search, the list or the pane, through the clipboard that clears itself.</summary>
public sealed class CopyByKeyTests
{
    private const string _password = "SENTINEL-KEYBOARD-COPY-PASSWORD-2c7f";

    private static readonly RawInputModifiers _command =
        OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private static void Seed(Vault vault)
    {
        vault.AddEntry(new VaultEntry { Title = "box", Username = "ops", Password = _password });
        Assert.True(vault.SetTotp(new EntryName(string.Empty, "box"), "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ"));
        vault.AddEntry(new VaultEntry { Title = "bare", Password = "SENTINEL-BARE-PASSWORD-81d3" });
    }

    private static void Chord(RenderedShell shell, PhysicalKey key, RawInputModifiers modifiers)
    {
        shell.Window.KeyPressQwerty(key, modifiers);
        shell.Window.KeyReleaseQwerty(key, modifiers);
        RenderedShell.Drain();
    }

    private static void SearchAndChoose(RenderedShell shell, string query)
    {
        Chord(shell, PhysicalKey.F, _command);
        WindowInput.Type(shell.Window, query);
        RenderedShell.Drain();
        Chord(shell, PhysicalKey.Enter, RawInputModifiers.None);
    }

    private static bool Copied(RenderedShell shell, int before) =>
        SpinWait.SpinUntil(() => { RenderedShell.Drain(); return shell.Clipboard.SetCount > before; }, TimeSpan.FromSeconds(10));

    [Fact]
    public Task Copy_in_the_search_copies_the_chosen_items_password_unless_the_searchs_text_is_selected() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-copy-search-", Seed);
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        var search = shell.Named<TextBox>("ListSearch");

        SearchAndChoose(shell, "box");
        Assert.Equal("box", entries.Selected?.Title);
        Assert.True(search.IsFocused);

        Chord(shell, PhysicalKey.C, _command);
        Assert.True(Copied(shell, 0), "Copy in the search copied nothing");
        Assert.Equal(_password, shell.Clipboard.Content);
        Assert.True(shell.Clipboard.ContentWasSetAsASecret);
        Assert.True(shell.Shell.Clipboard.IsCounting, "the copied password is not counting down");

        // With its own text selected, Copy is the field's.
        search.SelectAll();
        var sets = shell.Clipboard.SetCount;
        Assert.Null(shell.Commands.ForChord(Key.C, OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control));
        Chord(shell, PhysicalKey.C, _command);
        Assert.Equal(sets, shell.Clipboard.SetCount);
    });

    [Fact]
    public Task Shift_Copy_copies_the_username_and_T_the_one_time_code_from_the_search_the_list_and_the_pane() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-copy-chords-", Seed);
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);

        SearchAndChoose(shell, "box");

        Chord(shell, PhysicalKey.C, _command | RawInputModifiers.Shift);
        Assert.True(Copied(shell, 0), "Shift Copy in the search copied nothing");
        Assert.Equal("ops", shell.Clipboard.Content);

        var list = shell.Named<ListBox>("Rows");
        list.ContainerFromItem(entries.Selected!)!.Focus(NavigationMethod.Directional);
        Chord(shell, PhysicalKey.T, _command);
        Assert.True(Copied(shell, 1), "the one-time code's chord from the list copied nothing");
        Assert.Matches("^[0-9]{6}$", shell.Clipboard.Content);
        Assert.True(shell.Clipboard.ContentWasSetAsASecret);
        Assert.True(shell.Shell.Clipboard.IsCounting, "the copied code is not counting down");

        shell.Named<Button>("CopyPassword").Focus();
        Chord(shell, PhysicalKey.C, _command | RawInputModifiers.Shift);
        Assert.True(Copied(shell, 2), "Shift Copy from the pane copied nothing");
        Assert.Equal("ops", shell.Clipboard.Content);
    });

    [Fact]
    public Task Copy_username_and_the_one_time_code_are_off_for_an_item_without_them() => HeadlessSession.On(() =>
    {
        using var shell = new RenderedShell("keypaste-copy-empty-", Seed);
        var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);

        SearchAndChoose(shell, "bare");
        Assert.Equal("bare", entries.Selected?.Title);

        var item = shell.Commands.Menus.Single(menu => menu.Header == "Item").Entries.OfType<AppCommand>().ToList();
        Assert.False(item.Single(command => command.Header == "Copy username").CanRun());
        Assert.False(item.Single(command => command.Header == "Copy the one-time code").CanRun());

        Chord(shell, PhysicalKey.C, _command | RawInputModifiers.Shift);
        Chord(shell, PhysicalKey.T, _command);
        Assert.Equal(0, shell.Clipboard.SetCount);
    });
}
