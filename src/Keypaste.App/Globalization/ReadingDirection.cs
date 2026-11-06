using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Styling;
using Keypaste.App.Controls;
using Keypaste.App.Views;

namespace Keypaste.App.Globalization;

/// <summary>Which way the app's windows run: as the language of the system's interface reads. A prompt window keeps its own, left to right.</summary>
internal static class ReadingDirection
{
    internal static FlowDirection Of(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return culture.TextInfo.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    /// <summary>
    /// What runs the app right to left when the culture reads that way: every window mirrors, and each line of text, typed or not, keeps left to right inside its mirrored place, right-aligned, since the words are English and "24 hours" under a right-to-left paragraph reads "hours 24". A prompt window and what holds machine text stay as they are in English.
    /// </summary>
    /// <remarks>Goes first among the app's styles, so a class that aligns its text still does. A translation into a right-to-left language drops the text's half of this.</remarks>
    internal static Styles RightToLeftWindows() =>
    [
        new Style(selector => selector.Is<Window>())
        {
            Setters = { new Setter(Visual.FlowDirectionProperty, FlowDirection.RightToLeft) },
        },
        new Style(selector => selector.Is<TextBlock>())
        {
            Setters =
            {
                new Setter(Visual.FlowDirectionProperty, FlowDirection.LeftToRight),
                new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right),
            },
        },
        new Style(selector => selector.Is<TextBox>())
        {
            Setters = { new Setter(TextBox.TextAlignmentProperty, TextAlignment.Right) },
        },
        new Style(selector => selector.Is<TextBox>().Template().Is<TextPresenter>())
        {
            Setters = { new Setter(Visual.FlowDirectionProperty, FlowDirection.LeftToRight) },
        },
        KeepsItsStart(selector => selector.OfType<Window>().Class(PromptWindow.Class).Descendant()),
        KeepsItsStart(selector => selector.Is<MaskedInput>().Descendant()),
        KeepsItsStart(selector => selector.Is<TextBox>().Class("mono").Template()),
        KeepsItsStart(selector => selector.Is<AutoCompleteBox>().Class("mono").Descendant()),
        new Style(selector => selector.Is<TextBox>().Class("mono"))
        {
            Setters = { new Setter(TextBox.TextAlignmentProperty, TextAlignment.Start) },
        },
    ];

    private static Styles KeepsItsStart(Func<Selector?, Selector> within) =>
    [
        new Style(selector => within(selector).Is<TextBlock>())
        {
            Setters = { new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Start) },
        },
        new Style(selector => within(selector).Is<TextBox>())
        {
            Setters = { new Setter(TextBox.TextAlignmentProperty, TextAlignment.Start) },
        },
    ];
}
