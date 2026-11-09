using Keypaste.App.Resources;

namespace Keypaste.App.ViewModels;

/// <summary>One part of the Settings screen, whose words are read each time for the language in force.</summary>
internal sealed class SettingsSectionChoice(Func<string> name, Func<string> note)
{
    internal static SettingsSectionChoice General { get; } = new(() => Strings.General, () => Strings.SettingsGeneralNote);

    internal static SettingsSectionChoice ThisVault { get; } = new(() => Strings.ThisVault, () => Strings.SettingsThisVaultNote);

    internal static SettingsSectionChoice Browser { get; } = new(() => Strings.Browser, () => Strings.SettingsBrowserNote);

    /// <summary>What its settings are, under the screen's segmented control.</summary>
    internal string Note => note();

    public override string ToString() => name();
}
