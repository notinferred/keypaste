using System.Globalization;
using Keypaste.App.Navigation;
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

/// <summary>
/// Settings, with the way to Advanced, which leads to the screens the everyday ones leave out.
/// </summary>
/// <remarks>
/// Every change is written immediately and applied immediately — there is no Save button and no way
/// to leave the screen with a setting that looks changed but is not. Shortening the idle timeout
/// re-arms the session on the spot rather than at the next lock.
/// </remarks>
internal sealed class SettingsViewModel : ObservableObject, IDisposable
{
    private const string _translocatedNote =
        "macOS is running this copy from a temporary location. Move keypaste to Applications and open it again to link it.";

    private readonly AppVaultSession _session;
    private readonly string? _home;
    private readonly DesktopPreferences _preferences;
    private readonly Action<AppTheme> _applyTheme;
    private readonly IVaultFilePicker? _picker;

    private AppSettings _settings;
    private IdleChoice _idle;
    private AppTheme _theme;
    private bool _lockWhenMinimized;
    private bool _stayInTray;
    private bool? _openAtLogin;
    private string _message = string.Empty;
    private bool _exporting;
    private CliLinkStatus? _cliStatus;
    private bool _cliLinking;
    private bool _confirmingCliReplace;

    internal SettingsViewModel(
        AppVaultSession session,
        string? home,
        DesktopPreferences preferences,
        Action<AppTheme> applyTheme,
        IVaultFilePicker? picker = null,
        RecommendationsViewModel? recommendations = null,
        Action<DestinationKind>? open = null,
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

        ForgetAllCommand = new RelayCommand(ForgetAll);
        UseThisVaultCommand = new RelayCommand(UseThisVault, () => !IsThisVaultChosen && _session.VaultPath is not null);
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => !_exporting && _picker is not null);
        Access = new VaultAccessViewModel(session, home, picker, paste);
        Recommendations = recommendations;
        OpenAdvancedCommand = new RelayCommand(() => open?.Invoke(DestinationKind.Advanced), () => open is not null);
        LinkCliCommand = new AsyncRelayCommand(LinkCliAsync, () => OffersCliLink && !IsCliLinking);
        BeginReplaceCliCommand = new RelayCommand(
            () => IsConfirmingCliReplace = true,
            () => OffersCliReplace && !IsConfirmingCliReplace && !IsCliLinking);
        ConfirmReplaceCliCommand = new AsyncRelayCommand(ReplaceCliAsync, () => IsConfirmingCliReplace && !IsCliLinking);
        CancelReplaceCliCommand = new RelayCommand(
            () => IsConfirmingCliReplace = false,
            () => IsConfirmingCliReplace && !IsCliLinking);

        DependsOn(nameof(HasMessage), nameof(Message));
        DependsOn(nameof(ChosenVaultText), nameof(IsThisVaultChosen));
        DependsOn(UseThisVaultCommand, nameof(IsThisVaultChosen));
        DependsOn(nameof(CliLinkText), nameof(CliStatus));
        DependsOn(nameof(CliLinkNote), nameof(CliStatus));
        DependsOn(nameof(HasCliLinkNote), nameof(CliLinkNote));
        DependsOn(nameof(CliReplacePrompt), nameof(CliStatus));
        DependsOn(nameof(OffersCliLink), nameof(CliStatus));
        DependsOn(nameof(OffersCliReplace), nameof(CliStatus));
        DependsOn(LinkCliCommand, nameof(OffersCliLink), nameof(IsCliLinking));
        DependsOn(BeginReplaceCliCommand, nameof(OffersCliReplace), nameof(IsConfirmingCliReplace), nameof(IsCliLinking));
        DependsOn(ConfirmReplaceCliCommand, nameof(IsConfirmingCliReplace), nameof(IsCliLinking));
        DependsOn(CancelReplaceCliCommand, nameof(IsConfirmingCliReplace), nameof(IsCliLinking));
    }

    /// <summary>Settings › Advanced, the screen that leads to the activity log, share links, scoped tokens and diagnostics.</summary>
    internal RelayCommand OpenAdvancedCommand { get; }

    /// <summary>Recommendations, owned by the shell for the unlock; null where no shell built this screen.</summary>
    internal RecommendationsViewModel? Recommendations { get; }

    internal bool HasRecommendations => Recommendations is not null;

    /// <summary>Changing the vault's master password and keyfile.</summary>
    internal VaultAccessViewModel Access { get; }

    /// <summary>
    /// The timeouts every vault is offered. There is deliberately no "never".
    /// </summary>
    /// <remarks>
    /// A "never" option would be the one setting everybody chose the first time the countdown
    /// interrupted them, and it would turn off the feature this stage exists to ship. Eight hours
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
    /// using, which is the defect this task exists to close; snapping the session would discard a
    /// legitimate edit; rewriting the file to match the list would clobber it (D-0095). Whatever
    /// the file asked for, <see cref="AppSettings.IdleTimeoutSeconds"/> has already clamped it into
    /// a range that locks.
    /// </remarks>
    internal IReadOnlyList<IdleChoice> IdleChoices { get; }

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

    /// <summary>Whether this platform can tell the app that its window was minimized.</summary>
    /// <remarks>
    /// The checkbox is omitted where the answer is no, rather than offered and inert — which is the
    /// defect F.2b repairs, and it would be no better for being platform-shaped. An instance
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
            if (Set(ref _lockWhenMinimized, value))
            {
                Persist(_settings with { LockWhenMinimized = value });
            }
        }
    }

    /// <summary>The words for where the app stays: the menu bar on macOS, the tray elsewhere.</summary>
#pragma warning disable CA1822
    internal string StayInTrayLabel => OperatingSystem.IsMacOS()
        ? "Keep keypaste in the menu bar when the window closes"
        : "Keep keypaste in the tray when the window closes";
#pragma warning restore CA1822

    /// <summary>Whether closing the window leaves the app, locked, in the menu bar or tray rather than quitting.</summary>
    internal bool StayInTray
    {
        get => _stayInTray;
        set
        {
            if (Set(ref _stayInTray, value))
            {
                Persist(_settings with { StayInTray = value });
            }
        }
    }

    /// <summary>The app's crash reports, whose switch this screen shows; null where the app composed none.</summary>
    internal CrashReporting? Crashes { get; init; }

    internal bool HasCrashReports => Crashes is not null;

    /// <summary>Whether crash reports are sent, as chosen at first run; turning it off deletes the reports waiting (D-0594).</summary>
    internal bool SendCrashReports
    {
        get => Crashes?.IsOn == true;
        set
        {
            if (Crashes is not { } crashes || value == crashes.IsOn)
            {
                return;
            }

            var saved = crashes.Choose(value);

            // The next preference this screen writes starts from the choice, not from what it read when it opened.
            _settings = _preferences.Current;
            Raise();
            Message = saved ? string.Empty : "That preference could not be saved, so it will not survive a restart.";
        }
    }

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
            Raise();

            if (!done)
            {
                Message = value
                    ? "keypaste could not add itself to what opens at login."
                    : "keypaste could not remove itself from what opens at login.";
            }
        }
    }

    /// <summary>Empties the recent-vaults list.</summary>
    internal RelayCommand ForgetAllCommand { get; }

    /// <summary>Makes the unlocked vault the one agents and the CLI use without <c>--vault</c> (D-0389).</summary>
    internal RelayCommand UseThisVaultCommand { get; }

    /// <summary>The vault agents and the CLI use without <c>--vault</c>, read each time.</summary>
    internal string ChosenVaultText => ChosenVault.Read(_home) is { } chosen
        ? IsThisVaultChosen
            ? $"Agents and the keypaste CLI use this vault, {chosen}, when no --vault is given."
            : $"Agents and the keypaste CLI use {chosen} when no --vault is given, not this vault."
        : "No vault is chosen for agents and the keypaste CLI, so each needs --vault.";

    internal bool IsThisVaultChosen => ChosenVault.Same(ChosenVault.Read(_home), _session.VaultPath);

#pragma warning disable CA1822
    internal string ChosenVaultRule =>
        "An AI tool that is already running keeps the vault it started with until it restarts keypaste mcp. " +
        "An AI tool connected with --vault keeps that vault.";
#pragma warning restore CA1822

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
    internal bool IsCliLinking
    {
        get => _cliLinking;
        private set => Set(ref _cliLinking, value);
    }

    /// <summary>Whether the confirmation that names what Replace moves is showing.</summary>
    internal bool IsConfirmingCliReplace
    {
        get => _confirmingCliReplace;
        private set => Set(ref _confirmingCliReplace, value);
    }

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
    /// The desktop's counterpart to the line the CLI prints when it creates the directory (D-0260): a
    /// folder of encrypted vaults appearing beside somebody's file should not be a discovery.
    /// </remarks>
    internal string BackupsPath =>
        _session.VaultPath is { } path ? VaultBackups.DirectoryFor(path) : "none";

    /// <summary>How many copies are there, and how old the newest is. Read each time, never cached.</summary>
    internal string BackupsSummary => _session.VaultPath is { } path
        ? VaultBackups.List(path) switch
        {
            [] => "No copies yet. The first save after an unlock makes one.",
            [var only] => $"1 copy, from {When(only)}.",
            [var newest, ..] all => $"{all.Count} copies, the newest from {When(newest)}.",
        }
        : string.Empty;

#pragma warning disable CA1822
    internal string BackupsRule =>
        $"A save keeps the file it replaces: the first save after each unlock, no more often than every " +
        $"fifteen minutes, keeping the last {VaultBackups.Retained}. Each copy opens with the master " +
        "password it was made under, and none of them recovers a forgotten one.";

    internal string RestoreHint =>
        "To put one back, lock keypaste and choose Restore a backup on the unlock screen.";

    internal string ElsewhereAdvice =>
        "Copies beside the vault do not survive losing the disk or the folder. Export an encrypted copy " +
        "and keep it on another disk. It opens with this vault's master password.";
#pragma warning restore CA1822

    /// <summary>Confirmation of the last thing that happened, or nothing.</summary>
    internal string Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    internal bool HasMessage => _message.Length > 0;

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
        ExportCommand.RaiseCanExecuteChanged();

        try
        {
            // A vault unlocked while the picker was open is not the one the person chose to copy.
            var write = await Task.Run(() => _session.Write(
                vault =>
                {
                    if (!string.Equals(vault.Path, path, StringComparison.Ordinal))
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
                WriteOutcome.NothingToSave or WriteOutcome.Locked => "The vault locked before the copy was made. Nothing was written.",
                WriteOutcome.ChangedOnDisk => "The vault file changed since it was unlocked. Reload to see it, then export.",
                _ => write.Reason!,
            };
        }
        finally
        {
            _exporting = false;
            ExportCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Zeroes whatever the access form holds; the shell calls this on lock and on leaving.</summary>
    public void Dispose() => Access.Dispose();

    private static string When(VaultBackup backup) =>
        backup.TakenAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

    private void UseThisVault()
    {
        if (_session.VaultPath is not { } path)
        {
            return;
        }

        Message = ChosenVault.Choose(_home, path, onlyIfNone: false) switch
        {
            ChooseOutcome.Chosen => "Agents and the keypaste CLI now use this vault when no --vault is given.",
            ChooseOutcome.Unreadable => $"{KeypasteHome.SettingsPath(_home)} could not be read, so it was left as it is.",
            ChooseOutcome.Unrecordable => "This vault's path holds a quote, a control character or a backslash, which app.toml cannot record. Move or rename the vault.",
            _ => $"{KeypasteHome.SettingsPath(_home)} could not be written, so nothing changed.",
        };

        Raise(nameof(IsThisVaultChosen));
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
            Raise(nameof(CliStatus));
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
        { Refusal: CliLinkRefusal.UnsafeProgram } => $"keypaste could not link {link.LinkPath}: this app's path holds a line break.",
        _ => $"keypaste could not link {link.LinkPath}: {result.Reason}",
    };

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

        Message = _preferences.Update(settings)
            ? string.Empty
            : "That preference could not be saved, so it will not survive a restart.";
    }

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
