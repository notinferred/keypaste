using Keypaste.Core.Audit;

namespace Keypaste.Core.Settings;

/// <summary>How long the vault's owner keeps the agent log for, read from <c>app.toml</c> each time it compacts the log.</summary>
public static class LogRetention
{
    /// <summary>The period the person set, a year when no file sets one, or <see cref="AuditRetention.Forever"/> when the file cannot be read.</summary>
    /// <param name="home"><c>KEYPASTE_HOME</c>, or null for the default home.</param>
    /// <returns>The days to keep, as <see cref="AppSettings.KeepLogDays"/> holds them.</returns>
    /// <remarks>An unreadable file removes nothing, because only the person sets how long the log is kept (SECURITY.md law 3) and a removed day cannot come back.</remarks>
    public static int KeptDays(string? home) =>
        AppSettings.TryLoad(KeypasteHome.SettingsPath(home), out var settings) ? settings.KeepLogDays : AuditRetention.Forever;
}
