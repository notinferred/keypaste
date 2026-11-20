using System.Globalization;
using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Recommendations;
using Keypaste.Core.Settings;

namespace Keypaste.App.ViewModels;

/// <summary>The password findings of one problem, which their heading explains once.</summary>
/// <param name="Heading">The problem.</param>
/// <param name="Reason">Why it is one.</param>
/// <param name="Rows">Each item with it.</param>
internal sealed record PasswordGroup(string Heading, string Reason, IReadOnlyList<PasswordRow> Rows);

/// <summary>Items › Recommendations: keys left in notes (C.2), project tags another app dropped (V.11), weak, reused or expired passwords (V.9), and items expiring soon or marked as needing an update, for review.</summary>
/// <remarks>
/// <para>
/// Owned by the shell for one unlock, because the Settings screen is rebuilt on every visit. It is
/// checked when the shell is built, which is on unlock, again after each save, and when an item's
/// expiry passes or comes within the reminder days. Nothing here is
/// shown anywhere else: no banner, no toast, only a quiet count on its sidebar row.
/// </para>
/// <para>
/// A check reads every live entry under the vault's lock and rates each password against a
/// 10,000-word list, so it runs on the thread pool and its findings are shown on the UI thread once
/// it finishes, unless a later check, a lock or <see cref="Dispose"/> came first. Until then the
/// list shows the findings it had.
/// </para>
/// <para>
/// A row names its entry and key or problem, never the value. Moving goes through
/// <see cref="Vault.MoveNoteKeys"/>, which reads the values again under the vault's lock and
/// refuses once the notes have changed since this check.
/// </para>
/// </remarks>
internal sealed partial class RecommendationsViewModel : ObservableObject, IDisposable
{
    private readonly AppVaultSession _session;
    private readonly Action<EntryName>? _open;
    private readonly Action<Action>? _post;
    private readonly DesktopPreferences? _preferences;
    private readonly IWebLauncher? _web;
    private readonly string _dismissalsPath;
    private readonly bool _dismissalsReadable;
    private readonly NoteKeyCheck _check = new();

    // Bounds how late a recheck comes after the computer sleeps, since the timer runs on the monotonic clock.
    private static readonly TimeSpan _longestRecheck = TimeSpan.FromHours(1);

    private readonly List<Dismissal> _dismissals;
    private IReadOnlyList<NoteKeyFinding> _noteKeys = [];
    private IReadOnlyList<LostProjectTag> _lost = [];
    private IReadOnlyList<PasswordFinding> _weakReusedExpired = [];
    private IReadOnlyList<ItemReminder> _reminders = [];
    private int _expiryDays;
    private SameNameTells? _tells;
    private bool _disposed;
    private int _generation;
    private ITimer? _recheck;

    /// <param name="session">The session whose vault is checked.</param>
    /// <param name="home">The keypaste home dismissals are kept in.</param>
    /// <param name="open">Shows an item in Items.</param>
    /// <param name="post">Runs an action on the UI thread; null shows a check's findings on the context that asked for it, or, with none, checks on the asking thread.</param>
    /// <param name="preferences">Where the days to remind before an expiry are set; null reminds at <see cref="AppSettings.DefaultExpiryReminderDays"/>.</param>
    /// <param name="web">Opens a reminder's page in the browser.</param>
    internal RecommendationsViewModel(
        AppVaultSession session,
        string? home,
        Action<EntryName>? open = null,
        Action<Action>? post = null,
        DesktopPreferences? preferences = null,
        IWebLauncher? web = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        _open = open;
        _post = post;
        _preferences = preferences;
        _web = web;
        _expiryDays = ExpiryDays;
        _dismissalsPath = KeypasteHome.RecommendationsPath(home);
        _dismissalsReadable = RecommendationDismissals.TryLoad(_dismissalsPath, out var dismissals);
        _dismissals = [.. dismissals];

        MoveSelectedCommand = new RelayCommand(() => Move([.. Rows.Where(row => row.IsSelected && !row.IsDismissed)]));
        SelectAllCommand = new RelayCommand(SelectAll);

        if (_preferences is not null)
        {
            _preferences.Changed += OnPreferencesChanged;
        }

        _session.ConflictedCopies.Changed += OnConflictedCopiesChanged;
        Check();
    }

    /// <summary>The session whose vault is checked.</summary>
    internal AppVaultSession Session => _session;

    /// <summary>The filter Security offers with several vaults open: every vault, then each; none where one is open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OffersVaultChoice), nameof(ShownVault))]
    internal partial IReadOnlyList<VaultChoice> VaultChoices { get; set; } = [];

    /// <summary>Whether Security starts with its vault filter.</summary>
    internal bool OffersVaultChoice => VaultChoices.Count > 2;

    /// <summary>Whether this check is drawn under another vault's, which heads the page, as every vault's are.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsPageHeader))]
    internal partial bool IsListedUnderAnother { get; set; }

    internal bool ShowsPageHeader => !IsListedUnderAnother;

    /// <summary>The filter's choice: every vault, while others are drawn under this one, or this vault alone.</summary>
    internal VaultChoice? ShownVault
    {
        get => VaultChoices.FirstOrDefault(choice => AlsoShown.Count > 0 ? choice.Vault is null : ReferenceEquals(choice.Vault, _session));
        set
        {
            if (value is not null && !Equals(value, ShownVault))
            {
                ShowVault?.Invoke(value);
            }
        }
    }

    /// <summary>Shows Security for another choice of the filter, set by the shell.</summary>
    internal Action<VaultChoice>? ShowVault { get; set; }

    /// <summary>The vault's title over its findings while every vault's are listed, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsVaultHeading))]
    internal partial string VaultHeading { get; set; } = string.Empty;

    internal bool ShowsVaultHeading => VaultHeading.Length > 0;

    /// <summary>The other vaults' checks drawn under this one while the filter shows every vault.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownVault))]
    internal partial IReadOnlyList<RecommendationsViewModel> AlsoShown { get; set; } = [];

    /// <summary>The keys left in notes, one row for each item's: those needing review first, then those dismissed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsReview), nameof(IsEmpty), nameof(HasNoteKeys), nameof(OffersBulkMove), nameof(Line))]
    internal partial IReadOnlyList<RecommendationRow> Rows { get; private set; } = [];

    /// <summary>Every project tag an entry lost in another app: those needing review first, then those dismissed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsReview), nameof(IsEmpty), nameof(HasLostTags), nameof(Line))]
    internal partial IReadOnlyList<LostTagRow> LostTags { get; private set; } = [];

    /// <summary>Every weak, reused or expired password: those needing review first, then those dismissed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsReview), nameof(IsEmpty), nameof(HasPasswords), nameof(Line), nameof(PasswordGroups))]
    internal partial IReadOnlyList<PasswordRow> Passwords { get; private set; } = [];

    /// <summary>The passwords by problem, in the order the problems are declared.</summary>
    internal IReadOnlyList<PasswordGroup> PasswordGroups =>
    [
        .. Passwords
            .GroupBy(row => row.Finding.Problem)
            .OrderBy(group => group.Key)
            .Select(group => new PasswordGroup(PasswordRow.Heading(group.Key), PasswordRow.Reason(group.Key), [.. group])),
    ];

    /// <summary>Every item expiring soon or marked as needing an update: those needing review first, then those dismissed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsReview), nameof(IsEmpty), nameof(HasReminders), nameof(Line), nameof(ReminderGroups))]
    internal partial IReadOnlyList<ReminderRow> Reminders { get; private set; } = [];

    /// <summary>The reminders by reason, in the order the reasons are declared.</summary>
    internal IReadOnlyList<ReminderGroup> ReminderGroups =>
    [
        .. Reminders
            .GroupBy(row => row.Finding.Reason)
            .OrderBy(group => group.Key)
            .Select(group => new ReminderGroup(ReminderRow.Heading(group.Key), ReminderRow.Reason(group.Key, _expiryDays), [.. group])),
    ];

    internal bool HasReminders => Reminders.Count > 0;

    /// <summary>Sync tools' conflicted copies beside the vault, each to review and merge.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsReview), nameof(IsEmpty), nameof(HasConflictedCopies), nameof(Line))]
    internal partial IReadOnlyList<ConflictedCopyRow> ConflictedCopies { get; private set; } = [];

    internal bool HasConflictedCopies => ConflictedCopies.Count > 0;

    /// <summary>Shows the conflicted copies the vault's owner last found, keeping the rows of those still there.</summary>
    internal void ShowConflictedCopies()
    {
        var found = _disposed || _session.Unlocked is null ? [] : _session.ConflictedCopies.Found;

        if (!found.SequenceEqual(ConflictedCopies.Select(row => row.Path), StringComparer.Ordinal))
        {
            ConflictedCopies = [.. found.Select(path => ConflictedCopies.FirstOrDefault(row => row.Path == path) ?? new ConflictedCopyRow(this, path))];
        }
    }

    private void OnConflictedCopiesChanged(object? sender, EventArgs e) => Post(ShowConflictedCopies);

    /// <summary>Works out what merging a copy would change, which writes nothing.</summary>
    internal async Task ReviewAsync(ConflictedCopyRow row)
    {
        if (await RunMerge(row, save: false).ConfigureAwait(true) is not { Report: { } report })
        {
            return;
        }

        List<string> names = [.. report.Taken.Take(20).Select(ApprovalPrompt.Shown)];
        var more = report.Taken.Count > names.Count ? string.Create(CultureInfo.InvariantCulture, $", and {report.Taken.Count - names.Count} more") : string.Empty;
        row.Review = report.Taken.Count == 0
            ? "This copy has nothing your vault doesn't. Merging only moves it in with the backups."
            : string.Create(CultureInfo.InvariantCulture, $"{report.Taken.Count} from this copy will come in: {string.Join(", ", names)}{more}. Anything that replaces a value keeps the other in that item's history.");
    }

    /// <summary>Merges a copy into the vault's file and moves the copy in with the backups.</summary>
    internal async Task MergeAsync(ConflictedCopyRow row)
    {
        if (await RunMerge(row, save: true).ConfigureAwait(true) is { Outcome: VaultMergeSaveOutcome.Saved })
        {
            row.Review = null;
            Message = $"Merged {row.Name} and moved it in with this vault's backups.";

            if (_session.Unlocked is { } vault)
            {
                await Task.Run(() => _session.ConflictedCopies.Look(vault)).ConfigureAwait(true);
            }

            ShowConflictedCopies();
        }
    }

    private async Task<VaultMergeSave?> RunMerge(ConflictedCopyRow row, bool save)
    {
        if (_session.Unlocked is not { } vault)
        {
            return null;
        }

        row.Busy = true;
        row.Message = null;

        try
        {
            var result = await Task.Run(() => vault.MergeConflictedCopy(row.Path, save)).ConfigureAwait(true);

            if (result.Outcome is VaultMergeSaveOutcome.Saved or VaultMergeSaveOutcome.Unchanged)
            {
                return result;
            }

            row.Message = result.Outcome == VaultMergeSaveOutcome.KeyRefused
                ? "This copy doesn't open with the password and key file you unlocked with. Open it in KeePassXC to merge it."
                : $"This copy can't be merged: {MergeNotice.Refused(new MergeResult(result.Outcome, result.Reason, null, [], false, DateTimeOffset.MinValue))}.";
            return null;
        }
        finally
        {
            row.Busy = false;
        }
    }

    /// <summary>The vault's key file, when it is one keyed by its exact bytes (T-28).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsReview), nameof(IsEmpty), nameof(HasFragileKeyFile), nameof(FragileKeyFileWarning), nameof(Line))]
    internal partial string? FragileKeyFile { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsReview), nameof(Line), nameof(FragileKeyFileNeedsReview))]
    internal partial bool FragileKeyFileDismissed { get; private set; }

    internal bool HasFragileKeyFile => FragileKeyFile is not null;

    internal bool FragileKeyFileNeedsReview => !FragileKeyFileDismissed;

    internal string FragileKeyFileWarning => UseText.FragileKeyFile(FragileKeyFile ?? string.Empty);

    /// <summary>The one line that sums up what is listed; whatever was dismissed stays reachable from it.</summary>
    internal string Line
    {
        get
        {
            var dismissed = Rows.Count + LostTags.Count + Passwords.Count + Reminders.Count + (HasFragileKeyFile ? 1 : 0) + ConflictedCopies.Count - NeedsReview;

            return NeedsReview > 0
                ? string.Create(CultureInfo.InvariantCulture, $"Recommendations: {NeedsReview} to review")
                : string.Create(CultureInfo.InvariantCulture, $"Recommendations: nothing to review, {dismissed} dismissed");
        }
    }

    /// <summary>How many findings still need review; the Settings row shows this.</summary>
    internal int NeedsReview =>
        Rows.Count(row => !row.IsDismissed) + LostTags.Count(row => !row.IsDismissed) + Passwords.Count(row => !row.IsDismissed)
        + Reminders.Count(row => !row.IsDismissed) + (HasFragileKeyFile && !FragileKeyFileDismissed ? 1 : 0) + ConflictedCopies.Count;

    internal bool IsEmpty => Rows.Count == 0 && LostTags.Count == 0 && Passwords.Count == 0 && Reminders.Count == 0 && !HasFragileKeyFile && ConflictedCopies.Count == 0;

    internal bool HasNoteKeys => Rows.Count > 0;

    /// <summary>Whether Select all and Move selected are offered: only while two or more items' keys need review.</summary>
    internal bool OffersBulkMove => Rows.Count(row => row.NeedsReview) > 1;

    /// <summary>Whether Move selected has anything to move.</summary>
    internal bool HasSelectedKeys => Rows.Any(row => row.IsSelected && !row.IsDismissed);

    internal bool HasLostTags => LostTags.Count > 0;

    internal bool HasPasswords => Passwords.Count > 0;

    /// <summary>What the last act did or why it was refused, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    internal partial string Message { get; private set; } = string.Empty;

    internal bool HasMessage => Message.Length > 0;

    internal RelayCommand MoveSelectedCommand { get; }

    internal RelayCommand SelectAllCommand { get; }

    /// <summary>The latest check, complete once its findings are shown, or handed to the UI thread, or dropped.</summary>
    internal Task Checking { get; private set; } = Task.CompletedTask;

    /// <summary>Checks the open vault's notes, tags, passwords and expiries again, off the UI thread.</summary>
    internal void Check()
    {
        var generation = ++_generation;
        _expiryDays = ExpiryDays;
        _recheck?.Dispose();
        _recheck = null;

        if (_disposed || _session.Unlocked is not { } vault)
        {
            _noteKeys = [];
            _lost = [];
            _weakReusedExpired = [];
            _reminders = [];
            _tells = null;
            Show();
            Checking = Task.CompletedTask;
            return;
        }

        // With no UI thread to come back to, a finished scan would be shown on a pool thread while its owner goes on.
        if (_post is null && SynchronizationContext.Current is null)
        {
            Show(generation, vault, Task.FromResult(Scan(_check, vault, _session.Clock.GetUtcNow(), _expiryDays)));
            Checking = Task.CompletedTask;
            return;
        }

        Checking = CheckAsync(generation, vault, _session.Clock.GetUtcNow(), _expiryDays);
    }

    private async Task CheckAsync(int generation, Vault vault, DateTimeOffset now, int expiryDays)
    {
        var check = _check;
        var scan = Task.Run(() => Scan(check, vault, now, expiryDays));
        await ((Task)scan).ConfigureAwait(_post is null
            ? ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing
            : ConfigureAwaitOptions.SuppressThrowing);

        if (_post is { } post)
        {
            post(() => Show(generation, vault, scan));
        }
        else
        {
            Show(generation, vault, scan);
        }
    }

    /// <summary>Every finding, or null when a lock disposed the vault, or <see cref="Dispose"/> the check, mid-scan.</summary>
    private static Findings? Scan(NoteKeyCheck check, Vault vault, DateTimeOffset now, int expiryDays)
    {
        try
        {
            return new Findings(
                check.Scan(vault),
                LostProjectTagCheck.Scan(vault),
                PasswordHealthCheck.Scan(vault, now),
                ItemReminderCheck.Scan(vault, now, expiryDays),
                SameNameTells.Of(vault.ReadEntries()),
                ItemReminderCheck.NextChange(vault, now, expiryDays));
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>Shows a finished scan's findings, unless a later check, a lock or <see cref="Dispose"/> came first.</summary>
    private void Show(int generation, Vault vault, Task<Findings?> scan)
    {
        if (generation != _generation || _disposed || !ReferenceEquals(_session.Unlocked, vault))
        {
            return;
        }

        // A scan that failed fails here, on the UI thread, as a check run there did.
        if (scan.GetAwaiter().GetResult() is not { } found)
        {
            return;
        }

        _noteKeys = found.NoteKeys;
        _lost = found.Lost;
        _weakReusedExpired = found.Passwords;
        _reminders = found.Reminders;
        _tells = found.Tells;
        Show();

        if (found.NextChange is { } next)
        {
            var due = next - _session.Clock.GetUtcNow();
            due = due < TimeSpan.Zero ? TimeSpan.Zero : due > _longestRecheck ? _longestRecheck : due;
            _recheck = _session.Clock.CreateTimer(_ => Post(Check), null, due, Timeout.InfiniteTimeSpan);
        }
    }

    private void Post(Action action)
    {
        if (_post is { } post)
        {
            post(action);
        }
        else
        {
            action();
        }
    }

    /// <summary>Shows the last check's findings, each dismissed or not, without reading the vault again.</summary>
    private void Show()
    {
        if (_disposed || _session.Unlocked is null)
        {
            Rows = [];
            LostTags = [];
            Passwords = [];
            Reminders = [];
            FragileKeyFile = null;
            ConflictedCopies = [];
            return;
        }

        var vaultKey = VaultKey();
        var keyfile = _session.Unlocked?.KeyfilePath;
        FragileKeyFile = keyfile is not null && VaultKeyfile.Inspect(keyfile).IsFragile ? Path.GetFileName(keyfile) : null;
        FragileKeyFileDismissed = FragileKeyFile is not null && _dismissals.Contains(FragileKeyFileDismissal(vaultKey));
        ShowConflictedCopies();
        Rows = [.. _noteKeys
            .GroupBy(finding => (finding.EntryUuid, finding.Entry, Dismissed: IsDismissed(vaultKey, finding)))
            .Select(item => new RecommendationRow(this, [.. item.OrderBy(finding => finding.Line)], item.Key.Dismissed))
            .OrderBy(row => row.IsDismissed)
            .ThenBy(row => row.Entry, StringComparer.Ordinal)];
        LostTags = [.. _lost
            .Select(finding => new LostTagRow(this, finding, IsDismissed(vaultKey, finding)))
            .OrderBy(row => row.IsDismissed)
            .ThenBy(row => row.Entry, StringComparer.Ordinal)
            .ThenBy(row => row.Tag, StringComparer.Ordinal)];
        Passwords = [.. _weakReusedExpired
            .Select(finding => new PasswordRow(this, finding, IsDismissed(vaultKey, finding)))
            .OrderBy(row => row.IsDismissed)
            .ThenBy(row => row.Entry, StringComparer.Ordinal)
            .ThenBy(row => row.Finding.Problem)];
        Reminders = [.. _reminders
            .Select(finding => new ReminderRow(this, finding, IsDismissed(vaultKey, finding)))
            .OrderBy(row => row.IsDismissed)
            .ThenBy(row => row.Finding.When ?? DateTimeOffset.MaxValue)
            .ThenBy(row => row.Entry, StringComparer.Ordinal)];
    }

    /// <summary>Shows what putting a lost tag back does, before it is written.</summary>
    internal void AskRestore(LostTagRow row)
    {
        if (_session.Unlocked is not { } vault)
        {
            Message = "The vault is locked. Unlock it, then try again.";
            return;
        }

        try
        {
            row.Confirmation = ProjectTagChange.Preview(vault, row.Finding.Entry, [row.Finding.Tag], adding: true)?.Describe() ?? [];
            Message = string.Empty;
        }
        catch (VaultException e)
        {
            Message = e.Message;
        }
    }

    /// <summary>Puts a lost tag back on its entry, in one revision, and saves.</summary>
    internal void Restore(LostTagRow row)
    {
        row.Confirmation = [];

        var write = _session.Write(vault => vault.AddTag(row.Finding.Entry, row.Finding.Tag), added => added);

        if (write.Problem("restore the tag again") is { } problem)
        {
            Message = problem;
            return;
        }

        Message = write.Outcome == WriteOutcome.NothingToSave
            ? $"{row.Entry} already has {row.Tag}."
            : $"Put {row.Tag} back on {row.Entry}. The item as it was stays in its history.";
        Check();
    }

    /// <summary>Moves the keys ticked on the given items into protected fields on each item, and saves.</summary>
    internal void Move(IReadOnlyList<RecommendationRow> rows)
    {
        List<NoteKeyFinding> findings = [.. rows.SelectMany(row => row.Included)];

        if (findings.Count == 0)
        {
            Message = "Choose a key to move first.";
            return;
        }

        Write(vault => vault.MoveNoteKeys(_check, findings), findings.Count, stage: null);
    }

    /// <summary>Opens the choice of a project stage for an item's ticked keys, every stage of every project listed.</summary>
    internal void AskMoveToStage(RecommendationRow row)
    {
        if (_session.Unlocked is not { } vault)
        {
            Message = "The vault is locked. Unlock it, then try again.";
            return;
        }

        List<ProjectMembership> stages = [.. ProjectCatalog.Read(vault).Projects
            .SelectMany(project => project.Environments.Select(stage => new ProjectMembership(project.Name, stage.Name)))];

        if (stages.Count == 0)
        {
            Message = Resources.Strings.NoProjectYetCreateOneInEnvironments;
            return;
        }

        Message = string.Empty;
        row.Stages = stages;
        row.ChosenStage = stages[0];
    }

    /// <summary>What moving an item's ticked keys into its chosen stage does: the stage it joins and every variable it brings, the item's own included.</summary>
    internal string StagePreview(RecommendationRow row)
    {
        if (row.ChosenStage is not { } stage || _session.Unlocked is not { } vault || !row.HasIncludedKeys)
        {
            return string.Empty;
        }

        var listing = ProjectCatalog.Read(vault).Projects.FirstOrDefault(project => project.Name == stage.Project)?.Environments.FirstOrDefault(environment => environment.Name == stage.Environment);
        var variables = (vault.Fields(row.Keys[0].Finding.Entry) ?? [])
            .Select(field => field.Name)
            .Where(EnvConvention.IsEnvNamedField)
            .Concat(row.Included.Select(finding => finding.Field))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(name => EntryNameSanitizer.Sanitize(name).Text);
        var joins = UseText.JoinsStage(row.Entry, Shown(stage), string.Join(", ", variables));

        return listing?.IsProtected == true ? joins + " " + Resources.Strings.AgentsAreAskedEveryTimeForAProtectedStage : joins;
    }

    /// <summary>Moves an item's ticked keys into fields on it and puts it in the chosen stage, in one revision, and saves.</summary>
    internal void MoveToStage(RecommendationRow row)
    {
        if (row.ChosenStage is not { } stage || row.Included is not { Count: > 0 } findings)
        {
            return;
        }

        row.Stages = [];

        try
        {
            Write(vault => vault.MoveNoteKeysInto(_check, findings, stage.Project, stage.Environment), findings.Count, stage);
        }
        catch (VaultException e)
        {
            Message = e.Message;
        }
    }

    private static string Shown(ProjectMembership stage) => EntryNameSanitizer.SanitizePath(stage.ToString()).Text;

    private void Write(Func<Vault, NoteKeyMove> act, int keys, ProjectMembership? stage)
    {
        var write = _session.Write(act, result => result.Moved);

        if (write.Problem("move the keys again") is { } problem)
        {
            Message = problem;
            return;
        }

        var move = write.Value!;

        if (!move.Moved)
        {
            Message = Refused(move.Refusals);

            if (move.Refusals.Any(refusal => refusal.Reason == NoteKeyRefusalReason.NotesChanged))
            {
                Check();
            }

            return;
        }

        Message = Moved(keys, move.Entries, stage);
        Check();
    }

    /// <summary>Shows the item a password finding names in Items, found again by its UUID so a rename since the check still finds it.</summary>
    internal void Open(PasswordRow row) => Open(row.Finding.EntryUuid, row.Entry);

    /// <summary>Shows the item a reminder names in Items, found again by its UUID.</summary>
    internal void Open(ReminderRow row) => Open(row.Finding.EntryUuid, row.Entry);

    /// <summary>Opens a reminder's page in the browser, which makes the only request; nothing changes at the provider or in the vault.</summary>
    internal async Task OpenPageAsync(ReminderRow row)
    {
        if (row.Finding.Page is not { } page)
        {
            return;
        }

        Message = string.Empty;

#pragma warning disable RS0030 // The reminder's page, which the person asked to open.
        if (_web is null || !await _web.OpenAsync(page.Address).ConfigureAwait(true))
#pragma warning restore RS0030
        {
            Message = $"Your system didn't open {row.PageAddress}. Copy it and paste it into your browser.";
        }
    }

    private void Open(string entryUuid, string shown)
    {
        if (_session.Unlocked is not { } vault)
        {
            Message = "The vault is locked. Unlock it, then try again.";
            return;
        }

        if (vault.EntryNameOf(entryUuid) is not { } name)
        {
            Message = $"{shown} is no longer in this vault. Check the list, then try again.";
            return;
        }

        Message = string.Empty;
        _open?.Invoke(name);
    }

    [RelayCommand]
    private void DismissFragileKeyFile() => Dismiss(FragileKeyFileDismissal(VaultKey()));

    [RelayCommand]
    private void ReviewFragileKeyFileAgain() => ReviewAgain(FragileKeyFileDismissal(VaultKey()));

    private Dismissal FragileKeyFileDismissal(string vaultKey) =>
        new(vaultKey, RecommendationDismissals.FragileKeyFile, RecommendationDismissals.FragileKeyFile, FragileKeyFile ?? string.Empty);

    /// <summary>Shows the item whose notes hold the keys in Items, found again by its UUID.</summary>
    internal void Open(RecommendationRow row) => Open(row.EntryUuid, row.Entry);

    /// <summary>Shows the item that lost the tag in Items, found again by its UUID.</summary>
    internal void Open(LostTagRow row) => Open(row.Finding.EntryUuid, row.Entry);

    internal void Dismiss(RecommendationRow row) =>
        Dismiss([.. row.Keys.Select(key => new Dismissal(VaultKey(), row.EntryUuid, RecommendationDismissals.NoteKey, key.Key))]);

    internal void ReviewAgain(RecommendationRow row) =>
        ReviewAgain([.. row.Keys.Select(key => new Dismissal(VaultKey(), row.EntryUuid, RecommendationDismissals.NoteKey, key.Key))]);

    internal void Dismiss(LostTagRow row) =>
        Dismiss(new Dismissal(VaultKey(), row.Finding.EntryUuid, RecommendationDismissals.LostProjectTag, row.Finding.Key));

    internal void ReviewAgain(LostTagRow row) =>
        ReviewAgain(new Dismissal(VaultKey(), row.Finding.EntryUuid, RecommendationDismissals.LostProjectTag, row.Finding.Key));

    internal void Dismiss(PasswordRow row) =>
        Dismiss(new Dismissal(VaultKey(), row.Finding.EntryUuid, RecommendationDismissals.Password, row.Finding.Key));

    internal void ReviewAgain(PasswordRow row) =>
        ReviewAgain(new Dismissal(VaultKey(), row.Finding.EntryUuid, RecommendationDismissals.Password, row.Finding.Key));

    internal void Dismiss(ReminderRow row) =>
        Dismiss(new Dismissal(VaultKey(), row.Finding.EntryUuid, RecommendationDismissals.Reminder, row.Finding.Key));

    internal void ReviewAgain(ReminderRow row) =>
        ReviewAgain(new Dismissal(VaultKey(), row.Finding.EntryUuid, RecommendationDismissals.Reminder, row.Finding.Key));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _generation++;

        if (_preferences is not null)
        {
            _preferences.Changed -= OnPreferencesChanged;
        }

        _session.ConflictedCopies.Changed -= OnConflictedCopiesChanged;
        Rows = [];
        LostTags = [];
        Passwords = [];
        Reminders = [];
        _recheck?.Dispose();
        _check.Dispose();
    }

    private void Dismiss(params IReadOnlyList<Dismissal> dismissals)
    {
        _dismissals.AddRange(dismissals.Where(dismissal => !_dismissals.Contains(dismissal)).Distinct());
        Message = Remember();
        Show();
    }

    private void ReviewAgain(params IReadOnlyList<Dismissal> dismissals)
    {
        _dismissals.RemoveAll(dismissals.Contains);
        Message = Remember();
        Show();
    }

    private void SelectAll()
    {
        var select = Rows.Any(row => !row.IsDismissed && !row.IsSelected);

        foreach (var row in Rows.Where(row => !row.IsDismissed))
        {
            row.IsSelected = select;
        }
    }

    /// <summary>Writes the dismissals, and says so when they will not outlive this unlock.</summary>
    private string Remember()
    {
        if (!_dismissalsReadable)
        {
            return "Your earlier dismissals could not be read, so this change lasts until you lock. Check that keypaste's folder can be read.";
        }

        return RecommendationDismissals.Save(_dismissalsPath, _dismissals)
            ? string.Empty
            : "This change could not be saved on this machine, so it lasts until you lock. Check that keypaste's folder can be written.";
    }

    private bool IsDismissed(string vaultKey, NoteKeyFinding finding) =>
        _dismissals.Contains(new Dismissal(vaultKey, finding.EntryUuid, RecommendationDismissals.NoteKey, finding.Field));

    private bool IsDismissed(string vaultKey, LostProjectTag finding) =>
        _dismissals.Contains(new Dismissal(vaultKey, finding.EntryUuid, RecommendationDismissals.LostProjectTag, finding.Key));

    private bool IsDismissed(string vaultKey, PasswordFinding finding) =>
        _dismissals.Contains(new Dismissal(vaultKey, finding.EntryUuid, RecommendationDismissals.Password, finding.Key));

    private bool IsDismissed(string vaultKey, ItemReminder finding) =>
        _dismissals.Contains(new Dismissal(vaultKey, finding.EntryUuid, RecommendationDismissals.Reminder, finding.Key));

    private int ExpiryDays => _preferences?.Current.ExpiryReminderDays ?? AppSettings.DefaultExpiryReminderDays;

    private void OnPreferencesChanged(object? sender, EventArgs e)
    {
        if (ExpiryDays != _expiryDays)
        {
            Check();
        }
    }

    private string VaultKey() => _session.Identity?.Key ?? string.Empty;

    private string Moved(int keys, IReadOnlyList<EntryName> entries, ProjectMembership? stage)
    {
        var what = keys == 1 ? "1 key" : keys.ToString(CultureInfo.InvariantCulture) + " keys";

        if (stage is not null)
        {
            return UseText.MovedIntoStage(what, Named(entries[0]), Shown(stage));
        }

        return entries.Count == 1
            ? $"Moved {what} from the notes of {Named(entries[0])} into protected fields. The notes as they were stay in its history."
            : $"Moved {what} from the notes of {entries.Count} items into protected fields. The notes as they were stay in each item's history.";
    }

    private string Refused(IReadOnlyList<NoteKeyRefusal> refusals)
    {
        var refusal = refusals[0];
        var entry = Named(refusal.Finding.Entry);
        var key = refusal.Finding.Field;

        return refusal.Reason switch
        {
            NoteKeyRefusalReason.NotesChanged => $"The notes of {entry} changed after they were checked, so nothing was moved. They have been checked again.",
            NoteKeyRefusalReason.FieldHoldsOtherValue => $"{entry} already has a {key} field with a different value, so nothing was moved. Remove or rename that field, or dismiss this key.",
            NoteKeyRefusalReason.KeyRepeated => $"The notes of {entry} set {key} on more than one line, so nothing was moved. Edit the notes to keep one.",
            _ => $"{entry} is no longer in this vault under that name, so nothing was moved. Check the list, then try again.",
        };
    }

    /// <summary>A finding's item by its title, as a sentence names it, with the tell two items of one name need to read apart.</summary>
    internal string Named(EntryName entry) => SameNameTells.WithTell(EntryNameSanitizer.Sanitize(entry.Title).Text, _tells?.Of(entry));

    internal static string Shown(EntryName entry) =>
        EntryNameSanitizer.SanitizePath(entry.GroupPath.Length == 0 ? entry.Title : entry.GroupPath + "/" + entry.Title).Text;

    private sealed record Findings(
        IReadOnlyList<NoteKeyFinding> NoteKeys,
        IReadOnlyList<LostProjectTag> Lost,
        IReadOnlyList<PasswordFinding> Passwords,
        IReadOnlyList<ItemReminder> Reminders,
        SameNameTells Tells,
        DateTimeOffset? NextChange);
}
