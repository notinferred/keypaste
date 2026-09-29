using System.Globalization;
using Keypaste.App.Navigation;
using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Recent;
using Keypaste.Core.Settings;

namespace Keypaste.App.ViewModels;

/// <summary>One offered idle timeout.</summary>
/// <param name="Seconds">The value written to <c>app.toml</c>.</param>
/// <param name="Label">How it reads in the list.</param>
internal sealed record IdleChoice(int Seconds, string Label);

/// <summary>
/// Settings, with Advanced's way to the screens the everyday ones leave out.
/// </summary>
/// <remarks>
/// Every change is written immediately and applied immediately — there is no Save button and no way
/// to leave the screen with a setting that looks changed but is not. Shortening the idle timeout
/// re-arms the session on the spot rather than at the next lock.
/// </remarks>
internal sealed class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly AppVaultSession _session;
    private readonly string? _home;
    private readonly DesktopPreferences _preferences;
    private readonly Action<AppTheme> _applyTheme;
    private readonly IVaultFilePicker? _picker;

    private AppSettings _settings;
    private IdleChoice _idle;
    private AppTheme _theme;
    private bool _lockWhenMinimized;
    private string _message = string.Empty;
    private bool _exporting;

    internal SettingsViewModel(
        AppVaultSession session,
        string? home,
        DesktopPreferences preferences,
        Action<AppTheme> applyTheme,
        IVaultFilePicker? picker = null,
        RecommendationsViewModel? recommendations = null,
        Action<DestinationKind>? open = null)
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

        IdleChoices = ChoicesFor(_settings.IdleTimeoutSeconds);
        _idle = IdleChoices.Single(choice => choice.Seconds == _settings.IdleTimeoutSeconds);

        ForgetAllCommand = new RelayCommand(ForgetAll);
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => !_exporting && _picker is not null);
        Access = new VaultAccessViewModel(session, home, picker);
        Recommendations = recommendations;
        OpenActivityLogCommand = new RelayCommand(() => open?.Invoke(DestinationKind.Log), () => open is not null);
        OpenShareLinksCommand = new RelayCommand(() => open?.Invoke(DestinationKind.Sharing), () => open is not null);
        OpenScopedTokensCommand = new RelayCommand(() => open?.Invoke(DestinationKind.Tokens), () => open is not null);
        OpenDiagnosticsCommand = new RelayCommand(() => open?.Invoke(DestinationKind.Diagnostics), () => open is not null);
        ShareLinksNote = ShareLinksNoteOf(session);
    }

    /// <summary>Settings › Advanced: the whole activity log, with its hash check.</summary>
    internal RelayCommand OpenActivityLogCommand { get; }

    /// <summary>Settings › Advanced: the share links made from this vault.</summary>
    internal RelayCommand OpenShareLinksCommand { get; }

    /// <summary>Settings › Advanced: the vault's scoped tokens, minting and revoking one.</summary>
    internal RelayCommand OpenScopedTokensCommand { get; }

    /// <summary>Settings › Advanced: the paths and version this app is using.</summary>
    internal RelayCommand OpenDiagnosticsCommand { get; }

    /// <summary>How many of this vault's links may still open, from their recorded expiry only.</summary>
    internal string ShareLinksNote { get; }

    private static string ShareLinksNoteOf(AppVaultSession session)
    {
        if (session.Unlocked is not { } vault)
        {
            return string.Empty;
        }

        var now = DateTimeOffset.UtcNow;
        var open = new Core.Sharing.ShareStore(vault).List().Count(share => share.Expires > now);

        return open switch
        {
            0 => "Links you make from an item's Share… are listed here. None may still open.",
            1 => "Links you make from an item's Share… are listed here. 1 may still open.",
            _ => string.Create(CultureInfo.InvariantCulture, $"Links you make from an item's Share… are listed here. {open} may still open."),
        };
    }

    /// <summary>Keys left in notes, owned by the shell for the unlock; null where no shell built this screen.</summary>
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

    /// <summary>Empties the recent-vaults list.</summary>
    internal RelayCommand ForgetAllCommand { get; }

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
        private set
        {
            if (Set(ref _message, value))
            {
                Raise(nameof(HasMessage));
            }
        }
    }

    internal bool HasMessage => _message.Length > 0;

    /// <summary>Asks where, then has the core write the copy.</summary>
    /// <remarks>
    /// <c>internal</c> for <see cref="UnlockViewModel.UnlockAsync"/>'s reason. The suggested name
    /// only saves typing; every refusal is <see cref="Vault.ExportTo"/>'s.
    /// </remarks>
    internal async Task ExportAsync()
    {
        if (_exporting || _picker is null || _session.Unlocked is not { } vault)
        {
            return;
        }

        var stem = Path.GetFileNameWithoutExtension(vault.Path);
        var today = _session.Clock.GetLocalNow().ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        if (await _picker.PickExportDestinationAsync($"{stem}-copy-{today}.kdbx").ConfigureAwait(true) is not { } destination)
        {
            return;
        }

        _exporting = true;
        ExportCommand.RaiseCanExecuteChanged();

        try
        {
            await Task.Run(() => vault.ExportTo(destination)).ConfigureAwait(true);
            Message = $"An encrypted copy is at {destination}. It opens with this vault's master password.";
        }
        catch (VaultChangedOnDiskException)
        {
            Message = "The vault file changed since it was unlocked. Lock and unlock to see it, then export.";
        }
        catch (VaultException e)
        {
            Message = e.Message;
        }
        catch (ObjectDisposedException)
        {
            Message = "The vault locked before the copy was made. Nothing was written.";
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
