using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Keypaste.App.Views;

/// <summary>
/// Turns a group's depth into a left margin.
/// </summary>
/// <remarks>
/// A converter rather than a <c>Thickness</c> on <see cref="ViewModels.GroupNode"/>, because that
/// would put an Avalonia type in a view model — and every view model in this app is testable
/// without an application precisely because none of them name one.
/// </remarks>
internal sealed class DepthToIndent : IValueConverter
{
    /// <summary>How far one level of nesting moves a row.</summary>
    internal const double Step = 14d;

    /// <summary>The only one there needs to be.</summary>
    internal static DepthToIndent Instance { get; } = new();

    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new Thickness(value is int depth ? Math.Max(0, depth) * Step : 0d, 0, 0, 0);

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("An indent is derived from a depth, never the other way round.");
}
