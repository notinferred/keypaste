using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Keypaste.App.Clipboard;

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

    /// <summary>The clipboard the press is judged by, inherited from the shell so a press shows its check only once a copy reached it.</summary>
    internal static readonly AttachedProperty<ClipboardCountdown?> ClipboardProperty =
        AvaloniaProperty.RegisterAttached<PressToCopy, Control, ClipboardCountdown?>("Clipboard", null, inherits: true);

    private const double _glyphAndGap = 22;

    private ITimer? _check;
    private double _valueWidth;

    public PressToCopy() => Classes.Add("press-to-copy");

    public static ClipboardCountdown? GetClipboard(Control control) => control.GetValue(ClipboardProperty);

    public static void SetClipboard(Control control, ClipboardCountdown? value) => control.SetValue(ClipboardProperty, value);

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
        var clipboard = GetValue(ClipboardProperty);
        var landedBefore = clipboard?.Landings;
        base.OnClick();

        if (clipboard is not null && Command is IAsyncRelayCommand { ExecutionTask: { } copying })
        {
            _ = ShowCheckOnceCopiedAsync(copying, clipboard, landedBefore);
        }
    }

    private async Task ShowCheckOnceCopiedAsync(Task copying, ClipboardCountdown clipboard, int? landedBefore)
    {
        await Task.WhenAny(copying).ConfigureAwait(true);

        if (!copying.IsCompletedSuccessfully || clipboard.Landings == landedBefore)
        {
            return;
        }

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
