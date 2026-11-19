using Keypaste.App.Navigation;
using Keypaste.App.Resources;

namespace Keypaste.App.ViewModels;

/// <summary>One part of the Settings screen, whose words are read each time for the language in force.</summary>
/// <param name="name">The section list's words for it.</param>
/// <param name="screen">The screen that shows it: Settings itself, or a screen of its own drawn in the same column.</param>
internal sealed class SettingsSectionChoice(Func<string> name, DestinationKind screen)
{
    public static SettingsSectionChoice General { get; } = new(() => Strings.General, DestinationKind.Settings);

    public static SettingsSectionChoice ThisVault { get; } = new(() => Strings.ThisVault, DestinationKind.Settings);

    public static SettingsSectionChoice Browser { get; } = new(() => Strings.Browser, DestinationKind.Settings);

    public static SettingsSectionChoice Agents { get; } = new(() => Strings.Agents, DestinationKind.Settings);

    public static SettingsSectionChoice ShareLinks { get; } = new(() => Strings.ShareLinks, DestinationKind.Sharing);

    public static SettingsSectionChoice ScopedTokens { get; } = new(() => Strings.ScopedTokens, DestinationKind.Tokens);

    public static SettingsSectionChoice Diagnostics { get; } = new(() => Strings.Diagnostics, DestinationKind.Diagnostics);

    /// <summary>Every section, in the order the list reads.</summary>
    public static IReadOnlyList<SettingsSectionChoice> All { get; } =
        [General, ThisVault, Browser, Agents, ShareLinks, ScopedTokens, Diagnostics];

    internal DestinationKind Screen => screen;

    public override string ToString() => name();
}
