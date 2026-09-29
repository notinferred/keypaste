using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Styling;
using Keypaste.App.Controls;
using Xunit;

namespace Keypaste.App.Tests.Rendering;

/// <summary>
/// The amber detector, held to frames whose count is known, so that a journey passing it means what
/// it says (D-0375). Each case is drawn by Skia with the app's own theme.
/// </summary>
public sealed class AmberElementsTests
{
    [Fact]
    public Task One_primary_button_is_one_element_and_two_are_two() => HeadlessSession.On(() =>
    {
        Assert.Single(Count(Row(Primary("Save"))));
        Assert.Equal(2, Count(Row(Primary("Save"), Primary("Send"))).Count);
    });

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task Amber_text_is_one_element_in_either_theme(string theme) => HeadlessSession.On(() =>
    {
        var text = new TextBlock { Text = "42m left", FontSize = 11 };
        text.Classes.Add("mono");
        text.Classes.Add("amber");

        Assert.Single(Count(Row(text), theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark));
    });

    [Fact]
    public Task A_tint_a_selected_row_and_a_focused_field_are_none() => HeadlessSession.On(() =>
    {
        var tint = new Border { Width = 120, Height = 40 };
        tint.Bind(Border.BackgroundProperty, tint.GetResourceObservable("KpAccentTint"));
        Assert.Empty(Count(Row(tint)));

        var list = new ListBox { Width = 200, ItemsSource = new[] { "github", "gmail" }, SelectedIndex = 0 };
        Assert.Empty(Count(Row(list)));

        var field = new TextBox { Width = 200, Text = "query" };
        Assert.Empty(Count(Row(field), focus: field));
    });

    [Fact]
    public Task A_waiting_dot_is_one_element() => HeadlessSession.On(() =>
    {
        var dot = new Border();
        dot.Classes.Add("dot");
        dot.Classes.Add("waiting");

        Assert.Single(Count(Row(dot)));
    });

    [Fact]
    public Task The_mark_is_not_counted_beside_a_primary() => HeadlessSession.On(() =>
    {
        Assert.Single(Count(Row(new BrandMark { Width = 32, Height = 32 }, Primary("Unlock"))));
    });

    [Fact]
    public Task Only_the_dialog_counts_over_a_backdrop() => HeadlessSession.On(() =>
    {
        var dialog = new Border { Child = Primary("Import"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        dialog.Classes.Add("dialog");
        var backdrop = new Border { Child = dialog };
        backdrop.Classes.Add("backdrop");
        var behind = Row(Primary("New"), Primary("Save"));

        Assert.Single(Count(new Panel { Children = { behind, backdrop } }));
    });

    private static Button Primary(string text)
    {
        var button = new Button { Content = text };
        button.Classes.Add("primary");
        return button;
    }

    private static StackPanel Row(params Control[] controls)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 40, Margin = new Thickness(24), VerticalAlignment = VerticalAlignment.Top };
        row.Children.AddRange(controls);
        return row;
    }

    private static IReadOnlyList<PixelRect> Count(Control content, ThemeVariant? theme = null, Control? focus = null)
    {
        var window = new Window { Width = 480, Height = 240, Content = content, RequestedThemeVariant = theme ?? ThemeVariant.Dark };
        window.Bind(Window.BackgroundProperty, window.GetResourceObservable("KpBgApp"));
        window.Show();

        try
        {
            focus?.Focus();
            return AmberElements.In(window);
        }
        finally
        {
            window.Close();
        }
    }
}
