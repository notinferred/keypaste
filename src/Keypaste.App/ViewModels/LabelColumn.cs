namespace Keypaste.App.ViewModels;

/// <summary>The item pane's label column, as wide as it was last dragged, kept while the app runs.</summary>
internal sealed partial class LabelColumn : ObservableObject
{
    internal const double Default = 130;

    [ObservableProperty]
    internal partial double Width { get; set; } = Default;

    partial void OnWidthChanged(double value)
    {
        var held = Math.Clamp(value, Controls.FieldRow.LeastLabelWidth, Controls.FieldRow.MostLabelWidth);

        if (held != value)
        {
            Width = held;
        }
    }
}
