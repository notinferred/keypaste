using System.Globalization;
using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Recommendations;

namespace Keypaste.App.ViewModels;

/// <summary>Settings › Recommendations: keys left in notes (C.2, D-0372), project tags another app dropped (V.11) and weak, reused or expired passwords (V.9), for review.</summary>
/// <remarks>
/// <para>
/// Owned by the shell for one unlock, because the Settings screen is rebuilt on every visit. It is
/// checked when the shell is built, which is on unlock, and again after each save. Nothing here is
/// shown anywhere else: no banner, no toast, only a quiet count on the Settings row.
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
internal sealed class RecommendationsViewModel : ObservableObject, IDisposable
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
    private IReadOnlyList<RecommendationRow> _rows = [];
    private IReadOnlyList<LostTagRow> _lostTags = [];
    private IReadOnlyList<PasswordRow> _passwords = [];
    private string _message = string.Empty;
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

        MoveSelectedCommand = new RelayCommand(() => Move([.. _rows.Where(row => row.IsSelected && !row.IsDismissed)]));
        SelectAllCommand = new RelayCommand(SelectAll);

        DependsOn(nameof(NeedsReview), nameof(Rows), nameof(LostTags), nameof(Passwords));
        DependsOn(nameof(IsEmpty), nameof(Rows), nameof(LostTags), nameof(Passwords));
        DependsOn(nameof(HasNoteKeys), nameof(Rows));
        DependsOn(nameof(HasLostTags), nameof(LostTags));
        DependsOn(nameof(HasPasswords), nameof(Passwords));
        DependsOn(nameof(HasMessage), nameof(Message));

        Check();
    }

    /// <summary>Every finding: those needing review first, then those dismissed.</summary>
    internal IReadOnlyList<RecommendationRow> Rows
    {
        get => _rows;
        private set => Set(ref _rows, value);
    }

    /// <summary>Every project tag an entry lost in another app: those needing review first, then those dismissed.</summary>
    internal IReadOnlyList<LostTagRow> LostTags
    {
        get => _lostTags;
        private set => Set(ref _lostTags, value);
    }

    /// <summary>Every weak, reused or expired password: those needing review first, then those dismissed.</summary>
    internal IReadOnlyList<PasswordRow> Passwords
    {
        get => _passwords;
        private set => Set(ref _passwords, value);
    }

    /// <summary>How many findings still need review; the Settings row shows this.</summary>
    internal int NeedsReview =>
        _rows.Count(row => !row.IsDismissed) + _lostTags.Count(row => !row.IsDismissed) + _passwords.Count(row => !row.IsDismissed);

    internal bool IsEmpty => _rows.Count == 0 && _lostTags.Count == 0 && _passwords.Count == 0;

    internal bool HasNoteKeys => _rows.Count > 0;

    internal bool HasLostTags => _lostTags.Count > 0;

    internal bool HasPasswords => _passwords.Count > 0;

    /// <summary>What the last act did or why it was refused, or empty.</summary>
    internal string Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    internal bool HasMessage => _message.Length > 0;

    internal RelayCommand MoveSelectedCommand { get; }

    internal RelayCommand SelectAllCommand { get; }

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
            return new Findings(check.Scan(vault), LostProjectTagCheck.Scan(vault), PasswordHealthCheck.Scan(vault, now));
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
            return;
        }

        var vaultKey = VaultKey();
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
            Message = "The vault is locked.";
            return;
        }

        try
        {
            row.Confirmation = _session.PreviewTagChange(vault, row.Finding.Entry, [row.Finding.Tag], adding: true)?.Describe() ?? [];
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
            Message = "The vault is locked.";
            return;
        }

        if (vault.EntryNameOf(row.Finding.EntryUuid) is not { } name)
        {
            Message = $"{row.Entry} is no longer in this vault.";
            return;
        }

        Message = string.Empty;
        _open?.Invoke(name);
    }

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

    private void SelectAll()
    {
        var select = _rows.Any(row => !row.IsDismissed && !row.IsSelected);

        foreach (var row in _rows.Where(row => !row.IsDismissed))
        {
            row.IsSelected = select;
        }
    }

    /// <summary>Writes the dismissals, and says so when they will not outlive this unlock.</summary>
    private string Remember()
    {
        if (!_dismissalsReadable)
        {
            return "Your earlier dismissals could not be read, so this change lasts until you lock.";
        }

        return RecommendationDismissals.Save(_dismissalsPath, _dismissals)
            ? string.Empty
            : "This change could not be saved on this machine, so it lasts until you lock.";
    }

    private bool IsDismissed(string vaultKey, NoteKeyFinding finding) =>
        _dismissals.Contains(new Dismissal(vaultKey, finding.EntryUuid, RecommendationDismissals.NoteKey, finding.Field));

    private bool IsDismissed(string vaultKey, LostProjectTag finding) =>
        _dismissals.Contains(new Dismissal(vaultKey, finding.EntryUuid, RecommendationDismissals.LostProjectTag, finding.Key));

    private bool IsDismissed(string vaultKey, PasswordFinding finding) =>
        _dismissals.Contains(new Dismissal(vaultKey, finding.EntryUuid, RecommendationDismissals.Password, finding.Key));

    private string VaultKey() => _session.Identity?.Key ?? string.Empty;

    private static string Moved(int keys, IReadOnlyList<EntryName> entries)
    {
        var what = keys == 1 ? "1 key" : keys.ToString(CultureInfo.InvariantCulture) + " keys";

        return entries.Count == 1
            ? $"Moved {what} from the notes of {Shown(entries[0])} into protected fields. The notes as they were stay in its history."
            : $"Moved {what} from the notes of {entries.Count} items into protected fields. The notes as they were stay in each item's history.";
    }

    private static string Refused(IReadOnlyList<NoteKeyRefusal> refusals)
    {
        var refusal = refusals[0];
        var entry = Shown(refusal.Finding.Entry);
        var key = refusal.Finding.Field;

        return refusal.Reason switch
        {
            NoteKeyRefusalReason.NotesChanged => $"The notes of {entry} changed after they were checked, so nothing was moved. They have been checked again.",
            NoteKeyRefusalReason.FieldHoldsOtherValue => $"{entry} already has a {key} field with a different value, so nothing was moved. Remove or rename that field, or dismiss this key.",
            NoteKeyRefusalReason.KeyRepeated => $"The notes of {entry} set {key} on more than one line, so nothing was moved. Edit the notes to keep one.",
            _ => $"{entry} is no longer in this vault under that name, so nothing was moved.",
        };
    }

    internal static string Shown(EntryName entry) =>
        EntryNameSanitizer.SanitizePath(entry.GroupPath.Length == 0 ? entry.Title : entry.GroupPath + "/" + entry.Title).Text;

    private sealed record Findings(IReadOnlyList<NoteKeyFinding> NoteKeys, IReadOnlyList<LostProjectTag> Lost, IReadOnlyList<PasswordFinding> Passwords);
}
