using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Keypaste.App.Views;

/// <summary>The edge between a column and the next: dragged, or moved with the arrow keys while it has focus. It lies over its head's end, so a press here never sorts.</summary>
internal sealed class ColumnGrip : Border
{
    public static readonly StyledProperty<string> ColumnProperty = AvaloniaProperty.Register<ColumnGrip, string>(nameof(Column), string.Empty);

    private const double _target = 20;
    private const double _lineHeight = 12;
    private const double _keyStep = 8;
    private const double _insideColumn = 6;
    private const string _keysHelp = "The left and right arrow keys change the width.";

    private readonly Border _line = new() { Width = 1, Height = _lineHeight, HorizontalAlignment = HorizontalAlignment.Right };
    private Point? _pressedAt;
    private IReadOnlyList<double> _drawnAtPress = [];

    public ColumnGrip()
    {
        Focusable = true;
        Width = _target;
        Height = _target;
        HorizontalAlignment = HorizontalAlignment.Right;
        Background = Brushes.Transparent;
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
        AutomationProperties.SetHelpText(this, _keysHelp);

        _line.Classes.Add("column-grip-line");
        Child = _line;
    }

    public string Column
    {
        get => GetValue(ColumnProperty);
        set => SetValue(ColumnProperty, value);
    }

    private TableColumns? Columns => Parent as TableColumns;

    /// <summary>How far an edge lies over the end of its head: in the middle of the gap between two columns, or with none, inside its own column so its line and focus ring stay clear of the next head's text.</summary>
    /// <param name="gap">The spacing between the table's columns.</param>
    internal static double Over(double gap) => gap > 0 ? _target - gap / 2 : _target + _insideColumn;

    private (double Now, double Least, double Most) Reach =>
        TableColumns.GetTable(this) is { } table && Columns is { } columns ? table.Reach(Column, columns.DrawnWidths) : default;

    protected override AutomationPeer OnCreateAutomationPeer() => new WidthPeer(this);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        Margin = new Thickness(0, 0, Over(Columns?.ColumnSpacing ?? 0) - _target, 0);

        if (TableColumns.GetTable(this)?.Columns.FirstOrDefault(column => column.Key == Column) is { } named)
        {
            var name = $"Resize the {named.Name} column";
            AutomationProperties.SetName(this, name);
            ToolTip.SetTip(this, $"{name}. {_keysHelp}");
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (Columns is { } columns && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _pressedAt = e.GetPosition(columns);
            _drawnAtPress = columns.DrawnWidths;
            e.Pointer.Capture(this);
            Focus(NavigationMethod.Pointer);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_pressedAt is { } from && Columns is { } columns)
        {
            TableColumns.GetTable(this)?.Resize(Column, e.GetPosition(columns).X - from.X, _drawnAtPress);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_pressedAt is not null)
        {
            _pressedAt = null;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _pressedAt = null;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Left or Key.Right && Columns is { } columns)
        {
            TableColumns.GetTable(this)?.Resize(Column, e.Key == (FlowDirection == FlowDirection.RightToLeft ? Key.Right : Key.Left) ? -_keyStep : _keyStep, columns.DrawnWidths);
            e.Handled = true;
        }
    }

    /// <summary>What a screen reader gets: the edge's name and help, and its column's width as a value it can read, change and hear change.</summary>
    private sealed class WidthPeer : ControlAutomationPeer, IRangeValueProvider
    {
        private readonly ColumnGrip _grip;
        private double _reported;

        public WidthPeer(ColumnGrip grip)
            : base(grip)
        {
            _grip = grip;
            _reported = grip.Reach.Now;
            grip.LayoutUpdated += (_, _) => ReportWidth();
        }

        public bool IsReadOnly => false;

        public double Minimum => _grip.Reach.Least;

        public double Maximum => _grip.Reach.Most;

        public double Value => _grip.Reach.Now;

        public double SmallChange => _keyStep;

        public double LargeChange => _keyStep;

        public void SetValue(double value)
        {
            if (TableColumns.GetTable(_grip) is { } table && _grip.Columns is { } columns)
            {
                table.Resize(_grip.Column, value - Value, columns.DrawnWidths);
            }
        }

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;

        private void ReportWidth()
        {
            var now = Value;

            if (now != _reported)
            {
                RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, _reported, now);
                _reported = now;
            }
        }
    }
}
