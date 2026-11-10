using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Keypaste.App.Controls;

/// <summary>
/// A value that copies when pressed: by pointer, Enter or Space. A copy glyph shows on hover and focus, and a check for
/// about a second after a press.
/// </summary>
/// <remarks>
/// Its <see cref="Avalonia.Controls.Button.Command"/> is the row's existing copy command, so the clipboard clears as it always
/// has. Its accessible name is fixed words the caller sets, never the value, and a secret's content is a
/// <see cref="RevealedValue"/>, which publishes nothing.
/// </remarks>
internal sealed class PressToCopy : Button
{
    internal static readonly TimeSpan CheckShownFor = TimeSpan.FromSeconds(1);

    /// <summary>The room its value has, for a secret drawn at a width a reveal cannot change.</summary>
    internal static readonly DirectProperty<PressToCopy, double> ValueWidthProperty =
        AvaloniaProperty.RegisterDirect<PressToCopy, double>(nameof(ValueWidth), control => control.ValueWidth);

    private const double _glyphAndGap = 22;

    private ITimer? _check;
    private double _valueWidth;

    public PressToCopy() => Classes.Add("press-to-copy");

    internal double ValueWidth
    {
        get => _valueWidth;
        private set => SetAndRaise(ValueWidthProperty, ref _valueWidth, value);
    }

    protected override Type StyleKeyOverride => typeof(Button);

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ValueWidth = Math.Max(0, e.NewSize.Width - Padding.Left - Padding.Right - _glyphAndGap);
    }

    protected override void OnClick()
    {
        base.OnClick();
        PseudoClasses.Set(":copied", true);
        _check?.Dispose();
        _check = GetValue(RevealedValue.ClockProperty).CreateTimer(
            _ => Dispatcher.UIThread.Post(() => PseudoClasses.Set(":copied", false)),
            null,
            CheckShownFor,
            Timeout.InfiniteTimeSpan);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _check?.Dispose();
        _check = null;
        PseudoClasses.Set(":copied", false);
        base.OnDetachedFromVisualTree(e);
    }
}
