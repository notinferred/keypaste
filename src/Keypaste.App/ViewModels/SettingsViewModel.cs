using System.Globalization;
using Keypaste.App.Globalization;
using Keypaste.App.Resources;
using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Linking;
using Keypaste.Core.Login;
using Keypaste.Core.Recent;
using Keypaste.Core.Settings;

namespace Keypaste.App.ViewModels;

/// <summary>One offered idle timeout.</summary>
/// <param name="Seconds">The value written to <c>app.toml</c>.</param>
/// <param name="Label">How it reads in the list.</param>
internal sealed record IdleChoice(int Seconds, string Label);

/// <summary>One offered period the agent log is kept for.</summary>
/// <param name="Days">The value written to <c>app.toml</c>, <see cref="AuditRetention.Forever"/> for forever.</param>
/// <param name="Label">How it reads in the list.</param>
internal sealed record LogKeepChoice(int Days, string Label);

/// <summary>One offered number of days before an item expires that Recommendations lists it.</summary>
/// <param name="Days">The value written to <c>app.toml</c>, 0 for off.</param>
/// <param name="Label">How it reads in the list.</param>
internal sealed record ExpiryReminderChoice(int Days, string Label);

/// <summary>One vault General offers agents and the CLI, or None.</summary>
/// <param name="Path">The vault's file, or null for None.</param>
/// <param name="Label">Its name in the pop-up.</param>
internal sealed record AgentVaultChoice(string? Path, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Settings' sections that are not screens of their own: General, This vault, Browser and Agents.
/// </summary>
/// <remarks>
/// Every change is written immediately and applied immediately — there is no Save button and no way
/// to leave the screen with a setting that looks changed but is not. Shortening the idle timeout
/// re-arms the session on the spot rather than at the next lock.
/// </remarks>
internal sealed partial class SettingsViewModel : ObservableObject, IDisposable, IHoldsDraft
{
    private const string _translocatedNote =
        "This copy runs from the disk image or from a temporary copy macOS made, and is gone once it's ejected or quits. Move keypaste to Applications and open it from there to link it.";

    private readonly AppVaultSession _session;
    private readonly string? _home;
    private readonly DesktopPreferences _preferences;
    private readonly Action<AppTheme> _applyTheme;
    private readonly IVaultFilePicker? _picker;

    private AppSettings _settings;
    private IdleChoice _idle;
    private LogKeepChoice _keepLog;
    private ExpiryReminderChoice _expiryReminder;
    private AppTheme _theme;
    private bool _lockWhenMinimized;
    private bool _stayInTray;
    private bool? _openAtLogin;
    private bool _exporting;
    private CliLinkStatus? _cliStatus;
    private IReadOnlyList<AgentVaultChoice>? _agentVaultChoices;
    private IReadOnlyList<RecentVault>? _recentVaults;

    internal SettingsViewModel(
        AppVaultSession session,
        string? home,
        DesktopPreferences preferences,
        Action<AppTheme> applyTheme,
        IVaultFilePicker? picker = null,
        Func<SecretBuffer, Task<PasteOutcome>>? paste = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(applyTheme);

        _session = session;
        _home = home;
        _preferences = preferences;
        _applyTheme = applyTheme;
        _picker = picker;

        // The preferences the app was composed from, not a second read of the same file: what is
        // shown here is what is in force.
        _settings = preferences.Current;
        _theme = _settings.Theme;
        _lockWhenMinimized = _settings.LockWhenMinimized;
        _stayInTray = preferences.StaysInTray;

        IdleChoices = ChoicesFor(_settings.IdleTimeoutSeconds);
        _idle = IdleChoices.Single(choice => choice.Seconds == _settings.IdleTimeoutSeconds);
        KeepLogChoices = KeepChoicesFor(_settings.KeepLogDays);
        _keepLog = KeepLogChoices.Single(choice => choice.Days == _settings.KeepLogDays);
        ExpiryReminderChoices = ReminderChoicesFor(_settings.ExpiryReminderDays);
        _expiryReminder = ExpiryReminderChoices.Single(choice => choice.Days == _settings.ExpiryReminderDays);

        UseThisVaultCommand = new RelayCommand(() => ChooseAgentVault(_session.VaultPath!), () => !IsThisVaultChosen && _session.VaultPath is not null);
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => !_exporting && _picker is not null);
        Access = new VaultAccessViewModel(session, home, picker, paste);
        LinkCliCommand = new AsyncRelayCommand(LinkCliAsync, () => OffersCliLink && !IsCliLinking);
        BeginReplaceCliCommand = new RelayCommand(
            () => IsConfirmingCliReplace = true,
            () => OffersCliReplace && !IsConfirmingCliReplace && !IsCliLinking);
        ConfirmReplaceCliCommand = new AsyncRelayCommand(ReplaceCliAsync, () => IsConfirmingCliReplace && !IsCliLinking);
        CancelReplaceCliCommand = new RelayCommand(
            () => IsConfirmingCliReplace = false,
            () => IsConfirmingCliReplace && !IsCliLinking);
    }

    /// <summary>The unlocked vaults This vault's picker offers, or none where one vault is open.</summary>
    internal IReadOnlyList<VaultChoice> VaultChoices { get; init; } = [];

    /// <summary>Whether This vault starts with a picker of the vault it shows.</summary>
    internal bool OffersVaultChoice => VaultChoices.Count > 1;

    /// <summary>The vault This vault shows; choosing another shows that vault's settings through <see cref="ShowVault"/>.</summary>
    internal VaultChoice? ShownVault
    {
        get => VaultChoices.FirstOrDefault(choice => ReferenceEquals(choice.Vault, _session));
        set
        {
            if (value?.Vault is { } vault && !ReferenceEquals(vault, _session))
            {
                ShowVault?.Invoke(value);
            }
        }
    }

    /// <summary>Shows another vault's settings, set by the shell.</summary>
    internal Action<VaultChoice>? ShowVault { get; init; }

    /// <summary>Changing the vault's master password and keyfile.</summary>
    internal VaultAccessViewModel Access { get; }

    /// <inheritdoc/>
    public string? UnsavedDraft => Access.UnsavedDraft;

    /// <summary>
    /// The timeouts every vault is offered. There is deliberately no "never".
    /// </summary>
    /// <remarks>
    /// A "never" option would be the one setting everybody chose the first time the countdown
    /// interrupted them, and it would turn off the idle lock altogether. Eight hours
    /// covers a working day, which is the honest version of the same wish.
    /// </remarks>
    internal static IReadOnlyList<IdleChoice> Offered { get; } =
    [
        new(60, "1 minute"),
        new(5 * 60, "5 minutes"),
        new(15 * 60, "15 minutes"),
        new(30 * 60, "30 minutes"),
        new(60 * 60, "1 hour"),
        new(8 * 60 * 60, "8 hours"),
    ];

    /// <summary>
    /// What this screen's list shows: <see cref="Offered"/>, plus the timeout in force when the
    /// file asked for a number none of them names.
    /// </summary>
    /// <remarks>
    /// A hand-edited <c>idle_timeout_seconds</c> is honoured and named rather than snapped to the
    /// nearest offering. Snapping the label would put a number on the screen that the vault is not
    /// using; snapping the session would discard a legitimate edit; rewriting the file to match the list would clobber it. Whatever
    /// the file asked for, <see cref="AppSettings.IdleTimeoutSeconds"/> has already clamped it into
    /// a range that locks.
    /// </remarks>
    internal IReadOnlyList<IdleChoice> IdleChoices { get; }

    /// <summary>The part of this screen in view.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsGeneral), nameof(ShowsThisVault), nameof(ShowsBrowser), nameof(ShowsAgents))]
    internal partial SettingsSectionChoice Section { get; set; } = SettingsSectionChoice.General;

    partial void OnSectionChanged(SettingsSectionChoice value) => SectionIsOpen = true;

    /// <summary>Whether the part in view stands where a narrow window would otherwise show the list of parts.</summary>
    [ObservableProperty]
    internal partial bool SectionIsOpen { get; set; } = true;

    internal bool ShowsGeneral => Section == SettingsSectionChoice.General;

    internal bool ShowsThisVault => Section == SettingsSectionChoice.ThisVault;

    internal bool ShowsBrowser => Section == SettingsSectionChoice.Browser;

    internal bool ShowsAgents => Section == SettingsSectionChoice.Agents;

    /// <summary>Whether Appearance shows, only where the system reports no light or dark of its own.</summary>
    internal bool OffersTheme { get; init; }

    /// <summary>The three theme choices; System follows the operating system.</summary>
    internal static IReadOnlyList<AppTheme> Themes { get; } =
        [AppTheme.System, AppTheme.Light, AppTheme.Dark];

    /// <summary>How long the app may sit untouched.</summary>
    internal IdleChoice Idle
    {
        get => _idle;
        set
        {
            if (value is not null && SetProperty(ref _idle, value))
            {
                _session.IdleTimeout = TimeSpan.FromSeconds(value.Seconds);
                Persist(_settings with { IdleTimeoutSeconds = value.Seconds });
            }
        }
    }

    /// <summary>The periods the agent log may be kept for, never under 30 days.</summary>
    internal static IReadOnlyList<LogKeepChoice> OfferedLogKeeps { get; } =
    [
        new(90, Strings.LogKeptNinetyDays),
        new(AuditRetention.DefaultDays, Strings.LogKeptOneYear),
        new(2 * AuditRetention.DefaultDays, Strings.LogKeptTwoYears),
        new(AuditRetention.Forever, Strings.LogKeptForever),
    ];

    /// <summary>What this screen's list shows: <see cref="OfferedLogKeeps"/>, plus the period in force when <c>app.toml</c> names another.</summary>
    internal IReadOnlyList<LogKeepChoice> KeepLogChoices { get; }

    /// <summary>How long the vault's owner keeps the agent log, applied at its next compaction.</summary>
    internal LogKeepChoice KeepLog
    {
        get => _keepLog;
        set
        {
            if (value is not null && SetProperty(ref _keepLog, value))
            {
                Persist(_settings with { KeepLogDays = value.Days });
            }
        }
    }

    /// <summary>How many days before an item expires Recommendations may list it.</summary>
    internal static IReadOnlyList<ExpiryReminderChoice> OfferedExpiryReminders { get; } =
    [
        new(0, Strings.ExpiryReminderOff),
        new(7, Strings.ExpiryReminderSevenDays),
        new(AppSettings.DefaultExpiryReminderDays, Strings.ExpiryReminderFourteenDays),
        new(30, Strings.ExpiryReminderThirtyDays),
    ];

    /// <summary>What this screen's list shows: <see cref="OfferedExpiryReminders"/>, plus the days in force when <c>app.toml</c> names others.</summary>
    internal IReadOnlyList<ExpiryReminderChoice> ExpiryReminderChoices { get; }

    /// <summary>How many days before an item expires Recommendations lists it, applied at once.</summary>
    internal ExpiryReminderChoice ExpiryReminder
    {
        get => _expiryReminder;
        set
        {
            if (value is not null && SetProperty(ref _expiryReminder, value))
            {
                Persist(_settings with { ExpiryReminderDays = value.Days });
            }
        }
    }

    /// <summary>Light, dark, or whatever the operating system says.</summary>
    internal AppTheme Theme
    {
        get => _theme;
        set
        {
            if (SetProperty(ref _theme, value))
            {
                _applyTheme(value);
                Persist(_settings with { Theme = value });
            }
        }
    }

    /// <summary>Whether this platform can tell the app that its window was minimized.</summary>
    /// <remarks>
    /// The checkbox is omitted where the answer is no, rather than offered and inert. An instance
    /// property because a binding needs one, in the shape <see cref="ShellViewModel"/> already uses.
    /// </remarks>
#pragma warning disable CA1822
    internal bool MinimizeLockSupported => MinimizeLock.IsSupported;
#pragma warning restore CA1822

    /// <summary>Whether minimizing counts as leaving.</summary>
    /// <remarks>
    /// <para>
    /// Off by default. Minimizing is not a security event for most people, and for the few for whom
    /// it is the gesture that means "I am leaving", it is one checkbox.
    /// </para>
    /// <para>
    /// Writing it is applying it. <see cref="MinimizeLock"/> asks the same
    /// <see cref="DesktopPreferences"/> this screen writes through, at each minimize, so there is
    /// no second step here and no restart between ticking the box and being protected by it.
    /// </para>
    /// </remarks>
    internal bool LockWhenMinimized
    {
        get => _lockWhenMinimized;
        set
        {
            if (SetProperty(ref _lockWhenMinimized, value))
            {
                Persist(_settings with { LockWhenMinimized = value });
            }
        }
    }

    /// <summary>Whether macOS draws the rail and the sidebar over its vibrancy; only macOS has it to reduce.</summary>
    public static bool OffersReduceTransparency => OperatingSystem.IsMacOS();

    /// <summary>Settings › General's Reduce transparency: the rail and the sidebar drawn solid, as they always are on Windows, Linux and under high contrast.</summary>
    internal bool ReduceTransparency
    {
        get => _settings.ReduceTransparency;
        set
        {
            if (_settings.ReduceTransparency != value)
            {
                Persist(_settings with { ReduceTransparency = value });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>The words for where the app stays: the menu bar on macOS, the tray elsewhere.</summary>
#pragma warning disable CA1822
    internal string StayInTrayLabel => OperatingSystem.IsMacOS()
        ? Strings.KeepInMenuBar
        : Strings.KeepInTray;
#pragma warning restore CA1822

    /// <summary>Whether closing the window leaves the app in the menu bar or tray, its open vaults unlocked, rather than quitting.</summary>
    internal bool StayInTray
    {
        get => _stayInTray;
        set
        {
            if (SetProperty(ref _stayInTray, value))
            {
                Persist(_settings with { StayInTray = value });
            }
        }
    }

    /// <summary>Browser fill: pairing a browser and the pairings the vault holds; null where nothing answers browsers.</summary>
    internal BrowserFillViewModel? BrowserFill { get; init; }

    internal bool HasBrowserFill => BrowserFill is not null;

    /// <summary>Browser fill's browsers, each turned on and off as keypaste's host; null where the shell lists none.</summary>
    internal BrowserHostsViewModel? BrowserHosts { get; init; }

    /// <summary>What every agent can see and gets when it asks, for Settings › Agents; null where the vault serves no agents.</summary>
    internal AgentRulesViewModel? AgentDefaults { get; init; }

    internal bool HasBrowserHosts => BrowserHosts is not null;

    /// <summary>The entry that opens the app at login, or null where Settings offers none.</summary>
    internal ILoginItem? Login { get; init; }

    internal bool OpenAtLoginSupported => Login is not null;

    /// <summary>Whether the app opens at login; read from the platform's own entry, which is the only record of it.</summary>
    internal bool OpenAtLogin
    {
        get => _openAtLogin ??= Login?.IsEnabled == true;
        set
        {
            if (Login is null || value == OpenAtLogin)
            {
                return;
            }

            var done = value ? Login.Enable() : Login.Disable();
            _openAtLogin = Login.IsEnabled;
            OnPropertyChanged();

            if (!done)
            {
                Message = (value, Login.RunsFromTemporaryLocation) switch
                {
                    (true, true) => "This copy runs from the disk image or from a temporary copy macOS made. Move keypaste to Applications, open it from there, then turn on Open at login.",
                    (true, false) => "keypaste could not add itself to what opens at login. Try again, or add it in your system's login items.",
                    (false, _) => "keypaste could not remove itself from what opens at login. Try again, or remove it in your system's login items.",
                };
            }
        }
    }

    /// <summary>The vaults General's pop-up offers agents and the CLI: this computer's recent and open vaults, and None while none is chosen.</summary>
    internal IReadOnlyList<AgentVaultChoice> AgentVaultChoices => _agentVaultChoices ??= ReadAgentVaultChoices();

    /// <summary>The vault agents and the CLI use without <c>--vault</c>; choosing one writes it at once.</summary>
    internal AgentVaultChoice? AgentVault
    {
        get => AgentVaultChoices.FirstOrDefault(choice => ChosenVault.Same(choice.Path, ChosenVault.Read(_home)))
            ?? AgentVaultChoices.FirstOrDefault(choice => choice.Path is null);
        set
        {
            if (value?.Path is { } path && !ChosenVault.Same(path, ChosenVault.Read(_home)))
            {
                ChooseAgentVault(path);
            }
        }
    }

    /// <summary>Makes the unlocked vault the one agents and the CLI use without <c>--vault</c>.</summary>
    internal RelayCommand UseThisVaultCommand { get; }

    internal bool IsThisVaultChosen => ChosenVault.Same(ChosenVault.Read(_home), _session.VaultPath);

    private List<AgentVaultChoice> ReadAgentVaultChoices()
    {
        var chosen = ChosenVault.Read(_home);
        List<string> paths = [.. new[] { chosen, _session.VaultPath }.OfType<string>()];
        paths.AddRange(VaultChoices.Select(choice => choice.Vault?.VaultPath).OfType<string>());
        paths.AddRange(RecentList.Select(recent => recent.Path));

        List<AgentVaultChoice> choices = chosen is null ? [new(null, Strings.NoneWord)] : [];
        foreach (var path in paths.Where(path => !choicesHave(path)))
        {
            choices.Add(new(path, Path.GetFileNameWithoutExtension(path)));
        }

        return choices;

        bool choicesHave(string path) => choices.Exists(choice => ChosenVault.Same(choice.Path, path));
    }

    /// <summary>How many vaults keypaste remembers having opened on this computer.</summary>
    internal string RecentVaultsCount => RecentList.Count switch
    {
        0 => Strings.NoneWord,
        1 => "1 vault",
        var count => string.Create(CultureInfo.InvariantCulture, $"{count} vaults"),
    };

    internal bool HasRecentVaults => RecentList.Count > 0;

    /// <summary>The recent-vaults file, read once for the screen and again after Settings writes it.</summary>
    private IReadOnlyList<RecentVault> RecentList => _recentVaults ??= RecentVaults.Load(KeypasteHome.RecentPath(_home));

    /// <summary>Links the <c>keypaste</c> a terminal runs to this app's own, or null where Settings offers no link (Windows, and an app with no <c>keypaste</c> beside it).</summary>
    internal ICliLink? CliLink { get; init; }

    internal bool CliLinkSupported => CliLink is not null;

    /// <summary>What a terminal's <c>keypaste</c> is now.</summary>
    internal string CliLinkText => CliStatus switch
    {
        null => string.Empty,
        { State: CliLinkState.ThisCopy, EarlierCopy: null } status => $"A terminal runs this app's keypaste from {status.LinkPath}.",
        { State: CliLinkState.ThisCopy } status => $"{status.LinkPath} runs this app's keypaste.",
        { State: CliLinkState.OtherCopy } status => $"{status.LinkPath} runs keypaste from {status.LinkedTo}.",
        { State: CliLinkState.Foreign, LinkedTo: { } target } status => $"{status.LinkPath} is another program, not this app's keypaste. It links to {target}.",
        { State: CliLinkState.Foreign } status => $"{status.LinkPath} is another program, not this app's keypaste.",
        { } status => $"Link this app's keypaste at {status.LinkPath} so a terminal can run it.{AdministratorNote(status)}",
    };

    /// <summary>What stands in the way of a terminal finding this app's <c>keypaste</c>, or nothing.</summary>
    internal string CliLinkNote
    {
        get
        {
            if (CliStatus is not { } status)
            {
                return string.Empty;
            }

            List<string> notes = [];

            if (status.Translocated)
            {
                notes.Add(_translocatedNote);
            }

            if (status is { State: CliLinkState.Foreign, BackupExists: true })
            {
                notes.Add(BackupTaken(status.LinkPath, status.BackupPath));
            }

            if (status.EarlierCopy is { } earlier)
            {
                notes.Add(
                    $"A terminal may run {earlier} instead, if its folder comes before {Path.GetDirectoryName(status.LinkPath)} on PATH. " +
                    "Move or delete it so a terminal runs this app's keypaste.");
            }

            if (!status.DirectoryOnPath)
            {
                notes.Add(
                    $"{Path.GetDirectoryName(status.LinkPath)} is not on PATH yet. Most Linux desktops add it " +
                    "at your next login; otherwise add it to PATH in your shell's startup file.");
            }

            return string.Join(' ', notes);
        }
    }

    internal bool HasCliLinkNote => CliLinkNote.Length > 0;

    /// <summary>Whether Link is offered: nothing is at the path, or another copy's link is.</summary>
    internal bool OffersCliLink => CliStatus is { State: CliLinkState.Absent or CliLinkState.OtherCopy, Translocated: false };

    /// <summary>Whether Replace is offered: something keypaste did not write is at the path, and nothing is at its backup's.</summary>
    internal bool OffersCliReplace => CliStatus is { State: CliLinkState.Foreign, BackupExists: false, Translocated: false };

    /// <summary>Whether a link or a replacement is under way, which on macOS lasts until the person answers the system's dialog.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LinkCliCommand), nameof(BeginReplaceCliCommand), nameof(ConfirmReplaceCliCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelReplaceCliCommand))]
    internal partial bool IsCliLinking { get; private set; }

    /// <summary>Whether the confirmation that names what Replace moves is showing.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BeginReplaceCliCommand), nameof(ConfirmReplaceCliCommand), nameof(CancelReplaceCliCommand))]
    internal partial bool IsConfirmingCliReplace { get; private set; }

    /// <summary>What Replace moves and where it keeps it.</summary>
    internal string CliReplacePrompt => CliStatus is { } status
        ? $"Replace {status.LinkPath} with this app's keypaste? The file there now is kept as {status.BackupPath}.{AdministratorNote(status)}"
        : string.Empty;

    internal AsyncRelayCommand LinkCliCommand { get; }

    /// <summary>Shows the confirmation; nothing moves until <see cref="ConfirmReplaceCliCommand"/>.</summary>
    internal RelayCommand BeginReplaceCliCommand { get; }

    internal AsyncRelayCommand ConfirmReplaceCliCommand { get; }

    internal RelayCommand CancelReplaceCliCommand { get; }

    // Read when the row is first shown and again after each link or replacement.
    private CliLinkStatus? CliStatus => _cliStatus ??= CliLink?.Read();

    /// <summary>Writes an exact encrypted copy of the open vault where the person says.</summary>
    internal AsyncRelayCommand ExportCommand { get; }

    /// <summary>Where a save keeps the file it replaces.</summary>
    /// <remarks>
    /// The desktop's counterpart to the line the CLI prints when it creates the directory: a
    /// folder of encrypted vaults appearing beside somebody's file should not be a discovery.
    /// </remarks>
    internal string BackupsPath =>
        _session.VaultPath is { } path ? VaultBackups.DirectoryFor(path) : "none";

    /// <summary>How many copies are there, and how old the newest is. Read each time, never cached.</summary>
    internal string BackupsSummary => _session.VaultPath is { } path
        ? VaultBackups.List(path) switch
        {
            [] => Strings.NoCopiesYet,
            [var only] => $"1 copy · latest {When(only)}",
            [var newest, ..] all => string.Create(CultureInfo.InvariantCulture, $"{all.Count} copies · latest {When(newest)}"),
        }
        : string.Empty;

    /// <summary>Whether a copy is there to put back, for the line that says how.</summary>
    internal bool HasBackupCopies => _session.VaultPath is { } path && VaultBackups.List(path).Count > 0;

#pragma warning disable CA1822
    internal string CloseHelp => Strings.ClosingTheWindowKeepsKeypasteRunning;

    internal string BackupsRule =>
        $"A save keeps the file it replaces: the first save after each unlock, no more often than every " +
        $"fifteen minutes, keeping the last {VaultBackups.Retained}, one a day for a week and one a week for four weeks. Each copy opens with the master " +
        "password it was made under, and none of them recovers a forgotten one.";

    internal string RestoreHint =>
        "To put one back, lock keypaste and choose Restore a backup on the unlock screen.";

    internal string ElsewhereAdvice => Strings.KeepACopyOnAnotherDisk;
#pragma warning restore CA1822

    /// <summary>Confirmation of the last thing that happened, or nothing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    internal partial string Message { get; private set; } = string.Empty;

    internal bool HasMessage => Message.Length > 0;

    /// <summary>Asks where, then has the core write the copy.</summary>
    /// <remarks>
    /// <c>internal</c> for <see cref="UnlockViewModel.UnlockAsync"/>'s reason. The suggested name
    /// only saves typing; every refusal is <see cref="Vault.ExportTo"/>'s.
    /// </remarks>
    internal async Task ExportAsync()
    {
        if (_exporting || _picker is null || _session.VaultPath is not { } path)
        {
            return;
        }

        var stem = Path.GetFileNameWithoutExtension(path);
        var today = _session.Clock.GetLocalNow().ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        if (await _picker.PickExportDestinationAsync($"{stem}-copy-{today}.kdbx").ConfigureAwait(true) is not { } destination)
        {
            return;
        }

        _exporting = true;
        ExportCommand.NotifyCanExecuteChanged();

        try
        {
            // A vault unlocked while the picker was open is not the one the person chose to copy.
            var write = await Task.Run(() => _session.Write(
                vault =>
                {
                    if (!PathIdentity.SameFile(vault.Path, path))
                    {
                        return false;
                    }

                    vault.ExportTo(destination);
                    return true;
                },
                _ => false)).ConfigureAwait(true);

            Message = write.Outcome switch
            {
                WriteOutcome.NothingToSave when write.Value => $"An encrypted copy is at {destination}. It opens with this vault's master password.",
                WriteOutcome.NothingToSave or WriteOutcome.Locked => "The vault locked before the copy was made. Nothing was written. Unlock it, then try again.",
                WriteOutcome.ChangedOnDisk when write.Merged => write.Problem("export again")!,
                WriteOutcome.ChangedOnDisk => "The vault file changed since it was unlocked. Reload to see it, then export.",
                _ => write.Reason!,
            };
        }
        finally
        {
            _exporting = false;
            ExportCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Zeroes whatever the access form holds; the shell calls this on lock and on leaving.</summary>
    public void Dispose()
    {
        Access.Dispose();
        BrowserFill?.Dispose();
        AgentDefaults?.Dispose();
    }

    private static string When(VaultBackup backup) =>
        PersonTime.Moment(backup.TakenAt.ToLocalTime());

    private void ChooseAgentVault(string path)
    {
        Message = ChosenVault.Choose(_home, path, onlyIfNone: false) switch
        {
            ChooseOutcome.Chosen => $"Agents and the keypaste CLI now use {Path.GetFileNameWithoutExtension(path)} when no --vault is given.",
            ChooseOutcome.Unreadable => $"{KeypasteHome.SettingsPath(_home)} could not be read, so it was left as it is. Fix or delete it, then choose again.",
            ChooseOutcome.Unrecordable => "This vault's path holds a quote, a control character or a backslash, which app.toml cannot record. Move or rename the vault.",
            _ => $"{KeypasteHome.SettingsPath(_home)} could not be written, so nothing changed. Check that you can write to it, then choose again.",
        };

        _agentVaultChoices = null;
        OnPropertyChanged(nameof(AgentVaultChoices));
        OnPropertyChanged(nameof(AgentVault));
        OnPropertyChanged(nameof(IsThisVaultChosen));
        UseThisVaultCommand.NotifyCanExecuteChanged();
    }

    private static string AdministratorNote(CliLinkStatus status) =>
        OperatingSystem.IsMacOS() && !status.Translocated ? " macOS asks for an administrator's password." : string.Empty;

    private static string BackupTaken(string linkPath, string backupPath) =>
        $"{backupPath} already exists, so keypaste replaces nothing. Move or delete it to replace {linkPath}.";

    private async Task LinkCliAsync()
    {
        if (CliLink is { } link)
        {
            Message = CliLinkMessage(link, await ChangeCliLinkAsync(link.Link).ConfigureAwait(true));
        }
    }

    private async Task ReplaceCliAsync()
    {
        if (CliLink is { } link)
        {
            var result = await ChangeCliLinkAsync(link.Replace).ConfigureAwait(true);
            IsConfirmingCliReplace = false;
            Message = CliLinkMessage(link, result);
        }
    }

    private async Task<CliLinkResult> ChangeCliLinkAsync(Func<CliLinkResult> change)
    {
        IsCliLinking = true;

        try
        {
            return await Task.Run(change).ConfigureAwait(true);
        }
        finally
        {
            _cliStatus = CliLink?.Read();
            IsCliLinking = false;
            OnPropertyChanged(nameof(CliStatus));
            OnPropertyChanged(nameof(CliLinkText));
            OnPropertyChanged(nameof(CliLinkNote));
            OnPropertyChanged(nameof(CliReplacePrompt));
            OnPropertyChanged(nameof(OffersCliLink));
            OnPropertyChanged(nameof(OffersCliReplace));
            OnPropertyChanged(nameof(HasCliLinkNote));
            LinkCliCommand.NotifyCanExecuteChanged();
            BeginReplaceCliCommand.NotifyCanExecuteChanged();
        }
    }

    private static string CliLinkMessage(ICliLink link, CliLinkResult result) => result switch
    {
        { Outcome: CliLinkOutcome.Linked } => $"{link.LinkPath} now runs this app's keypaste.",
        { Outcome: CliLinkOutcome.Replaced } => $"{link.LinkPath} now runs this app's keypaste; the old file is {link.BackupPath}.",
        { Outcome: CliLinkOutcome.Cancelled } => "Nothing was linked.",
        { Refusal: CliLinkRefusal.BackupExists } => BackupTaken(link.LinkPath, link.BackupPath),
        { Refusal: CliLinkRefusal.NotKeypaste } => $"{link.LinkPath} changed before keypaste linked it, so it was left as it is.",
        { Refusal: CliLinkRefusal.Translocated } => _translocatedNote,
        { Refusal: CliLinkRefusal.UnsafeProgram } => $"keypaste could not link {link.LinkPath}: this app's path holds a line break. Move the app to a path without one.",
        _ => $"keypaste could not link {link.LinkPath}: {result.Reason?.TrimEnd('.')}. Correct that, then link again.",
    };

    /// <summary>Whether "Forget them all" is waiting on its confirmation.</summary>
    [ObservableProperty]
    internal partial bool IsConfirmingForgetAll { get; private set; }

    [RelayCommand]
    private void AskForgetAll() => IsConfirmingForgetAll = true;

    [RelayCommand]
    private void CancelForgetAll() => IsConfirmingForgetAll = false;

    /// <summary>Empties the recent-vaults list, having been confirmed.</summary>
    [RelayCommand]
    private void ForgetAll()
    {
        IsConfirmingForgetAll = false;
        RecentVaults.Save(KeypasteHome.RecentPath(_home), []);
        _recentVaults = null;
        Message = "The recent vaults list is empty.";
        OnPropertyChanged(nameof(RecentVaultsCount));
        OnPropertyChanged(nameof(HasRecentVaults));
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

        Message = _preferences.Update(settings)
            ? string.Empty
            : "That preference could not be saved, so it will not survive a restart. Check that keypaste's folder can be written, then change it again.";
    }

    /// <summary>The offered periods, widened by the one in force if it is not among them, forever last.</summary>
    private static IReadOnlyList<LogKeepChoice> KeepChoicesFor(int days)
    {
        if (OfferedLogKeeps.Any(choice => choice.Days == days))
        {
            return OfferedLogKeeps;
        }

        return
        [
            .. OfferedLogKeeps
                .Append(new LogKeepChoice(days, Counted(days, "day")))
                .OrderBy(choice => choice.Days == AuditRetention.Forever)
                .ThenBy(choice => choice.Days),
        ];
    }

    /// <summary>The offered reminders, widened by the one in force if it is not among them.</summary>
    private static IReadOnlyList<ExpiryReminderChoice> ReminderChoicesFor(int days) =>
        OfferedExpiryReminders.Any(choice => choice.Days == days)
            ? OfferedExpiryReminders
            : [.. OfferedExpiryReminders.Append(new ExpiryReminderChoice(days, Counted(days, "day"))).OrderBy(choice => choice.Days)];

    /// <summary>The offered timeouts, widened by the one in force if it is not among them.</summary>
    private static IReadOnlyList<IdleChoice> ChoicesFor(int seconds)
    {
        foreach (var choice in Offered)
        {
            if (choice.Seconds == seconds)
            {
                return Offered;
            }
        }

        var widened = new List<IdleChoice>(Offered) { new(seconds, Label(seconds)) };
        widened.Sort((left, right) => left.Seconds.CompareTo(right.Seconds));

        return widened;
    }

    /// <summary>Names a number of seconds the way the offered labels read.</summary>
    private static string Label(int seconds)
    {
        var parts = new List<string>(3);

        if (seconds / 3600 is var hours and > 0)
        {
            parts.Add(Counted(hours, "hour"));
        }

        if (seconds % 3600 / 60 is var minutes and > 0)
        {
            parts.Add(Counted(minutes, "minute"));
        }

        if (seconds % 60 is var rest and > 0)
        {
            parts.Add(Counted(rest, "second"));
        }

        return parts.Count > 0 ? string.Join(' ', parts) : Counted(seconds, "second");
    }

    private static string Counted(int count, string unit) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {unit}{(count == 1 ? string.Empty : "s")}");
}
