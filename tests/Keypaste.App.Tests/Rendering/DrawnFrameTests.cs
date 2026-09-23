using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Keypaste.App.Controls;
using Keypaste.App.ViewModels;
using Xunit;

namespace Keypaste.App.Tests.Rendering;

/// <summary>
/// The frame reader, proved against frames Skia drew before any claim about the app rests on it.
/// </summary>
/// <remarks>
/// Every negative the app's tests make ("the value is not on screen") is only as good as the reader's
/// ability to find a value that is. So each negative here sits beside the positive it would have
/// had to miss, in the same cell and the same frame size.
/// </remarks>
public sealed class DrawnFrameTests
{
    private const string _first = "SENTINEL-4d1e";
    private const string _second = "PQWXYZ_gkmr07";

    [Fact]
    public Task A_held_value_is_in_the_frame_and_absent_before_and_after() => HeadlessSession.On(() =>
    {
        using var cell = new HeldCell(_first);
        var dots = new string('•', _first.Length);

        var atRest = cell.Frame();
        Report("at rest", atRest, cell, _first, dots);
        Assert.True(atRest.Shows(dots, cell.Style, atRest.Of(cell.Cell, cell.Window)));
        Assert.False(atRest.Shows(_first, cell.Style, atRest.Everywhere));

        cell.Press();
        var held = cell.Frame();
        Report("held", held, cell, _first, dots);
        Assert.True(held.Shows(_first, cell.Style, held.Of(cell.Cell, cell.Window)));
        Assert.True(held.Shows(_first, cell.Style, held.Everywhere));
        Assert.False(held.Shows(dots, cell.Style, held.Everywhere));

        cell.Release();
        var released = cell.Frame();
        Assert.False(released.Shows(_first, cell.Style, released.Everywhere));
        Assert.True(released.Shows(dots, cell.Style, released.Of(cell.Cell, cell.Window)));
    });

    [Fact]
    public Task Two_values_of_one_length_sharing_no_character_are_told_apart() => HeadlessSession.On(() =>
    {
        Assert.Equal(_first.Length, _second.Length);
        Assert.Empty(_first.Intersect(_second));

        foreach (var (shown, other) in new[] { (_first, _second), (_second, _first) })
        {
            using var cell = new HeldCell(shown);
            cell.Press();
            var frame = cell.Frame();
            Report($"holding {shown}", frame, cell, shown, other);

            Assert.True(frame.Shows(shown, cell.Style, frame.Everywhere));
            Assert.False(frame.Shows(other, cell.Style, frame.Everywhere));
        }
    });

    /// <summary>The press is a click the platform hit-tested, which needs a drawn scene.</summary>
    [Fact]
    public Task A_click_on_the_window_reaches_the_drawn_cell() => HeadlessSession.On(() =>
    {
        using var cell = new HeldCell(_first);

        cell.Press();

        Assert.True(cell.Cell.IsRevealed);
    });

    private static void Report(string when, DrawnFrame frame, HeldCell cell, string value, string other) =>
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{when}: '{value}' scores {frame.Score(value, cell.Style, frame.Everywhere):F3}, " +
            $"'{other}' scores {frame.Score(other, cell.Style, frame.Everywhere):F3}");

    /// <summary>A lone <see cref="RevealedValue"/> in the typeface the app draws values in.</summary>
    private sealed class HeldCell : IDisposable
    {
        internal HeldCell(string value)
        {
            Cell = new RevealedValue
            {
                Source = new Source(value),
                MaskedLength = value.Length,
                FontFamily = (FontFamily)Application.Current!.FindResource("KpMono")!,
                FontSize = 13,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(24),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            };

            Window = new Window { Width = 480, Height = 160, Content = Cell };
            Window.Show();
            Dispatcher.UIThread.RunJobs();
        }

        internal RevealedValue Cell { get; }

        internal Window Window { get; }

        internal TextStyle Style => TextStyle.Of(Cell);

        internal DrawnFrame Frame() => DrawnFrame.Capture(Window);

        internal void Press() => WindowInput.Press(Window, Cell);

        internal void Release() => WindowInput.Release(Window, Cell);

        public void Dispose() => Window.Close();
    }

    private sealed class Source(string value) : IRevealSource
    {
        public int MaskedLength => value.Length;

        public string? Reveal() => value;

        public void Conceal()
        {
        }
    }
}
