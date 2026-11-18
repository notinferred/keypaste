using Avalonia;
using Avalonia.Controls;

namespace Keypaste.App.Controls;

/// <summary>
/// One row of a <see cref="GroupedRows"/>: a label on the left, the value after it, and quiet actions on the right.
/// </summary>
/// <remarks>
/// <c>Classes="stacked"</c> puts the label above the value, as a field a person types in keeps it.
/// A custom field's name takes <c>Classes="mono-label"</c>. The value is the row's content; a row without one gives its label the value's room.
/// <c>Classes="adaptive"</c> keeps the label at the left and puts it above while the row is under <see cref="NarrowBelow"/> wide.
/// </remarks>
internal sealed class FieldRow : ContentControl
{
    /// <summary>The width under which an adaptive row's label goes above its field: the label's column, a field's least width and its actions.</summary>
    internal const double NarrowBelow = 380;

    /// <summary>What the row holds, in words: a label, never a value.</summary>
    internal static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<FieldRow, string?>(nameof(Label));

    /// <summary>The row's quiet actions, such as Reveal.</summary>
    internal static readonly StyledProperty<object?> ActionsProperty =
        AvaloniaProperty.Register<FieldRow, object?>(nameof(Actions));

    public FieldRow() => PseudoClasses.Set(":no-value", true);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);

        base.OnPropertyChanged(change);

        if (change.Property == GroupedRows.FollowsARowProperty)
        {
            PseudoClasses.Set(":follows", change.GetNewValue<bool>());
        }
        else if (change.Property == ContentProperty)
        {
            PseudoClasses.Set(":no-value", change.NewValue is null);
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        base.OnSizeChanged(e);
        PseudoClasses.Set(":narrow", e.NewSize.Width < NarrowBelow);
    }

    internal string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    internal object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }
}
