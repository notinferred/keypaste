using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Keypaste.App.Controls;

/// <summary>
/// One row of a <see cref="GroupedRows"/>: a label on the left, the value after it, and quiet actions on the right.
/// </summary>
/// <remarks>
/// <c>Classes="stacked"</c> puts the label above the value, as a field a person types in keeps it.
/// A custom field's name takes <c>Classes="mono-label"</c>. The value is the row's content; a row without one gives its label the value's room.
/// <c>Classes="adaptive"</c> keeps the label at the left and puts it above while the row is under <see cref="NarrowBelow"/> wide.
/// The label's tooltip names it only while the label is trimmed.
/// Where an ancestor sets <see cref="LabelWidthProperty"/>, the rows under it share that label column and dragging the edge after a label resizes it.
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

    /// <summary>The label column's width the rows under an ancestor share, or NaN for the fixed one, which no drag changes.</summary>
    public static readonly AttachedProperty<double> LabelWidthProperty =
        AvaloniaProperty.RegisterAttached<FieldRow, Control, double>("LabelWidth", double.NaN, inherits: true, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    internal const double LeastLabelWidth = 80;
    internal const double MostLabelWidth = 260;
    private const double _fixedLabelWidth = 130;

    private TextBlock? _label;
    private Grid? _columns;
    private Control? _grip;
    private (double X, double Width)? _dragFrom;

    public FieldRow() => PseudoClasses.Set(":no-value", true);

    public static double GetLabelWidth(Control control) => control.GetValue(LabelWidthProperty);

    public static void SetLabelWidth(Control control, double value) => control.SetValue(LabelWidthProperty, value);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        base.OnApplyTemplate(e);
        _label = e.NameScope.Find<TextBlock>("Label");
        _columns = e.NameScope.Find<Grid>("Columns");

        if (_grip is not null)
        {
            _grip.PointerPressed -= OnGripPressed;
            _grip.PointerMoved -= OnGripMoved;
            _grip.PointerReleased -= OnGripReleased;
        }

        _grip = e.NameScope.Find<Control>("LabelGrip");

        if (_grip is not null)
        {
            _grip.PointerPressed += OnGripPressed;
            _grip.PointerMoved += OnGripMoved;
            _grip.PointerReleased += OnGripReleased;
        }

        ShowLabelWidth();
    }

    private void ShowLabelWidth()
    {
        var width = GetValue(LabelWidthProperty);

        if (_columns is { ColumnDefinitions.Count: > 0 } columns)
        {
            columns.ColumnDefinitions[0].Width = new GridLength(double.IsNaN(width) ? _fixedLabelWidth : width);
        }

        PseudoClasses.Set(":resizable", !double.IsNaN(width));
    }

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !double.IsNaN(GetValue(LabelWidthProperty)))
        {
            _dragFrom = (e.GetPosition(this).X, GetValue(LabelWidthProperty));
            e.Pointer.Capture(_grip);
            e.Handled = true;
        }
    }

    private void OnGripMoved(object? sender, PointerEventArgs e)
    {
        if (_dragFrom is not { } from)
        {
            return;
        }

        var width = Math.Clamp(from.Width + e.GetPosition(this).X - from.X, LeastLabelWidth, MostLabelWidth);
        var owner = this.GetVisualAncestors().OfType<Control>().FirstOrDefault(control => control.IsSet(LabelWidthProperty)) ?? this;
        owner.SetCurrentValue(LabelWidthProperty, width);
        e.Handled = true;
    }

    private void OnGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragFrom is not null)
        {
            _dragFrom = null;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);

        if (_label is not null)
        {
            var trimmed = _label.TextLayout.TextLines.Any(line => line.HasCollapsed);
            ToolTip.SetTip(_label, trimmed ? Label : null);
        }

        return arranged;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);

        base.OnPropertyChanged(change);

        if (change.Property == LabelWidthProperty)
        {
            ShowLabelWidth();
        }
        else if (change.Property == GroupedRows.FollowsARowProperty)
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
