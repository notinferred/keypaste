using Keypaste.App.Session;
using Keypaste.Core.Audit;
using Keypaste.Core.Recent;
using Keypaste.Core.Settings;

namespace Keypaste.App.ViewModels;

/// <summary>One offered idle timeout.</summary>
/// <param name="Seconds">The value written to <c>app.toml</c>.</param>
/// <param name="Label">How it reads in the list.</param>
internal sealed record IdleChoice(int Seconds, string Label);

/// <summary>
/// Settings, and the facts a person needs when something is not where they expected.
/// </summary>
/// <remarks>
/// <para>
/// Every change is written immediately and applied immediately — there is no Save button and no way
/// to leave the screen with a setting that looks changed but is not. Shortening the idle timeout
/// re-arms the session on the spot rather than at the next lock.
/// </para>
/// <para>
/// <b>The facts block is the cheapest support tool this project will ever build.</b> Nearly every
/// "it can't find my vault" is answered by showing which paths are actually in use and whether an
/// environment variable is overriding them.
/// </para>
/// </remarks>
internal sealed class SettingsViewModel : ObservableObject
{
    private readonly AppVaultSession _session;
    private readonly string? _home;
    private readonly Action<AppTheme> _applyTheme;

    private AppSettings _settings;
    private IdleChoice _idle;
    private AppTheme _theme;
    private bool _lockWhenMinimized;
    private string _message = string.Empty;

    internal SettingsViewModel(AppVaultSession session, string? home, Action<AppTheme> applyTheme)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(applyTheme);

        _session = session;
        _home = home;
        _applyTheme = applyTheme;

        _settings = AppSettings.Load(KeypasteHome.SettingsPath(home));
        _theme = _settings.Theme;
        _lockWhenMinimized = _settings.LockWhenMinimized;
        _idle = Nearest(_settings.IdleTimeoutSeconds);

        ForgetAllCommand = new RelayCommand(ForgetAll);
    }

    /// <summary>
    /// The timeouts on offer. There is deliberately no "never".
    /// </summary>
    /// <remarks>
    /// A "never" option would be the one setting everybody chose the first time the countdown
    /// interrupted them, and it would turn off the feature this stage exists to ship. Eight hours
    /// covers a working day, which is the honest version of the same wish.
    /// </remarks>
    internal static IReadOnlyList<IdleChoice> IdleChoices { get; } =
    [
        new(60, "1 minute"),
        new(5 * 60, "5 minutes"),
        new(15 * 60, "15 minutes"),
        new(30 * 60, "30 minutes"),
        new(60 * 60, "1 hour"),
        new(8 * 60 * 60, "8 hours"),
    ];

    /// <summary>The three theme choices; System follows the operating system.</summary>
    internal static IReadOnlyList<AppTheme> Themes { get; } =
        [AppTheme.System, AppTheme.Light, AppTheme.Dark];

    /// <summary>How long the app may sit untouched.</summary>
    internal IdleChoice Idle
    {
        get => _idle;
        set
        {
            if (value is not null && Set(ref _idle, value))
            {
                _session.IdleTimeout = TimeSpan.FromSeconds(value.Seconds);
                Persist(_settings with { IdleTimeoutSeconds = value.Seconds });
            }
        }
    }

    /// <summary>Light, dark, or whatever the operating system says.</summary>
    internal AppTheme Theme
    {
        get => _theme;
        set
        {
            if (Set(ref _theme, value))
            {
                _applyTheme(value);
                Persist(_settings with { Theme = value });
            }
        }
    }

    /// <summary>Whether minimizing counts as leaving.</summary>
    /// <remarks>
    /// Off by default. Minimizing is not a security event for most people, and for the few for whom
    /// it is the gesture that means "I am leaving", it is one checkbox.
    /// </remarks>
    internal bool LockWhenMinimized
    {
        get => _lockWhenMinimized;
        set
        {
            if (Set(ref _lockWhenMinimized, value))
            {
                Persist(_settings with { LockWhenMinimized = value });
            }
        }
    }

    /// <summary>Empties the recent-vaults list.</summary>
    internal RelayCommand ForgetAllCommand { get; }

    /// <summary>The vault that is open.</summary>
    internal string VaultPath => _session.VaultPath ?? "none";

    /// <summary>Where the machine-local files live.</summary>
    internal string HomePath => KeypasteHome.Resolve(_home);

    /// <summary>Whether <c>KEYPASTE_HOME</c> is overriding that.</summary>
    internal string HomeOverride =>
        string.IsNullOrEmpty(_home) ? "not set" : _home;

    /// <summary>The app's version, for a bug report.</summary>
    internal static string Version =>
        typeof(SettingsViewModel).Assembly.GetName().Version?.ToString() ?? "unknown";

    /// <summary>Confirmation of the last thing that happened, or nothing.</summary>
    internal string Message
    {
        get => _message;
        private set
        {
            if (Set(ref _message, value))
            {
                Raise(nameof(HasMessage));
            }
        }
    }

    internal bool HasMessage => _message.Length > 0;

    private void ForgetAll()
    {
        RecentVaults.Save(KeypasteHome.RecentPath(_home), []);
        Message = "The recent vaults list is empty.";
    }

    /// <summary>
    /// Writes the file, and says so only when it could not.
    /// </summary>
    /// <remarks>
    /// A failed write costs a preference and never costs a lock — the session was already told
    /// about the new timeout before this ran, so an unwritable file means the setting holds for
    /// this run and not the next one, which is worth saying rather than swallowing.
    /// </remarks>
    private void Persist(AppSettings settings)
    {
        _settings = settings;

        Message = AppSettings.Save(KeypasteHome.SettingsPath(_home), settings)
            ? string.Empty
            : "That preference could not be saved, so it will not survive a restart.";
    }

    private static IdleChoice Nearest(int seconds)
    {
        var best = IdleChoices[1];

        foreach (var choice in IdleChoices)
        {
            if (Math.Abs(choice.Seconds - seconds) < Math.Abs(best.Seconds - seconds))
            {
                best = choice;
            }
        }

        return best;
    }
}
