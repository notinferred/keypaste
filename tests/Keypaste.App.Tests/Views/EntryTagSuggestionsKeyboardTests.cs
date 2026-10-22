using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Keypaste.App.Navigation;
using Keypaste.App.Tests.Rendering;
using Keypaste.App.ViewModels;
using Keypaste.Core.Tests;
using Xunit;

namespace Keypaste.App.Tests.Views;

/// <summary>The item pane's tag box offers the vault's project tags as one is typed, and one is chosen with the keyboard alone.</summary>
public sealed class EntryTagSuggestionsKeyboardTests
{
    [Fact]
    public Task Typing_offers_a_project_tag_that_Down_and_Enter_put_in_the_box() =>
        HeadlessSession.On(() =>
        {
            using var shell = new RenderedShell(
                "keypaste-tag-suggestions-",
                vault => ProjectVariables.Set(vault, "billing", "prod", "PROD_KEY", "SENTINEL-PROD-VALUE-2c7a19"));
            var entries = shell.Show<EntriesViewModel>(DestinationKind.Entries);
            entries.Selected = entries.Rows.Single(row => row.Title == "github");
            RenderedShell.Drain();

            var box = shell.Named<AutoCompleteBox>("NewTag");
            box.GetVisualDescendants().OfType<TextBox>().First().Focus(NavigationMethod.Tab);
            RenderedShell.Drain();

            shell.Window.KeyTextInput("prod");
            RenderedShell.Drain();

            Assert.True(box.IsDropDownOpen);

            Press(shell, PhysicalKey.ArrowDown);
            Press(shell, PhysicalKey.Enter);

            Assert.False(box.IsDropDownOpen);
            Assert.Equal("env:billing:prod", entries.Detail!.DraftTag);
            Assert.Empty(entries.Detail.Tags);
        });

    private static void Press(RenderedShell shell, PhysicalKey key)
    {
        shell.Window.KeyPressQwerty(key, RawInputModifiers.None);
        shell.Window.KeyReleaseQwerty(key, RawInputModifiers.None);
        RenderedShell.Drain();
    }
}
