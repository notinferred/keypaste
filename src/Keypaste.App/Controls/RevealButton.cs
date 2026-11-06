using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Keypaste.App.Resources;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Controls;

/// <summary>
/// The toggle beside a masked value: pressing it, by pointer, Space, Enter or a screen reader's activation,
/// shows the value in <see cref="Target"/>, and pressing it again hides it.
/// </summary>
/// <remarks>
/// It says which it will do, in its icon, its word, its tooltip and its accessible name, and names the thing
/// from <see cref="Subject"/>. It asks the cell to reveal and never sees the value, so nothing it says can
/// hold one (T-22).
/// </remarks>
internal sealed class RevealButton : Button
{
    /// <summary>The cell it shows.</summary>
    internal static readonly StyledProperty<RevealedValue?> TargetProperty =
        AvaloniaProperty.Register<RevealButton, RevealedValue?>(nameof(Target));

    /// <summary>The row whose drawn cell it shows, for a button outside that cell's template; <see cref="Target"/> wins when both are set.</summary>
    internal static readonly StyledProperty<IRevealSource?> TargetSourceProperty =
        AvaloniaProperty.Register<RevealButton, IRevealSource?>(nameof(TargetSource));

    /// <summary>What it reveals, in words that come after "Reveal" or "Hide": a name, never a value.</summary>
    internal static readonly StyledProperty<string?> SubjectProperty =
        AvaloniaProperty.Register<RevealButton, string?>(nameof(Subject));

    /// <summary>Whether its word shows beside its icon; an icon alone leaves it to the tooltip.</summary>
    internal static readonly StyledProperty<bool> ShowsWordProperty =
        AvaloniaProperty.Register<RevealButton, bool>(nameof(ShowsWord));

    private readonly KpIcon _icon = new() { Size = 13 };

    // As wide as "Reveal", so its neighbours stay where they are when it says "Hide".
    private readonly TextBlock _word = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 38 };

    public RevealButton()
    {
        Content = _icon;
        Say();
    }

    internal RevealedValue? Target
    {
        get => GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    internal IRevealSource? TargetSource
    {
        get => GetValue(TargetSourceProperty);
        set => SetValue(TargetSourceProperty, value);
    }

    internal string? Subject
    {
        get => GetValue(SubjectProperty);
        set => SetValue(SubjectProperty, value);
    }

    internal bool ShowsWord
    {
        get => GetValue(ShowsWordProperty);
        set => SetValue(ShowsWordProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(Button);

    private bool IsShowing =>
        RevealedValue.Shown is { } shown && (Target is { } target ? ReferenceEquals(shown, target) : ReferenceEquals(shown.Source, TargetSource));

    protected override void OnClick()
    {
        base.OnClick();
        (Target ?? CellAsking(TargetSource))?.ToggleReveal();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RevealedValue.ShownChanged += Say;
        Say();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        RevealedValue.ShownChanged -= Say;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);

        base.OnPropertyChanged(change);

        if (change.Property == ShowsWordProperty)
        {
            _icon.Size = ShowsWord ? 14 : 13;
            Content = null;
            Content = ShowsWord
                ? new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _icon, _word } }
                : _icon;
        }
        else if (change.Property == TargetProperty || change.Property == TargetSourceProperty || change.Property == SubjectProperty)
        {
            Say();
        }
    }

    private void Say()
    {
        var showing = IsShowing;
        var word = showing ? Strings.Hide : Strings.Reveal;

        _icon.Icon = showing ? "eye-off" : "eye";
        _word.Text = word;
        AutomationProperties.SetName(this, Subject is { Length: > 0 } subject ? $"{word} {subject}" : word);
        ToolTip.SetTip(this, showing ? Strings.Hide : UseText.RevealFor(RevealedValue.ShownFor));
        (Parent as Control)?.Classes.Set("shows-value", showing);
    }

    /// <summary>The nearest drawn cell asking <paramref name="source"/>, looked for from the button's closest ancestor outward.</summary>
    private RevealedValue? CellAsking(IRevealSource? source)
    {
        if (source is null)
        {
            return null;
        }

        foreach (var ancestor in this.GetVisualAncestors())
        {
            if (ancestor.GetVisualDescendants().OfType<RevealedValue>().FirstOrDefault(cell => ReferenceEquals(cell.Source, source)) is { } cell)
            {
                return cell;
            }
        }

        return null;
    }
}
