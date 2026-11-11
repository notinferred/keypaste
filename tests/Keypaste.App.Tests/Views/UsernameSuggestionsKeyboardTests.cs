using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Controls;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>New item's username box offers the usernames this vault uses as one is typed: Down and Enter fill it, Escape closes the list, and no secret reaches it.</summary>
public sealed class UsernameSuggestionsKeyboardTests
{
    private const string _password = "SENTINEL-MAIL-PASSWORD-91c4e2";

    [Fact]
    public Task Typing_offers_a_used_email_that_Down_and_Enter_put_in_the_box() => HeadlessSession.On(() =>
    {
        using var shell = Open(out var entries, out var box);

        shell.Window.KeyTextInput("example");
        RenderedShell.Drain();

        Assert.True(box.IsDropDownOpen);
        Assert.Equal(["dan@example.com"], entries.NewItem!.SuggestedUsernames);
        foreach (var secret in RenderedShell.Secrets.Append(_password))
        {
            AutomationSurface.AssertNothingExposes(shell.Window, secret);
        }

        Press(shell, PhysicalKey.ArrowDown);
        Press(shell, PhysicalKey.Enter);

        Assert.False(box.IsDropDownOpen);
        Assert.Equal("dan@example.com", entries.NewItem.Username);
        Assert.True(entries.IsAdding);
    });

    [Fact]
    public Task Escape_closes_the_list_and_keeps_what_was_typed() => HeadlessSession.On(() =>
    {
        using var shell = Open(out var entries, out var box);

        shell.Window.KeyTextInput("dan");
        RenderedShell.Drain();
        Assert.True(box.IsDropDownOpen);

        Press(shell, PhysicalKey.Escape);

        Assert.False(box.IsDropDownOpen);
        Assert.Equal("dan", entries.NewItem!.Username);
        Assert.True(entries.IsAdding);
    });

    private static RenderedShell Open(out EntriesViewModel entries, out AutoCompleteBox box)
    {
        var shell = new RenderedShell(
            "keypaste-username-suggestions-",
            vault => vault.AddEntry(new VaultEntry { Title = "mail", Username = "dan@example.com", Password = _password }));
        entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        entries.BeginAddCommand.Execute(null);
        RenderedShell.Drain();

        box = shell.Named<AutoCompleteBox>("NewEntryUsername");
        box.GetVisualDescendants().OfType<TextBox>().First().Focus(NavigationMethod.Tab);
        RenderedShell.Drain();
        return shell;
    }

    private static void Press(RenderedShell shell, PhysicalKey key)
    {
        shell.Window.KeyPressQwerty(key, RawInputModifiers.None);
        shell.Window.KeyReleaseQwerty(key, RawInputModifiers.None);
        RenderedShell.Drain();
    }
}
