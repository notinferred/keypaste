using System.Globalization;
using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Recommendations;

namespace Keypaste.App.ViewModels;

/// <summary>The password findings of one problem, which their heading explains once.</summary>
/// <param name="Heading">The problem.</param>
/// <param name="Reason">Why it is one.</param>
/// <param name="Rows">Each item with it.</param>
internal sealed record PasswordGroup(string Heading, string Reason, IReadOnlyList<PasswordRow> Rows);

/// <summary>Items › Recommendations: keys left in notes (C.2, D-0372), project tags another app dropped (V.11) and weak, reused or expired passwords (V.9), for review.</summary>
/// <remarks>
/// <para>
/// Owned by the shell for one unlock, because the Settings screen is rebuilt on every visit. It is
/// checked when the shell is built, which is on unlock, and again after each save. Nothing here is
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
    private readonly string _dismissalsPath;
    private readonly bool _dismissalsReadable;
    private readonly NoteKeyCheck _check = new();

    private readonly List<Dismissal> _dismissals;
    private IReadOnlyList<NoteKeyFinding> _noteKeys = [];
    private IReadOnlyList<LostProjectTag> _lost = [];
    private IReadOnlyList<PasswordFinding> _weakReusedExpired = [];
    private SameNameTells? _tells;
    private bool _disposed;
    private int _generation;

    /// <param name="session">The session whose vault is checked.</param>
    /// <param name="home">The keypaste home dismissals are kept in.</param>
    /// <param name="open">Shows an item in Items.</param>
    /// <param name="post">Runs an action on the UI thread; null shows a check's findings on the context that asked for it, or, with none, checks on the asking thread.</param>
    internal RecommendationsViewModel(AppVaultSession session, string? home, Action<EntryName>? open = null, Action<Action>? post = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        _open = open;
        _post = post;
        _dismissalsPath = KeypasteHome.RecommendationsPath(home);
        _dismissalsReadable = RecommendationDismissals.TryLoad(_dismissalsPath, out var dismissals);
        _dismissals = [.. dismissals];

        MoveSelectedCommand = new RelayCommand(() => Move([.. Rows.Where(row => row.IsSelected && !row.IsDismissed)]));

        Check();
    }

    /// <summary>Every finding: those needing review first, then those dismissed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsReview), nameof(IsEmpty), nameof(HasNoteKeys), nameof(Line))]
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
            var dismissed = Rows.Count + LostTags.Count + Passwords.Count + (HasFragileKeyFile ? 1 : 0) - NeedsReview;

            return NeedsReview > 0
                ? string.Create(CultureInfo.InvariantCulture, $"Recommendations: {NeedsReview} to review")
                : string.Create(CultureInfo.InvariantCulture, $"Recommendations: nothing to review, {dismissed} dismissed");
        }
    }

    /// <summary>How many findings still need review; the Settings row shows this.</summary>
    internal int NeedsReview =>
        Rows.Count(row => !row.IsDismissed) + LostTags.Count(row => !row.IsDismissed) + Passwords.Count(row => !row.IsDismissed)
        + (HasFragileKeyFile && !FragileKeyFileDismissed ? 1 : 0);

    internal bool IsEmpty => Rows.Count == 0 && LostTags.Count == 0 && Passwords.Count == 0 && !HasFragileKeyFile;

    internal bool HasNoteKeys => Rows.Count > 0;

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

    /// <summary>The latest check, complete once its findings are shown, or handed to the UI thread, or dropped.</summary>
    internal Task Checking { get; private set; } = Task.CompletedTask;

    /// <summary>Checks the open vault's notes, tags and passwords again, off the UI thread.</summary>
    internal void Check()
    {
        var generation = ++_generation;

        if (_disposed || _session.Unlocked is not { } vault)
        {
            _noteKeys = [];
            _lost = [];
            _weakReusedExpired = [];
            _tells = null;
            Show();
            Checking = Task.CompletedTask;
            return;
        }

        // With no UI thread to come back to, a finished scan would be shown on a pool thread while its owner goes on.
        if (_post is null && SynchronizationContext.Current is null)
        {
            Show(generation, vault, Task.FromResult(Scan(_check, vault, _session.Clock.GetUtcNow())));
            Checking = Task.CompletedTask;
            return;
        }

        Checking = CheckAsync(generation, vault, _session.Clock.GetUtcNow());
    }

    private async Task CheckAsync(int generation, Vault vault, DateTimeOffset now)
    {
        var check = _check;
        var scan = Task.Run(() => Scan(check, vault, now));
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
    private static Findings? Scan(NoteKeyCheck check, Vault vault, DateTimeOffset now)
    {
        try
        {
            return new Findings(check.Scan(vault), LostProjectTagCheck.Scan(vault), PasswordHealthCheck.Scan(vault, now), SameNameTells.Of(vault.ReadEntries()));
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
        _tells = found.Tells;
        Show();
    }

    /// <summary>Shows the last check's findings, each dismissed or not, without reading the vault again.</summary>
    private void Show()
    {
        if (_disposed || _session.Unlocked is null)
        {
            Rows = [];
            LostTags = [];
            Passwords = [];
            FragileKeyFile = null;
            return;
        }

        var vaultKey = VaultKey();
        var keyfile = _session.Unlocked?.KeyfilePath;
        FragileKeyFile = keyfile is not null && VaultKeyfile.Inspect(keyfile).IsFragile ? Path.GetFileName(keyfile) : null;
        FragileKeyFileDismissed = FragileKeyFile is not null && _dismissals.Contains(FragileKeyFileDismissal(vaultKey));
        var rows = _noteKeys
            .Select(finding => new RecommendationRow(this, finding, IsDismissed(vaultKey, finding)))
            .OrderBy(row => row.IsDismissed)
            .ThenBy(row => row.Entry, StringComparer.Ordinal)
            .ThenBy(row => row.Finding.Line)
            .ToList();

        Rows = rows;
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
    }

    /// <summary>Shows what putting a lost tag back does, before it is written (D-0415).</summary>
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

    /// <summary>Moves the given findings into protected fields and saves.</summary>
    internal void Move(IReadOnlyList<RecommendationRow> rows)
    {
        if (rows.Count == 0)
        {
            Message = "Choose a key to move first.";
            return;
        }

        var write = _session.Write(vault => vault.MoveNoteKeys(_check, [.. rows.Select(row => row.Finding)]), result => result.Moved);

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

        Message = Moved(rows.Count, move.Entries);
        Check();
    }

    /// <summary>Shows the item a password finding names in Items, found again by its UUID so a rename since the check still finds it.</summary>
    internal void Open(PasswordRow row)
    {
        if (_session.Unlocked is not { } vault)
        {
            Message = "The vault is locked. Unlock it, then try again.";
            return;
        }

        if (vault.EntryNameOf(row.Finding.EntryUuid) is not { } name)
        {
            Message = $"{row.Entry} is no longer in this vault. Check the list, then try again.";
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

    internal void Dismiss(RecommendationRow row) =>
        Dismiss(new Dismissal(VaultKey(), row.Finding.EntryUuid, RecommendationDismissals.NoteKey, row.Key));

    internal void ReviewAgain(RecommendationRow row) =>
        ReviewAgain(new Dismissal(VaultKey(), row.Finding.EntryUuid, RecommendationDismissals.NoteKey, row.Key));

    internal void Dismiss(LostTagRow row) =>
        Dismiss(new Dismissal(VaultKey(), row.Finding.EntryUuid, RecommendationDismissals.LostProjectTag, row.Finding.Key));

    internal void ReviewAgain(LostTagRow row) =>
        ReviewAgain(new Dismissal(VaultKey(), row.Finding.EntryUuid, RecommendationDismissals.LostProjectTag, row.Finding.Key));

    internal void Dismiss(PasswordRow row) =>
        Dismiss(new Dismissal(VaultKey(), row.Finding.EntryUuid, RecommendationDismissals.Password, row.Finding.Key));

    internal void ReviewAgain(PasswordRow row) =>
        ReviewAgain(new Dismissal(VaultKey(), row.Finding.EntryUuid, RecommendationDismissals.Password, row.Finding.Key));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _generation++;
        Rows = [];
        LostTags = [];
        Passwords = [];
        _check.Dispose();
    }

    private void Dismiss(Dismissal dismissal)
    {
        if (!_dismissals.Contains(dismissal))
        {
            _dismissals.Add(dismissal);
        }

        Message = Remember();
        Show();
    }

    private void ReviewAgain(Dismissal dismissal)
    {
        _dismissals.Remove(dismissal);
        Message = Remember();
        Show();
    }

    [RelayCommand]
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

    private string VaultKey() => _session.Identity?.Key ?? string.Empty;

    private string Moved(int keys, IReadOnlyList<EntryName> entries)
    {
        var what = keys == 1 ? "1 key" : keys.ToString(CultureInfo.InvariantCulture) + " keys";

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

    /// <summary>A finding's item as a sentence names it, which two of one name need to read apart.</summary>
    internal string Named(EntryName entry) => _tells?.Named(entry) ?? Shown(entry);

    internal static string Shown(EntryName entry) =>
        EntryNameSanitizer.SanitizePath(entry.GroupPath.Length == 0 ? entry.Title : entry.GroupPath + "/" + entry.Title).Text;

    private sealed record Findings(IReadOnlyList<NoteKeyFinding> NoteKeys, IReadOnlyList<LostProjectTag> Lost, IReadOnlyList<PasswordFinding> Passwords, SameNameTells Tells);
}
