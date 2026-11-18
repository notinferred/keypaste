using Keypaste.Core.Audit;

namespace Keypaste.Core.Settings;

/// <summary>
/// The one account there is before cloud accounts: the vaults the person opened on this computer, kept as
/// <see cref="AppSettings.AccountVaults"/> in <c>app.toml</c>.
/// </summary>
/// <remarks>
/// Holding a vault changes nothing an agent reaches: <see cref="ChosenVault"/> stays the vault a bridge started without
/// <c>--vault</c> asks for.
/// </remarks>
public static class LocalAccount
{
    /// <summary>The most vaults the account holds; a section each, within the sections <c>app.toml</c> may have.</summary>
    public const int MaximumVaults = 16;

    /// <summary>The account's name, as the app shows it.</summary>
    public static string Name => OperatingSystem.IsMacOS() ? "On this Mac" : "On this PC";

    /// <summary>The vaults the account holds, or none when the file cannot be read.</summary>
    /// <param name="home">The value of <c>KEYPASTE_HOME</c>, or null.</param>
    public static IReadOnlyList<string> Vaults(string? home) =>
        AppSettings.Load(KeypasteHome.SettingsPath(home)).AccountVaults;

    /// <summary>Adds a vault to the account, after those it holds, keeping every other preference in the file.</summary>
    /// <param name="home">The value of <c>KEYPASTE_HOME</c>, or null.</param>
    /// <param name="vaultPath">The vault.</param>
    /// <returns>Whether the account holds it now; false when the file cannot be read or written, the path cannot be recorded or the account is full.</returns>
    /// <exception cref="ArgumentException"><paramref name="vaultPath"/> is null or empty.</exception>
    public static bool Hold(string? home, string vaultPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(vaultPath);

        var path = KeypasteHome.SettingsPath(home);

        if (!AppSettings.TryLoad(path, out var settings))
        {
            return false;
        }

        var full = Path.GetFullPath(vaultPath);

        if (settings.AccountVaults.Any(held => ChosenVault.Same(held, full)))
        {
            return true;
        }

        if (settings.AccountVaults.Count >= MaximumVaults || !AppSettings.CanRecord(full))
        {
            return false;
        }

        return AppSettings.Save(path, settings with { AccountVaults = [.. settings.AccountVaults, full] })
            && Vaults(home).Any(held => ChosenVault.Same(held, full));
    }

    /// <summary>Removes a vault from the account, keeping every other preference in the file; the chosen vault stays as it is.</summary>
    /// <param name="home">The value of <c>KEYPASTE_HOME</c>, or null.</param>
    /// <param name="vaultPath">The vault.</param>
    /// <returns>Whether the account no longer holds it.</returns>
    /// <exception cref="ArgumentException"><paramref name="vaultPath"/> is null or empty.</exception>
    public static bool Release(string? home, string vaultPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(vaultPath);

        var path = KeypasteHome.SettingsPath(home);

        if (!AppSettings.TryLoad(path, out var settings))
        {
            return false;
        }

        if (!settings.AccountVaults.Any(held => ChosenVault.Same(held, vaultPath)))
        {
            return true;
        }

        return AppSettings.Save(path, settings with { AccountVaults = [.. settings.AccountVaults.Where(held => !ChosenVault.Same(held, vaultPath))] });
    }
}
