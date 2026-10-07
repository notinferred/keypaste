using Keypaste.Core.Audit;

namespace Keypaste.Core.Settings;

/// <summary>How recording the chosen vault ended.</summary>
public enum ChooseOutcome
{
    /// <summary>The vault is now the chosen one.</summary>
    Chosen,

    /// <summary>Another vault was already chosen, and only a first choice was asked for.</summary>
    AlreadyChosen,

    /// <summary><c>app.toml</c> exists and could not be read, so it was left alone rather than overwritten.</summary>
    Unreadable,

    /// <summary>The path holds a character the file's strings cannot, so it was not recorded.</summary>
    Unrecordable,

    /// <summary>The file could not be written.</summary>
    NotWritten,
}

/// <summary>
/// The vault agents and the CLI use when neither <c>--vault</c> nor <c>KEYPASTE_VAULT</c> names one,
/// kept as <c>vault</c> in <c>app.toml</c> (D-0389).
/// </summary>
public static class ChosenVault
{
    /// <summary>The vault chosen on this machine, or null when none is or the file cannot be read.</summary>
    /// <param name="home">The value of <c>KEYPASTE_HOME</c>, or null.</param>
    public static string? Read(string? home) =>
        AppSettings.Load(KeypasteHome.SettingsPath(home)).Vault;

    /// <summary>Makes a vault the chosen one, keeping every other preference in the file.</summary>
    /// <param name="home">The value of <c>KEYPASTE_HOME</c>, or null.</param>
    /// <param name="vaultPath">The vault.</param>
    /// <param name="onlyIfNone">Record it only when no vault is chosen yet: the first create or unlock.</param>
    /// <exception cref="ArgumentException"><paramref name="vaultPath"/> is null or empty.</exception>
    public static ChooseOutcome Choose(string? home, string vaultPath, bool onlyIfNone)
    {
        ArgumentException.ThrowIfNullOrEmpty(vaultPath);

        var path = KeypasteHome.SettingsPath(home);

        if (!AppSettings.TryLoad(path, out var settings))
        {
            return ChooseOutcome.Unreadable;
        }

        var full = Path.GetFullPath(vaultPath);

        if (settings.Vault is { } chosen)
        {
            if (onlyIfNone)
            {
                return ChooseOutcome.AlreadyChosen;
            }

            if (Same(chosen, full))
            {
                return ChooseOutcome.Chosen;
            }
        }

        if (!AppSettings.CanRecord(full))
        {
            return ChooseOutcome.Unrecordable;
        }

        return AppSettings.Save(path, settings with { Vault = full }) && Same(Read(home), full)
            ? ChooseOutcome.Chosen
            : ChooseOutcome.NotWritten;
    }

    /// <summary>Whether a path is the chosen vault, by this platform's rule for file names.</summary>
    public static bool Same(string? left, string? right) =>
        left is not null && right is not null
        && string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), PathIdentity.Comparison);

    internal static string? FullPath(string text)
    {
        if (text.Length == 0)
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(text);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
