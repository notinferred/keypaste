using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Keypaste.App.Tests.Controls;

/// <summary>A primary that waits keeps its fill and takes no press, while one that cannot act yet is greyed.</summary>
public sealed class ButtonStatesTests
{
    private static (IBrush? Fill, bool TakesPresses) Drawn(params string[] classes)
    {
        var button = new Button { Content = "Create link", IsEnabled = false };
        button.Classes.AddRange(classes);
        var window = new Window { Width = 300, Height = 100, Content = button };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var drawn = (button.GetVisualDescendants().OfType<ContentPresenter>().Single(part => part.Name == "PART_ContentPresenter").Background, button.IsHitTestVisible);
        window.Close();
        return drawn;
    }

    private static Color ColorOf(string key)
    {
        var app = Application.Current!;
        return ((ISolidColorBrush)app.FindResource(app.ActualThemeVariant, key)!).Color;
    }

    [Fact]
    public Task A_busy_primary_keeps_the_accent_and_takes_no_press() => HeadlessSession.On(() =>
    {
        var (fill, takesPresses) = Drawn("primary", "busy");

        Assert.Equal(ColorOf("AccentButtonBackground"), ((ISolidColorBrush)fill!).Color);
        Assert.False(takesPresses);
    });

    [Fact]
    public Task A_disabled_primary_is_grey_not_a_faded_accent() => HeadlessSession.On(() =>
        Assert.Equal(ColorOf("AccentButtonBackgroundDisabled"), ((ISolidColorBrush)Drawn("primary").Fill!).Color));
}
