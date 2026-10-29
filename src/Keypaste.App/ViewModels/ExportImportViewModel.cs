using System.Collections.ObjectModel;
using System.Globalization;
using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Import;

namespace Keypaste.App.ViewModels;

/// <summary>
/// The export import dialog: another password manager's export, read, previewed against the open vault with where each
/// item lands and everything that does not, copied in all or nothing, and then what was imported and that the file is
/// to be deleted (D-0591, D-0592).
/// </summary>
/// <remarks>
/// <para>
/// The file goes through the same <see cref="ExportImport"/> and <see cref="ExportSource"/> as <c>keypaste import</c>, so
/// both front ends read, place and copy an export the same way. It is read once, off the UI thread, and its items are
/// held only by the <see cref="ExportSource"/>, which goes, its attachments zeroed, on cancel, once imported and on lock.
/// Nothing here writes, moves or deletes the file.
/// </para>
/// <para>
/// Nothing here names a value: rows carry the export's item names and where they land, and what does not land is named
/// by its part and why (D-0565).
/// </para>
/// </remarks>
internal sealed class ExportImportViewModel : ObservableObject, IDisposable
{
    private readonly AppVaultSession _session;
    private readonly string _path;
    private readonly IReadOnlyList<IExportReader> _readers;
    private readonly Action<string> _announce;
    private readonly Func<Task>? _chooseAnother;
    private readonly Action<Action>? _post;

    private Task? _reading;
    private ExportSource? _source;
    private ExportPlan? _plan;
    private ExportDialogState _state = ExportDialogState.Reading;
    private string _format = string.Empty;
    private int _itemCount;
    private int _imported;
    private string _into = string.Empty;
    private bool _skipHeld;
    private string _message = string.Empty;

    /// <summary>Waits for <see cref="ReadAsync"/> to read the file.</summary>
    /// <param name="session">The session whose vault the export is copied into.</param>
    /// <param name="path">The export.</param>
    /// <param name="announce">Says what was imported, once the dialog closes on it.</param>
    /// <param name="readers">The formats read; null for every one keypaste reads.</param>
    /// <param name="chooseAnother">Asks for another file when this one cannot be read; null where nothing can ask.</param>
    /// <param name="post">Runs an action on the UI thread, where a lock, which may run on another, closes the dialog; null closes it where the lock runs.</param>
    internal ExportImportViewModel(
        AppVaultSession session,
        string path,
        Action<string> announce,
        IReadOnlyList<IExportReader>? readers = null,
        Func<Task>? chooseAnother = null,
        Action<Action>? post = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(announce);

        _session = session;
        _path = System.IO.Path.GetFullPath(path);
        _readers = readers ?? ExportImport.Readers;
        _announce = announce;
        _chooseAnother = chooseAnother;
        _post = post;

        ConfirmCommand = new RelayCommand(Confirm, () => CanConfirm);
        CancelCommand = new RelayCommand(Dispose);
        ChooseAnotherCommand = new AsyncRelayCommand(() => _chooseAnother!(), () => CanChooseAnother);

        DependsOn(nameof(IsReading), nameof(State));
        DependsOn(nameof(IsUnreadable), nameof(State));
        DependsOn(nameof(IsPreviewing), nameof(State));
        DependsOn(nameof(IsImported), nameof(State));
        DependsOn(nameof(CanChooseAnother), nameof(State));
        DependsOn(nameof(CardDetail), nameof(State), nameof(Format), nameof(ItemCount));
        DependsOn(nameof(HasMessage), nameof(Message));
        DependsOn(nameof(HasTrailingMessage), nameof(HasMessage), nameof(State));
        DependsOn(nameof(HeldCount), nameof(Plan));
        DependsOn(nameof(HasHeld), nameof(HeldCount), nameof(State));
        DependsOn(nameof(HeldLabel), nameof(HeldCount));
        DependsOn(nameof(KeepHeld), nameof(SkipHeld));
        DependsOn(nameof(NotImportedLabel), nameof(Plan));
        DependsOn(nameof(HasNotImported), nameof(Plan), nameof(State));
        DependsOn(nameof(ConfirmText), nameof(Plan));
        DependsOn(nameof(ImportedText), nameof(Imported));
        DependsOn(nameof(CanConfirm), nameof(State), nameof(Plan));
        DependsOn(ConfirmCommand, nameof(CanConfirm));
        DependsOn(ChooseAnotherCommand, nameof(CanChooseAnother));

        _session.Locked += OnLocked;
    }

    /// <summary>Raised once, when the dialog is done: cancelled, closed after an import or dropped by a lock.</summary>
    internal event EventHandler? Closed;

    /// <summary>The export's file name.</summary>
    internal string FileName => Show(System.IO.Path.GetFileName(_path));

    /// <summary>Where the export is on disk, so the person can find it to delete it.</summary>
    internal string FilePath => DisplayTextSanitizer.Sanitize(_path).Text;

    internal bool IsReading => _state == ExportDialogState.Reading;

    /// <summary>Whether the file was refused: the card says why, and only another file or Cancel is offered.</summary>
    internal bool IsUnreadable => _state == ExportDialogState.Unreadable;

    /// <summary>Whether the preview shows: the file is read and nothing is imported yet.</summary>
    internal bool IsPreviewing => _state == ExportDialogState.Previewing;

    /// <summary>Whether the import is done and the dialog says what it did.</summary>
    internal bool IsImported => _state == ExportDialogState.Imported;

    internal bool CanChooseAnother => IsUnreadable && _chooseAnother is not null;

    /// <summary>Asks for another file in place of one that cannot be read.</summary>
    internal AsyncRelayCommand ChooseAnotherCommand { get; }

    /// <summary>The format, as its reader names it, such as <c>Bitwarden JSON</c>.</summary>
    internal string Format
    {
        get => _format;
        private set => Set(ref _format, value);
    }

    /// <summary>How many items the export holds.</summary>
    internal int ItemCount
    {
        get => _itemCount;
        private set => Set(ref _itemCount, value);
    }

    /// <summary>The file card's second line.</summary>
    internal string CardDetail => _state switch
    {
        ExportDialogState.Reading => "Reading…",
        ExportDialogState.Previewing or ExportDialogState.Imported => $"{Format} · {Items(ItemCount)}",
        _ => string.Empty,
    };

    /// <summary>The group everything lands under; changing it lays every row out again.</summary>
    internal string Into
    {
        get => _into;
        set
        {
            if (Set(ref _into, value ?? string.Empty) && IsPreviewing)
            {
                Lay(_into);
            }
        }
    }

    /// <summary>One row per item of the export: where it lands, or that it is skipped, and why.</summary>
    internal ObservableCollection<ExportPreviewRow> Rows { get; } = [];

    /// <summary>Everything that does not land, each with why: skipped items, parts of items and records that are no item.</summary>
    internal ObservableCollection<ExportLossRow> NotImported { get; } = [];

    /// <summary>How many items have a name the vault already has.</summary>
    internal int HeldCount => _plan?.Rows.Count(row => row.Collision == ExportCollision.VaultItem) ?? 0;

    /// <summary>Whether the choice between keeping both and skipping shows.</summary>
    internal bool HasHeld => IsPreviewing && HeldCount > 0;

    internal string HeldLabel => HeldCount == 1
        ? "1 name your vault already has"
        : string.Create(CultureInfo.InvariantCulture, $"{HeldCount} names your vault already has");

    /// <summary>Whether an item whose name the vault already has lands beside it, the default (D-0566).</summary>
    internal bool KeepHeld
    {
        get => !_skipHeld;
        set => SkipHeld = !value;
    }

    /// <summary>
    /// Whether an item whose name the vault already has is skipped, as <c>keypaste import --skip-existing</c> skips it. An
    /// item the export holds twice is kept beside the other either way, since neither copy is in the vault yet.
    /// </summary>
    internal bool SkipHeld
    {
        get => _skipHeld;
        set
        {
            if (Set(ref _skipHeld, value) && IsPreviewing)
            {
                Lay(_into);
            }
        }
    }

    internal string NotImportedLabel => string.Create(CultureInfo.InvariantCulture, $"Not imported ({NotImported.Count})");

    internal bool HasNotImported => (IsPreviewing || IsImported) && NotImported.Count > 0;

    /// <summary>What stops the import, or why it failed, or empty.</summary>
    internal string Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    internal bool HasMessage => _message.Length > 0;

    /// <summary>Whether the message shows below the dialog's body rather than on the card, which says why a file is refused.</summary>
    internal bool HasTrailingMessage => HasMessage && !IsUnreadable;

    /// <summary><c>Import 142 items</c>, counting the items that land.</summary>
    internal string ConfirmText => $"Import {Items(_plan?.Landing ?? 0)}";

    /// <summary>False while the plan is refused, nothing lands or the vault is locked.</summary>
    internal bool CanConfirm => IsPreviewing && _plan is { Refusal: null, Landing: > 0 } && _session.IsUnlocked;

    internal RelayCommand ConfirmCommand { get; }

    /// <summary>Closes the dialog: before an import, nothing is copied; after it, the dialog says what it did as it goes.</summary>
    internal RelayCommand CancelCommand { get; }

    /// <summary>How many items the import copied.</summary>
    internal int Imported
    {
        get => _imported;
        private set => Set(ref _imported, value);
    }

    internal string ImportedText => $"Imported {Items(Imported)} into {Show(_into)}";

    /// <summary>The export while it is held. A test seam for when its attachments go.</summary>
    internal ExportSource? Source => _source;

    private ExportDialogState State
    {
        get => _state;
        set => Set(ref _state, value);
    }

    private ExportPlan? Plan
    {
        get => _plan;
        set => Set(ref _plan, value);
    }

    /// <summary>The formats as one list a sentence can end on: <c>A, B or C</c>.</summary>
    internal static string Either(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        return names.Count <= 1 ? string.Concat(names) : $"{string.Join(", ", names.Take(names.Count - 1))} or {names[^1]}";
    }

    /// <summary>Reads the file off the UI thread and lays out its preview, once however often it is asked.</summary>
    internal Task ReadAsync() => _reading ??= ReadOnceAsync();

    /// <summary>Drops the export and whatever was shown of it; after an import, says what it copied.</summary>
    public void Dispose()
    {
        if (_state == ExportDialogState.Closed)
        {
            return;
        }

        var imported = IsImported;
        State = ExportDialogState.Closed;
        _session.Locked -= OnLocked;
        _source?.Dispose();
        _source = null;
        Plan = null;
        Rows.Clear();
        NotImported.Clear();

        if (imported && _session.IsUnlocked)
        {
            _announce($"Imported {Items(Imported)} from {FileName}");
        }

        Closed?.Invoke(this, EventArgs.Empty);
    }

    private async Task ReadOnceAsync()
    {
        ExportSource? opened;

        try
        {
            opened = await Task.Run(() => ExportImport.Open(_path, _readers)).ConfigureAwait(true);
        }
        catch (VaultException ex)
        {
            Refuse(Why(ex.Message));
            return;
        }

        if (!IsReading || !_session.IsUnlocked)
        {
            opened?.Dispose();
            return;
        }

        if (opened is null)
        {
            Refuse($"This file is not an export keypaste reads: {Either([.. _readers.Select(reader => reader.Format)])}.");
            return;
        }

        _source = opened;
        Format = Show(opened.Format);
        ItemCount = opened.Items.Count;
        State = ExportDialogState.Previewing;
        Lay(null);
    }

    private void Refuse(string why)
    {
        if (IsReading)
        {
            Message = why;
            State = ExportDialogState.Unreadable;
        }
    }

    /// <summary>Plans where every item lands under <paramref name="into"/>, or the file's name, and shows it.</summary>
    private void Lay(string? into)
    {
        if (_source is not { } source || _session.Unlocked is not { } vault)
        {
            return;
        }

        var plan = source.Plan(vault, into);

        if (_skipHeld)
        {
            plan = source.Plan(
                vault,
                plan.Into,
                plan.Rows.Where(row => row.Collision == ExportCollision.VaultItem).ToDictionary(row => row.Index, _ => CollisionDecision.Skip));
        }

        _into = plan.Into;
        Raise(nameof(Into));

        Rows.Clear();
        NotImported.Clear();

        foreach (var row in plan.Rows)
        {
            var note = row.Note is { } said ? Prose(said) : string.Empty;
            Rows.Add(new ExportPreviewRow(Show(row.Item), row.Lands is { } name ? Shown(name) : "skipped", row.Lands is null, note));

            if (row.Lands is null)
            {
                NotImported.Add(new ExportLossRow(Show(row.Item), $"skipped: {note}"));
            }
        }

        foreach (var loss in plan.Losses)
        {
            NotImported.Add(new ExportLossRow(loss.Index < 0 ? string.Empty : Show(loss.Item), $"{Prose(loss.Part)}: {Prose(loss.Reason)}"));
        }

        Message = plan.Refusal is { } refusal ? $"Nothing can be imported: {Prose(refusal)}." : string.Empty;
        Plan = plan;
    }

    private void Confirm()
    {
        if (!CanConfirm || _source is not { } source || _plan is not { } plan)
        {
            return;
        }

        var write = _session.WriteWhole((vault, save) => source.Commit(vault, plan, save));

        switch (write.Outcome)
        {
            case WriteOutcome.Locked:
                return;
            case WriteOutcome.Saved:
                _source = null;
                source.Dispose();
                Imported = write.Value!.Entries;
                Message = string.Empty;
                State = ExportDialogState.Imported;
                return;
            case WriteOutcome.ChangedOnDisk:
                Message = $"{write.Problem("import again")} Nothing was imported.";
                return;
            default:
                // Commit took back whatever it added, so the vault holds what it held before; a vault changed
                // since the preview is laid out again, for the person to look at before importing.
                var reason = Prose(write.Reason ?? string.Empty);
                Lay(_into);
                Message = reason.StartsWith("Nothing was imported", StringComparison.Ordinal) ? reason : $"{reason} Nothing was imported.";
                return;
        }
    }

    /// <summary>The import's refusal of this file, without the file name the card already shows.</summary>
    private string Why(string error)
    {
        var named = System.IO.Path.GetFileName(_path) + " ";
        var why = error.StartsWith(named, StringComparison.Ordinal) ? "This file " + error[named.Length..] : error;
        why = Prose(why);
        return why.EndsWith('.') ? why : why + ".";
    }

    private void OnLocked(object? sender, VaultLockReason reason)
    {
        if (_post is { } post)
        {
            post(Dispose);
        }
        else
        {
            Dispose();
        }
    }

    private static string Show(string name) => EntryNameSanitizer.SanitizePath(name).Text;

    private static string Shown(EntryName name) => Show(name.GroupPath.Length == 0 ? name.Title : $"{name.GroupPath}/{name.Title}");

    private static string Prose(string text) => DisplayTextSanitizer.Sanitize(text).Text;

    private static string Items(int count) =>
        count == 1 ? "1 item" : string.Create(CultureInfo.InvariantCulture, $"{count} items");
}

/// <summary>Where the export dialog is.</summary>
internal enum ExportDialogState
{
    /// <summary>The file is being read.</summary>
    Reading = 0,

    /// <summary>The file was refused, and the dialog says why.</summary>
    Unreadable = 1,

    /// <summary>The preview shows, and nothing is imported yet.</summary>
    Previewing = 2,

    /// <summary>The items are in the vault and saved, and the dialog says what it did.</summary>
    Imported = 3,

    /// <summary>The dialog is closed and holds nothing of the export.</summary>
    Closed = 4,
}

/// <summary>One item of the export in the preview, by name only.</summary>
/// <param name="Item">The item as the export names it, its folders and title joined with <c>/</c>.</param>
/// <param name="Lands">Where it lands in the vault, or <c>skipped</c>.</param>
/// <param name="Skipped">Whether it is skipped.</param>
/// <param name="Note">Why it lands under another name or not at all, or empty.</param>
internal sealed record ExportPreviewRow(string Item, string Lands, bool Skipped, string Note)
{
    internal bool HasNote => Note.Length > 0;
}

/// <summary>Something of the export that does not land, named with why.</summary>
/// <param name="Item">The item it belongs to, or empty for a record that is no item.</param>
/// <param name="What">The part and why, such as <c>field PIN: …</c>.</param>
internal sealed record ExportLossRow(string Item, string What)
{
    internal bool HasItem => Item.Length > 0;
}
