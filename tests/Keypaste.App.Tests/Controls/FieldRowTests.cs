using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Keypaste.App.Controls;
using Xunit;

namespace Keypaste.App.Tests.Controls;

/// <summary>A row's label names itself in a tooltip only while it is trimmed, so pointing at a whole label shows nothing new.</summary>
public sealed class FieldRowTests
{
    private static object? TipOf(string label)
    {
        var row = new FieldRow { Label = label, Content = new TextBlock { Text = "value" } };
        var window = new Window { Width = 400, Height = 100, Content = row };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var tip = ToolTip.GetTip(row.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Name == "Label"));
        window.Close();
        return tip;
    }

    [Fact]
    public Task A_whole_label_has_no_tooltip() => HeadlessSession.On(() => Assert.Null(TipOf("Username")));

    [Fact]
    public Task A_trimmed_label_names_itself_in_its_tooltip()
    {
        const string label = "A label far longer than the label column has room for";
        return HeadlessSession.On(() => Assert.Equal(label, TipOf(label)));
    }
}
