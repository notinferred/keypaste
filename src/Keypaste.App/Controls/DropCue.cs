using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Keypaste.App.Controls;

/// <summary>
/// What a drag would land on and what it would do: a frame round the target and a caption saying so. Drawn over the
/// window and out of its hit testing, in ink rather than amber, since the view's one amber stays where it is.
/// </summary>
internal sealed class DropCue : Panel
{
    private readonly Border _frame = new()
    {
        Name = "DropFrame",
        BorderThickness = new Thickness(2),
        CornerRadius = new CornerRadius(7),
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
    };

    private readonly TextBlock _words = new() { Name = "DropCaption", TextWrapping = TextWrapping.Wrap, MaxWidth = 400 };

    public DropCue()
    {
        IsHitTestVisible = false;
        IsVisible = false;
        _frame[!Border.BorderBrushProperty] = this.GetResourceObservable("KpTextSecondary").ToBinding();

        var caption = new Border
        {
            Child = _words,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(24),
        };

        caption.Classes.Add("toast");
        Children.Add(_frame);
        Children.Add(caption);
    }

    /// <summary>Frames <paramref name="target"/> and says what dropping on it does.</summary>
    internal void Show(Control target, string caption)
    {
        ArgumentNullException.ThrowIfNull(target);

        var at = target.TranslatePoint(default, this) ?? default;
        _frame.Margin = new Thickness(at.X, at.Y, 0, 0);
        _frame.Width = target.Bounds.Width;
        _frame.Height = target.Bounds.Height;
        _words.Text = caption;
        IsVisible = true;
    }

    internal void Hide() => IsVisible = false;
}
