using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Keypaste.App.Controls;

namespace Keypaste.App.Tests.Rendering;

/// <summary>Over a drawn frame: a field someone types in or chooses from has a visible label above it, or before it in its row.</summary>
internal static class LabelledFields
{
    private const double _labelReach = 56;
    private const double _besideReach = 16;

    /// <summary>A sentence for each field on show with no label above it.</summary>
    internal static IEnumerable<string> Problems(TopLevel window)
    {
        var shown = window.GetVisualDescendants().OfType<Control>().Where(control => control.IsEffectivelyVisible).ToList();
        var labels = shown.OfType<TextBlock>().Where(text => text.Classes.Contains("label") && !string.IsNullOrEmpty(text.Text)).ToList();

        foreach (var field in shown.Where(AsksForAValue))
        {
            if (field.TransformToVisual(window) is not { } placed)
            {
                continue;
            }

            var bounds = new Rect(field.Bounds.Size).TransformToAABB(placed);

            if (!labels.Exists(label => IsAbove(label, bounds, window) || IsBeside(label, field, bounds, window)))
            {
                yield return $"the field {Automation(field)} has no label above it or beside it";
            }
        }
    }

    // The search field names itself by its icon and its one word and a title's picker is its heading; a mapping row's field takes its column's head; the box inside an AutoCompleteBox is that box's own.
    private static bool AsksForAValue(Control control) =>
        control is TextBox or MaskedInput or AutoCompleteBox or ComboBox or NumericUpDown or CalendarDatePicker
        && !control.Classes.Contains("search")
        && !control.Classes.Contains("title")
        && control.TemplatedParent is not (AutoCompleteBox or NumericUpDown or CalendarDatePicker or ComboBox)
        && !control.GetVisualAncestors().OfType<Border>().Any(row => row.Classes.Contains("map-row"));

    private static bool IsAbove(TextBlock label, Rect field, TopLevel window)
    {
        if (label.TransformToVisual(window) is not { } placed)
        {
            return false;
        }

        var bounds = new Rect(label.Bounds.Size).TransformToAABB(placed);
        var zoom = Math.Abs(placed.M11);

        return bounds.Bottom <= field.Top + (2 * zoom)
            && bounds.Bottom >= field.Top - (_labelReach * zoom)
            && bounds.Left < field.Right
            && field.Left < bounds.Right;
    }

    // A row of grouped rows, or a pop-up row such as Save in, puts its label before its field on the same line: left of it, or right of it under right to left.
    private static bool IsBeside(TextBlock label, Control field, Rect fieldBounds, TopLevel window)
    {
        if (label.TransformToVisual(window) is not { } placed)
        {
            return false;
        }

        var bounds = new Rect(label.Bounds.Size).TransformToAABB(placed);
        var zoom = Math.Abs(placed.M11);
        var before = (bounds.Right <= fieldBounds.Left + (2 * zoom) && bounds.Right >= fieldBounds.Left - (_besideReach * zoom))
            || (bounds.Left >= fieldBounds.Right - (2 * zoom) && bounds.Left <= fieldBounds.Right + (_besideReach * zoom));
        var sameRow = label.FindAncestorOfType<FieldRow>() is { } row && ReferenceEquals(row, field.FindAncestorOfType<FieldRow>());
        var sameLine = bounds.Top < fieldBounds.Bottom && fieldBounds.Top < bounds.Bottom;

        return before && (sameRow || sameLine);
    }

    private static string Automation(Control field) =>
        Avalonia.Automation.AutomationProperties.GetName(field) ?? field.Name ?? field.GetType().Name;
}
