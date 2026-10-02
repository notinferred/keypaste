using System.Globalization;
using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Audit;
using Keypaste.Core.Recommendations;

namespace Keypaste.App.ViewModels;

/// <summary>Settings › Recommendations: keys left in notes, for review (C.2, D-0372).</summary>
/// <remarks>
/// <para>
/// Owned by the shell for one unlock, because the Settings screen is rebuilt on every visit. It is
/// checked when the shell is built, which is on unlock, and again after each save. Nothing here is
/// shown anywhere else: no banner, no toast, only a quiet count on the Settings row.
/// </para>
/// <para>
/// A row names its entry and key, never the value. Moving goes through
/// <see cref="Vault.MoveNoteKeys"/>, which reads the values again under the vault's lock and
/// refuses once the notes have changed since this check.
/// </para>
/// </remarks>
internal sealed class RecommendationsViewModel : ObservableObject, IDisposable
{
    private readonly AppVaultSession _session;
    private readonly string _dismissalsPath;
    private readonly bool _dismissalsReadable;
    private readonly NoteKeyCheck _check = new();

    private readonly List<Dismissal> _dismissals;
    private IReadOnlyList<RecommendationRow> _rows = [];
    private string _message = string.Empty;
    private bool _disposed;

    internal RecommendationsViewModel(AppVaultSession session, string? home)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        _dismissalsPath = KeypasteHome.RecommendationsPath(home);
        _dismissalsReadable = RecommendationDismissals.TryLoad(_dismissalsPath, out var dismissals);
        _dismissals = [.. dismissals];

        MoveSelectedCommand = new RelayCommand(() => Move([.. _rows.Where(row => row.IsSelected && !row.IsDismissed)]));
        SelectAllCommand = new RelayCommand(SelectAll);

        Check();
    }

    /// <summary>Every finding: those needing review first, then those dismissed.</summary>
    internal IReadOnlyList<RecommendationRow> Rows
    {
        get => _rows;
        private set
        {
            if (Set(ref _rows, value))
            {
                Raise(nameof(NeedsReview));
                Raise(nameof(IsEmpty));
            }
        }
    }

    /// <summary>How many findings still need review; the Settings row shows this.</summary>
    internal int NeedsReview => _rows.Count(row => !row.IsDismissed);

    internal bool IsEmpty => _rows.Count == 0;

    /// <summary>What the last act did or why it was refused, or empty.</summary>
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

    internal RelayCommand MoveSelectedCommand { get; }

    internal RelayCommand SelectAllCommand { get; }

    /// <summary>Checks the open vault's notes again.</summary>
    internal void Check()
    {
        if (_disposed || _session.Unlocked is not { } vault)
        {
            Rows = [];
            return;
        }

        var vaultKey = VaultKey();
        var rows = _check.Scan(vault)
            .Select(finding => new RecommendationRow(this, finding, IsDismissed(vaultKey, finding)))
            .OrderBy(row => row.IsDismissed)
            .ThenBy(row => row.Entry, StringComparer.Ordinal)
            .ThenBy(row => row.Finding.Line)
            .ToList();

        Rows = rows;
    }

    /// <summary>Moves the given findings into protected fields and saves.</summary>
    internal void Move(IReadOnlyList<RecommendationRow> rows)
    {
        if (rows.Count == 0)
        {
            Message = "Choose a key to move first.";
            return;
        }

        if (_session.Unlocked is not { } vault)
        {
            Message = "The vault is locked.";
            return;
        }

        NoteKeyMove move;

        try
        {
            move = vault.MoveNoteKeys(_check, [.. rows.Select(row => row.Finding)]);

            if (!move.Moved)
            {
                Message = Refused(move.Refusals);

                if (move.Refusals.Any(refusal => refusal.Reason == NoteKeyRefusalReason.NotesChanged))
                {
                    Check();
                }

                return;
            }

            vault.Save();
        }
        catch (VaultChangedOnDiskException)
        {
            Message = "Something else changed this vault since you opened it. Reload to see it, then move the keys again.";
            return;
        }
        catch (VaultException e)
        {
            Message = e.Message;
            return;
        }

        Message = Moved(rows.Count, move.Entries);
        Check();
    }

    internal void Dismiss(RecommendationRow row)
    {
        var dismissal = new Dismissal(VaultKey(), row.Finding.EntryUuid, RecommendationDismissals.NoteKey, row.Key);

        if (!_dismissals.Contains(dismissal))
        {
            _dismissals.Add(dismissal);
        }

        Message = Remember();
        Check();
    }

    internal void ReviewAgain(RecommendationRow row)
    {
        _dismissals.Remove(new Dismissal(VaultKey(), row.Finding.EntryUuid, RecommendationDismissals.NoteKey, row.Key));
        Message = Remember();
        Check();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Rows = [];
        _check.Dispose();
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

    private string VaultKey() => _session.Identity?.Key ?? string.Empty;

    private static string Moved(int keys, IReadOnlyList<EntryName> entries)
    {
        var what = keys == 1 ? "1 key" : keys.ToString(CultureInfo.InvariantCulture) + " keys";

        return entries.Count == 1
            ? $"Moved {what} from the notes of {Shown(entries[0])} into protected fields. The notes as they were stay in its history."
            : $"Moved {what} from the notes of {entries.Count} entries into protected fields. The notes as they were stay in each entry's history.";
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
}
