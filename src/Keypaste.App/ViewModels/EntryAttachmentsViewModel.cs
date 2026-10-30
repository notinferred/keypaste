#pragma warning disable RS0030 // One of the item pane's two attachment files (D-0534).
using Keypaste.App.Session;
using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>An item's attachments in its pane: each one's name and size, with add, replace, remove and save (V.8b).</summary>
/// <remarks>
/// <para>
/// No attachment's content is in this object or passes through it. Adding and replacing hand the vault a stream over the
/// file the person chose, and saving hands it the path they chose, so the bytes go between that file and the vault inside
/// core (D-0533, D-0534). Each edit is one core call and one save, so one revision in the item's history.
/// </para>
/// <para>
/// Nothing here opens or runs an attachment or the file one was saved to, reaches a launcher or starts a process
/// (D-0534); the app's <c>BannedSymbols.txt</c> and <c>AttachmentSourceRulesTests</c> hold that.
/// </para>
/// </remarks>
internal sealed class EntryAttachmentsViewModel : ObservableObject, IDisposable
{
    /// <summary>Why an edit's revision is not in the item's history (D-0533).</summary>
    private const string _notKept = "the item as it was is larger than this vault's history size limit, so history doesn't keep it.";

    private readonly AppVaultSession _session;
    private readonly EntryName _item;
    private readonly IVaultFilePicker? _picker;
    private readonly Action<string?> _report;
    private readonly Action _changed;
    private IReadOnlyList<EntryAttachmentRow> _rows = [];
    private EntryAttachmentRow? _removing;
    private string? _note;
    private bool _picking;
    private bool _disposed;

    /// <param name="session">The session whose unlocked vault holds the item.</param>
    /// <param name="item">The item whose attachments these are.</param>
    /// <param name="picker">Where the person chooses a file, or null where there is no window to ask in.</param>
    /// <param name="report">Where a refusal goes: the screen's banner, cleared with null.</param>
    /// <param name="changed">Called once an edit is a revision, so a history read before it is read again (D-0229).</param>
    internal EntryAttachmentsViewModel(AppVaultSession session, EntryName item, IVaultFilePicker? picker, Action<string?> report, Action changed)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(changed);

        _session = session;
        _item = item;
        _picker = picker;
        _report = report;
        _changed = changed;

        AddCommand = new AsyncRelayCommand(AddAsync, () => CanPick);
        ConfirmRemoveCommand = new RelayCommand(ConfirmRemove, () => IsRemoving);
        CancelRemoveCommand = new RelayCommand(() => Removing = null, () => IsRemoving);

        DependsOn(nameof(HasRows), nameof(Rows));
        DependsOn(nameof(IsRemoving), nameof(Removing));
        DependsOn(nameof(RemovePrompt), nameof(Removing));
        DependsOn(nameof(HasNote), nameof(Note));
        DependsOn(ConfirmRemoveCommand, nameof(Removing));
        DependsOn(CancelRemoveCommand, nameof(Removing));

        Read();
    }

    /// <summary>The item's attachments, in ordinal order of name.</summary>
    internal IReadOnlyList<EntryAttachmentRow> Rows
    {
        get => _rows;
        private set => Set(ref _rows, value);
    }

    internal bool HasRows => _rows.Count > 0;

    /// <summary>Whether a file can be chosen here, which adding, replacing and saving all need.</summary>
    internal bool CanPick => _picker is not null;

    /// <summary>Asks for a file and attaches it under its own name.</summary>
    internal AsyncRelayCommand AddCommand { get; }

    /// <summary>The attachment the remove confirmation is for, or null.</summary>
    internal EntryAttachmentRow? Removing
    {
        get => _removing;
        private set => Set(ref _removing, value);
    }

    internal bool IsRemoving => _removing is not null;

    internal string RemovePrompt =>
        $"Remove {_removing?.DisplayName} from this item? History keeps it unless the item is larger than this vault's history size limit.";

    internal RelayCommand ConfirmRemoveCommand { get; }

    internal RelayCommand CancelRemoveCommand { get; }

    /// <summary>What the last save or edit leaves the person to know, such as that a saved copy is not encrypted; null otherwise.</summary>
    internal string? Note
    {
        get => _note;
        private set => Set(ref _note, value);
    }

    internal bool HasNote => _note is not null;

    /// <summary>Reads the item's attachments again, by name and size, closing a confirmation for one no longer there.</summary>
    internal void Read()
    {
        if (_disposed || _session.Unlocked is not { } vault)
        {
            return;
        }

        try
        {
            Rows = [.. (vault.Attachments(_item) ?? []).Select(attachment => new EntryAttachmentRow(this, attachment))];
        }
        catch (VaultException e)
        {
            _report(e.Message);
            return;
        }

        if (_removing is { } removing && !_rows.Any(row => string.Equals(row.Name, removing.Name, StringComparison.Ordinal)))
        {
            Removing = null;
        }
    }

    /// <summary>Asks whether to remove one attachment.</summary>
    internal void BeginRemove(EntryAttachmentRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        Note = null;
        Removing = row;
        _report(null);
    }

    /// <summary>Asks for another file and puts what it holds in place of what <paramref name="row"/> holds, keeping its name.</summary>
    internal async Task ReplaceAsync(EntryAttachmentRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (await PickAsync(picker => picker.PickAttachmentAsync()).ConfigureAwait(true) is { } path)
        {
            Attach(path, row.Name, replacing: true);
        }
    }

    /// <summary>Asks where to save a copy of <paramref name="row"/>, and has the vault write it there only where nothing is (D-0534).</summary>
    internal async Task SaveAsync(EntryAttachmentRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (await PickAsync(picker => picker.PickAttachmentDestinationAsync(row.SuggestedFileName)).ConfigureAwait(true) is not { } picked)
        {
            return;
        }

        if (_session.Unlocked is not { } vault)
        {
            _report("The vault is locked.");
            return;
        }

        var path = Path.GetFullPath(picked);
        AttachmentSaveOutcome outcome;
        string failure;

        try
        {
            outcome = vault.SaveAttachment(_item, row.Name, path, out failure);
        }
        catch (VaultException e)
        {
            _report(e.Message);
            return;
        }
        catch (ObjectDisposedException)
        {
            _report("The vault is locked.");
            return;
        }

        switch (outcome)
        {
            case AttachmentSaveOutcome.Saved:
                var readers = OperatingSystem.IsWindows() ? "it has its folder's permissions" : "only you can read it";
                Note = $"Saved {row.DisplayName} to {Shown(path)}. The file isn't encrypted, and {readers}. Delete it when you're done: keypaste can't take it back.";
                break;

            case AttachmentSaveOutcome.PathAlreadyExists:
                _report("There's already a file there, and keypaste won't replace it. Save again and choose a name that isn't taken.");
                break;

            case AttachmentSaveOutcome.NotFound:
                _report($"{row.DisplayName} is no longer on this item.");
                Read();
                break;

            default:
                _report($"keypaste couldn't save {row.DisplayName} there: {Shown(failure)} Choose another folder.");
                break;
        }
    }

    /// <summary>Lets go of what was read from the vault, on a new selection and on the lock.</summary>
    public void Dispose()
    {
        _disposed = true;
        Rows = [];
        Removing = null;
        Note = null;
    }

    private async Task AddAsync()
    {
        if (await PickAsync(picker => picker.PickAttachmentAsync()).ConfigureAwait(true) is { } path)
        {
            Attach(path, Path.GetFileName(path), replacing: false);
        }
    }

    /// <summary>Opens one picker at a time, and answers null for one cancelled or answered after the vault locked.</summary>
    private async Task<string?> PickAsync(Func<IVaultFilePicker, Task<string?>> ask)
    {
        if (_disposed || _picking || _picker is not { } picker)
        {
            return null;
        }

        _picking = true;
        Note = null;
        Removing = null;
        _report(null);

        try
        {
            var path = await ask(picker).ConfigureAwait(true);
            return _disposed ? null : path;
        }
        finally
        {
            _picking = false;
        }
    }

    /// <summary>Adds the file at <paramref name="path"/> as <paramref name="attachment"/>, or replaces what that attachment holds with it.</summary>
    private void Attach(string path, string attachment, bool replacing)
    {
        var kept = true;
        WriteResult<bool> write;

        try
        {
            using var content = File.OpenRead(path);
            write = _session.Write(
                vault => replacing
                    ? vault.ReplaceAttachment(_item, attachment, content, out kept)
                    : vault.AddAttachment(_item, attachment, content, out kept),
                attached => attached);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _report($"keypaste couldn't read {Shown(Path.GetFileName(path))}. Check that it's still there and that you can open it, then choose it again.");
            return;
        }

        var shown = EntryNameSanitizer.Sanitize(attachment).Text;
        Finish(
            write,
            replacing ? $"{shown} is no longer on this item." : "This item is no longer in this vault.",
            replacing ? $"{shown} is replaced, and what it held is gone: {_notKept}" : $"{shown} is attached, but {_notKept}",
            kept);
    }

    private void ConfirmRemove()
    {
        if (_removing is not { } row)
        {
            return;
        }

        var kept = true;
        var write = _session.Write(vault => vault.RemoveAttachment(_item, row.Name, out kept), removed => removed);
        Finish(write, $"{row.DisplayName} is no longer on this item.", $"{row.DisplayName} is removed for good: {_notKept}", kept);
    }

    /// <summary>Reads the attachments again after an edit, and says what went wrong or what history could not keep.</summary>
    /// <param name="write">What the edit came to.</param>
    /// <param name="unchanged">What to say when it found nothing to change.</param>
    /// <param name="lost">What to say when the vault's history limits dropped the revision the edit made.</param>
    /// <param name="kept">Whether history kept that revision.</param>
    private void Finish(WriteResult<bool> write, string unchanged, string lost, bool kept)
    {
        var problem = write.Outcome == WriteOutcome.NothingToSave ? unchanged : write.Problem("make your change again");

        Read();

        if (problem is not null)
        {
            _report(problem);
            return;
        }

        _changed();
        _report(null);
        Note = kept ? null : lost;
    }

    /// <summary>Text from outside keypaste, a path or what the filesystem said, as the pane can draw it.</summary>
    private static string Shown(string text) => DisplayTextSanitizer.Sanitize(text).Text;
}
