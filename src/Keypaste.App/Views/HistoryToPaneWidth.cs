using System.Globalization;
using Avalonia.Data.Converters;

namespace Keypaste.App.Views;

/// <summary>
/// Turns "the history section is open" into how wide the detail pane is.
/// </summary>
/// <remarks>
/// <para>
/// A revision's fields sit beside the current ones, so the pane needs room the browsing layout does
/// not give it. Rather than a permanently wider pane, the screen trades: while a comparison is on
/// screen the group tree steps aside and the pane takes its width. The pane's column is
/// <c>Auto</c>, so widening the content is what moves the column.
/// </para>
/// <para>
/// A converter rather than a property, for <see cref="DepthToIndent"/>'s reason — a width the view
/// decides is not something a view model should name.
/// </para>
/// </remarks>
internal sealed class HistoryToPaneWidth : IValueConverter
{
    /// <summary>Wide enough for one entry's fields.</summary>
    internal const double Browsing = 320d;

    /// <summary>Wide enough for two sets of them, side by side.</summary>
    internal const double Comparing = 520d;

    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Comparing : Browsing;

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("A width is derived from the screen's state, never the other way round.");
}
