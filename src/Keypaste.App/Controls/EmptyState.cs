using Avalonia;
using Avalonia.Controls;

namespace Keypaste.App.Controls;

/// <summary>
/// What a list or page shows while it has nothing: an icon, one line saying so, an optional helper line, and the next step as its content, one <c>sm</c> button.
/// </summary>
/// <remarks>It centres itself in the room the list leaves, so every empty place reads the same.</remarks>
internal sealed class EmptyState : ContentControl
{
    internal static readonly StyledProperty<string?> IconProperty =
        AvaloniaProperty.Register<EmptyState, string?>(nameof(Icon));

    internal static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<EmptyState, string?>(nameof(Text));

    internal static readonly StyledProperty<string?> HelperProperty =
        AvaloniaProperty.Register<EmptyState, string?>(nameof(Helper));

    /// <summary>What the helper line sets in monospace beyond what has a machine's shape, as <see cref="MachineRuns.WordsProperty"/> takes it.</summary>
    internal static readonly StyledProperty<string?> HelperWordsProperty =
        AvaloniaProperty.Register<EmptyState, string?>(nameof(HelperWords));

    internal string? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    internal string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    internal string? Helper
    {
        get => GetValue(HelperProperty);
        set => SetValue(HelperProperty, value);
    }

    internal string? HelperWords
    {
        get => GetValue(HelperWordsProperty);
        set => SetValue(HelperWordsProperty, value);
    }
}
