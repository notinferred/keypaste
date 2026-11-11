using System.Globalization;
using Avalonia.Data.Converters;
using Keypaste.App.ViewModels;

namespace Keypaste.App.Views;

/// <summary>The one generator open anywhere, from the places that can open one, for the window's single generator sheet.</summary>
internal sealed class FirstOpen : IMultiValueConverter
{
    public static FirstOpen Generator { get; } = new();

    /// <inheritdoc/>
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values.OfType<GeneratePasswordViewModel>().FirstOrDefault();
}
