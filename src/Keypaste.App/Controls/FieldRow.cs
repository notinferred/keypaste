using Avalonia;
using Avalonia.Controls;

namespace Keypaste.App.Controls;

/// <summary>
/// One row of a <see cref="GroupedRows"/>: a label on the left, the value after it, and quiet actions on the right.
/// </summary>
/// <remarks>
/// <c>Classes="stacked"</c> puts the label above the value, as a field a person types in keeps it (DESIGN Interaction rule 5).
/// A custom field's name takes <c>Classes="mono-label"</c>. The value is the row's content.
/// </remarks>
internal sealed class FieldRow : ContentControl
{
    /// <summary>What the row holds, in words: a label, never a value.</summary>
    internal static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<FieldRow, string?>(nameof(Label));

    /// <summary>The row's quiet actions, such as Reveal.</summary>
    internal static readonly StyledProperty<object?> ActionsProperty =
        AvaloniaProperty.Register<FieldRow, object?>(nameof(Actions));

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);

        base.OnPropertyChanged(change);

        if (change.Property == GroupedRows.FollowsARowProperty)
        {
            PseudoClasses.Set(":follows", change.GetNewValue<bool>());
        }
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
