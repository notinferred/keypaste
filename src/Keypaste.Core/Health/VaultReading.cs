using Keypaste.Core.Audit;
using Keypaste.Core.Settings;

namespace Keypaste.Core.Health;

/// <summary>What named the vault keypaste would use.</summary>
public enum VaultSource
{
    /// <summary>Nothing did.</summary>
    None,

    /// <summary><c>--vault</c>.</summary>
    Flag,

    /// <summary><c>KEYPASTE_VAULT</c>.</summary>
    Variable,

    /// <summary>The choice in <c>app.toml</c>.</summary>
    Choice,
}

/// <summary>What reading the vault's first bytes found.</summary>
public enum VaultState
{
    /// <summary>It is a KeePass database.</summary>
    Ok,

    /// <summary>No vault is named or chosen.</summary>
    None,

    /// <summary>No vault is named, and <c>app.toml</c>, which would hold the choice, could not be read.</summary>
    AppTomlUnreadable,

    /// <summary>There is no file at the vault's path.</summary>
    Missing,

    /// <summary>The file does not start as a KeePass database does.</summary>
    NotKeePass,

    /// <summary>The file could not be read.</summary>
    Unreadable,
}

/// <summary>The vault keypaste would use, found as every command finds it, and whether it is a KeePass database.</summary>
/// <param name="State">What reading it found.</param>
/// <param name="Source">What named it.</param>
/// <param name="Path">Its full path, or null when none is named or chosen.</param>
/// <param name="Findings">What doctor says of it.</param>
/// <remarks>Only the twelve bytes before anything a password protects are read.</remarks>
public sealed record VaultReading(VaultState State, VaultSource Source, string? Path, IReadOnlyList<HealthFinding> Findings)
{
    private const string _subject = "vault";

    /// <summary>Finds the vault from <c>--vault</c>, then <c>KEYPASTE_VAULT</c>, then the choice in <c>app.toml</c>, and reads its header.</summary>
    /// <param name="scope">What to look at.</param>
    /// <returns>The vault and what doctor says of it.</returns>
    public static VaultReading Read(HealthScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var settingsPath = KeypasteHome.SettingsPath(scope.Home);
        var settingsRead = AppSettings.TryLoad(settingsPath, out var settings, out var problem);
        var named = scope.Environment(VaultLocation.EnvironmentVariable);
        var chosen = settings.Vault;

        if (!VaultLocation.TryResolve(scope.NamedVault, named, chosen, out var path, out _))
        {
            if (!settingsRead)
            {
                var unread = new HealthFinding(HealthArea.Vault, _subject, HealthStatus.Fix, $"no vault is chosen: app.toml could not be read ({problem})")
                {
                    Where = settingsPath,
                    Fix = $"fix or delete {settingsPath}, then run `keypaste use <path>`",
                };

                return Found(VaultState.AppTomlUnreadable, VaultSource.None, path: null, unread);
            }

            var none = new HealthFinding(HealthArea.Vault, _subject, HealthStatus.Fix, "no vault is chosen")
            {
                Fix = "open your vault in the keypaste app, or run `keypaste use <path>`",
            };

            return Found(VaultState.None, VaultSource.None, path: null, none);
        }

        var source = !string.IsNullOrEmpty(scope.NamedVault) ? VaultSource.Flag
            : !string.IsNullOrEmpty(named) ? VaultSource.Variable
            : VaultSource.Choice;
        var vault = source switch
        {
            VaultSource.Flag => "the vault named with --vault",
            VaultSource.Variable => $"the vault named by {VaultLocation.EnvironmentVariable}",
            _ => "the chosen vault",
        };

        try
        {
            _ = KdbxHeader.Read(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Found(VaultState.Missing, source, path, Fixed($"{vault} is missing", "run `keypaste use <path>` to choose your vault, or `keypaste init <path>` to create it"));
        }
        catch (VaultException)
        {
            return Found(VaultState.NotKeePass, source, path, Fixed($"{vault} is not a KeePass database", "run `keypaste use <path>` with your vault"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Found(VaultState.Unreadable, source, path, Fixed($"{vault} could not be read: {ex.Message}", "check that you can read it"));
        }

        var ok = new HealthFinding(HealthArea.Vault, _subject, HealthStatus.Ok, $"{vault} is a KeePass database") { Where = path };

        if (source != VaultSource.Choice && chosen is not null && !ChosenVault.Same(chosen, path))
        {
            var note = new HealthFinding(HealthArea.Vault, _subject, HealthStatus.Note, "AI tools use the chosen vault unless their registration names one") { Where = chosen };
            return new VaultReading(VaultState.Ok, source, path, [ok, note]);
        }

        return Found(VaultState.Ok, source, path, ok);

        HealthFinding Fixed(string says, string fix) => new(HealthArea.Vault, _subject, HealthStatus.Fix, says) { Where = path, Fix = fix };
    }

    private static VaultReading Found(VaultState state, VaultSource source, string? path, HealthFinding finding) =>
        new(state, source, path, [finding]);
}
