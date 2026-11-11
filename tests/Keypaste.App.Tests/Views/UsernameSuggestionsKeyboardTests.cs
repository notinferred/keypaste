using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Controls;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.Core;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>New item's username box completes the best username this vault uses in grey as one is typed: Tab takes it, Down lists the others, Escape dismisses, and no secret reaches it.</summary>
public sealed class UsernameSuggestionsKeyboardTests
{
    private const string _password = "SENTINEL-MAIL-PASSWORD-91c4e2";

    [Fact]
    public Task Typing_completes_the_best_match_in_grey_and_Tab_takes_it() => HeadlessSession.On(() =>
    {
        using var shell = Open(out var entries, out var box);

        shell.Window.KeyTextInput("dan");
        RenderedShell.Drain();

        Assert.False(box.IsDropDownOpen);
        Assert.Equal("@example.com", box.CompletionShown);
        Assert.Equal("dan", entries.NewItem!.Username);
        Assert.Contains("dan@example.com", entries.NewItem.SuggestedUsernamesSpoken, StringComparison.Ordinal);
        foreach (var secret in RenderedShell.Secrets.Append(_password))
        {
            AutomationSurface.AssertNothingExposes(shell.Window, secret);
        }

        Press(shell, PhysicalKey.Tab);

        Assert.Equal("dan@example.com", entries.NewItem.Username);
        Assert.Null(box.CompletionShown);
        Assert.True(box.IsKeyboardFocusWithin);
        Assert.True(entries.IsAdding);
    });

    [Fact]
    public Task Down_lists_the_matches_that_Enter_puts_in_the_box() => HeadlessSession.On(() =>
    {
        using var shell = Open(out var entries, out var box);

        shell.Window.KeyTextInput("example");
        RenderedShell.Drain();

        Assert.False(box.IsDropDownOpen);
        Assert.Null(box.CompletionShown);
        Assert.Equal(["dan@example.com", "ops@example.com"], entries.NewItem!.SuggestedUsernames);

        Press(shell, PhysicalKey.ArrowDown);
        Assert.True(box.IsDropDownOpen);
        Assert.Contains("mono", box.Classes);
        Press(shell, PhysicalKey.ArrowDown);
        Press(shell, PhysicalKey.Enter);

        Assert.False(box.IsDropDownOpen);
        Assert.Equal("dan@example.com", entries.NewItem.Username);
        Assert.True(entries.IsAdding);
    });

    [Fact]
    public Task Escape_dismisses_the_completion_and_keeps_what_was_typed() => HeadlessSession.On(() =>
    {
        using var shell = Open(out var entries, out var box);

        shell.Window.KeyTextInput("dan");
        RenderedShell.Drain();
        Assert.NotNull(box.CompletionShown);

        Press(shell, PhysicalKey.Escape);

        Assert.Null(box.CompletionShown);
        Assert.Equal("dan", entries.NewItem!.Username);
        Assert.True(entries.IsAdding);

        Press(shell, PhysicalKey.Tab);
        Assert.Equal("dan", entries.NewItem.Username);
    });

    [Fact]
    public void The_completion_is_the_first_match_that_starts_with_what_is_typed()
    {
        string[] matching = ["ops@dan.dev", "Dan@example.com", "dan.o@example.com"];

        Assert.Equal("Dan@example.com", UsernameSuggestions.Completion("dan", matching));
        Assert.Null(UsernameSuggestions.Completion("example", matching));
        Assert.Null(UsernameSuggestions.Completion(string.Empty, matching));
        Assert.Null(UsernameSuggestions.Completion("dan@example.com", ["dan@example.com"]));
        Assert.StartsWith("Completes to Dan@example.com", UsernameSuggestions.Spoken("dan", matching), StringComparison.Ordinal);
        Assert.StartsWith("3 usernames", UsernameSuggestions.Spoken("example", matching), StringComparison.Ordinal);
    }

    private static RenderedShell Open(out EntriesViewModel entries, out UsernameBox box)
    {
        var shell = new RenderedShell(
            "keypaste-username-suggestions-",
            vault =>
            {
                vault.AddEntry(new VaultEntry { Title = "mail", Username = "dan@example.com", Password = _password });
                vault.AddEntry(new VaultEntry { Title = "ops", Username = "ops@example.com", Password = _password });
            });
        entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
        entries.BeginAddCommand.Execute(null);
        RenderedShell.Drain();

        box = shell.Named<UsernameBox>("NewEntryUsername");
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
