using Keypaste.App.Session;
using Keypaste.Core.Audit;

namespace Keypaste.App.ViewModels;

/// <summary>
/// Settings › Advanced › Diagnostics: the paths and version this app is using.
/// </summary>
/// <remarks>
/// The cheapest support tool this project will ever build. Nearly every "it can't find my vault" is
/// answered by showing which paths are actually in use and whether an environment variable is
/// overriding them.
/// </remarks>
internal sealed class DiagnosticsViewModel
{
    private readonly AppVaultSession _session;
    private readonly string? _home;

    internal DiagnosticsViewModel(AppVaultSession session, string? home)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        _home = home;
    }

    /// <summary>The vault that is open.</summary>
    internal string VaultPath => _session.VaultPath ?? "none";

    /// <summary>Where the machine-local files live.</summary>
    internal string HomePath => KeypasteHome.Resolve(_home);

    /// <summary>Whether <c>KEYPASTE_HOME</c> is overriding that.</summary>
    internal string HomeOverride => string.IsNullOrEmpty(_home) ? "not set" : _home;

    /// <summary>The app's version, for a bug report.</summary>
    internal static string Version =>
        typeof(DiagnosticsViewModel).Assembly.GetName().Version?.ToString() ?? "unknown";
}
