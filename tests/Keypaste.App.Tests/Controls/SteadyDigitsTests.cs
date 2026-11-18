using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Keypaste.App.Controls;
using Xunit;

namespace Keypaste.App.Tests.Controls;

/// <summary>A counting text keeps one width as it counts, and that width holds every value it shows in the face it is drawn in.</summary>
public sealed class SteadyDigitsTests
{
    [Fact]
    public Task A_countdown_keeps_one_width_that_holds_each_of_its_values() => HeadlessSession.On(() =>
    {
        var countdown = new SteadyDigits { FontSize = 12.5 };
        List<double> widths = [];

        foreach (var left in new[] { "0:30", "0:28", "0:11", "0:07", "0:00" })
        {
            countdown.Text = $"Denies in {left}";
            countdown.Measure(Size.Infinity);
            widths.Add(countdown.DesiredSize.Width);

            using var drawn = new TextLayout(countdown.Text, new Typeface(countdown.FontFamily), countdown.FontSize, null);
            Assert.True(countdown.DesiredSize.Width >= drawn.WidthIncludingTrailingWhitespace, $"{countdown.Text} is {drawn.WidthIncludingTrailingWhitespace} wide in {countdown.DesiredSize.Width}");
        }

        Assert.Single(widths.Distinct());
    });
}
