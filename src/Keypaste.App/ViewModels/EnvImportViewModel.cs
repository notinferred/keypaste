using Keypaste.App.Session;
using Keypaste.Core;

namespace Keypaste.App.ViewModels;

/// <summary>
/// Imports a <c>.env</c> into the open project: read and checked whole, previewed by name, and
/// written only on Import (E.1b).
/// </summary>
/// <remarks>
/// <para>
/// The file goes through the same reader and plan as <c>keypaste env pull</c> (<see cref="DotEnv"/>,
/// <see cref="EnvImport"/>), so the two front ends import the same file the same way. A file with any
/// problem imports nothing, and every message names a line and a rule, never the text.
/// </para>
/// <para>
/// The file is left where it is. The CLI offers to delete it with a warning about what deleting
/// cannot erase; the app does not repeat that offer.
/// </para>
/// </remarks>
internal sealed class EnvImportViewModel : ObservableObject, IDisposable
{
    private const int _problemsShown = 10;

    private readonly AppVaultSession _session;
    private readonly string _project;
    private readonly IVaultFilePicker? _picker;
    private readonly Action<string?> _report;
    private readonly Action<string?> _announce;
    private readonly Action _imported;

    private string _planned = EnvProfileNames.Default;

    private DotEnvDocument? _document;
    private IReadOnlyList<EnvImportKey> _previewed = [];
    private IReadOnlyList<string> _rows = [];
    private string _source = string.Empty;
    private string _notes = string.Empty;

    internal EnvImportViewModel(
        AppVaultSession session,
        string project,
        IVaultFilePicker? picker,
        Action<string?> report,
        Action<string?> announce,
        Action imported)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(announce);
        ArgumentNullException.ThrowIfNull(imported);

        _session = session;
        _project = project;
        _picker = picker;
        _report = report;
        _announce = announce;
        _imported = imported;

        ChooseCommand = new AsyncRelayCommand(ChooseAsync, () => _picker is not null);
        ConfirmCommand = new RelayCommand(Confirm, () => IsPreviewing);
        CancelCommand = new RelayCommand(Clear, () => IsPreviewing);
    }

    /// <summary>The profile a file is previewed for; the preview's own profile is what Import writes to.</summary>
    internal string Profile { get; set; } = EnvProfileNames.Default;

    /// <summary>Whether a file is read and waiting on Import.</summary>
    internal bool IsPreviewing => _document is not null;

    /// <summary>The file being previewed.</summary>
    internal string Source
    {
        get => _source;
        private set => Set(ref _source, value);
    }

    /// <summary>One line per variable: its name and what importing does to it. Never a value.</summary>
    internal IReadOnlyList<string> Rows
    {
        get => _rows;
        private set => Set(ref _rows, value);
    }

    /// <summary>What the reader interpreted, naming keys.</summary>
    internal string Notes
    {
        get => _notes;
        private set
        {
            if (Set(ref _notes, value))
            {
                Raise(nameof(HasNotes));
            }
        }
    }

    internal bool HasNotes => _notes.Length > 0;

    internal AsyncRelayCommand ChooseCommand { get; }

    internal RelayCommand ConfirmCommand { get; }

    internal RelayCommand CancelCommand { get; }

    /// <summary>Nothing read from the file outlives this.</summary>
    public void Dispose() => Clear();

    private async Task ChooseAsync()
    {
        if (_picker is not null && await _picker.PickDotEnvAsync().ConfigureAwait(true) is { } path)
        {
            Preview(path);
        }
    }

    /// <summary>Reads and checks a file and shows what importing it would do.</summary>
    /// <param name="path">The file.</param>
    internal void Preview(string path)
    {
        Clear();
        _report(null);

        if (_session.Unlocked is not { } vault)
        {
            _report("The vault is locked.");
            return;
        }

        byte[] bytes;

        try
        {
            if (new FileInfo(path).Length > DotEnv.MaximumBytes)
            {
                _report($"{path} is larger than a .env file can be, so nothing was imported.");
                return;
            }

            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _report($"{path} could not be read: {ex.Message}");
            return;
        }

        if (!DotEnv.TryDecode(bytes, out var text, out var decodeError))
        {
            _report($"{path}: {decodeError} Nothing was imported.");
            return;
        }

        if (!DotEnv.TryParse(text, out var document))
        {
            var problems = document.Problems.Take(_problemsShown)
                .Select(problem => DisplayTextSanitizer.Sanitize(problem.Message).Text);
            var more = document.Problems.Count > _problemsShown ? $" ({document.Problems.Count - _problemsShown} more not shown)" : string.Empty;
            _report($"{path} has problems, so nothing was imported: {string.Join("; ", problems)}{more}.");
            return;
        }

        EnvImportPlan plan;

        try
        {
            plan = EnvImport.Plan(new EnvStore(vault), _project, Profile, document);
        }
        catch (VaultException e)
        {
            _report(e.Message);
            return;
        }

        if (plan.Refusal is { } refusal)
        {
            _report($"Nothing was imported: {refusal}.");
            return;
        }

        _document = document;
        _planned = plan.Profile;
        _previewed = plan.Keys;
        Source = path;
        Rows = [.. plan.Keys.Select(key => $"{EntryNameSanitizer.Sanitize(key.Key).Text}  {Describe(key.Change)}")];
        Notes = NotesOf(document);
        RaisePreviewing();
    }

    private void Confirm()
    {
        if (_document is not { } document)
        {
            return;
        }

        if (_session.Unlocked is not { } vault)
        {
            _report("The vault is locked.");
            Clear();
            return;
        }

        try
        {
            var store = new EnvStore(vault);
            var plan = EnvImport.Plan(store, _project, _planned, document);

            // The preview is what the person agreed to. A project that changed since is shown again.
            if (plan.Refusal is not null || !plan.Keys.SequenceEqual(_previewed))
            {
                _report("The project changed since this preview, so nothing was imported. Choose the file again.");
                Clear();
                return;
            }

            if (!plan.WritesAnything)
            {
                _announce($"{EnvProfileNames.GroupPath(_project, _planned)} already matches the file; nothing was written.");
                Clear();
                return;
            }

            if (!EnvImport.TryApply(store, plan, out var rejection))
            {
                // Checked before anybody was asked, so nothing has been saved. Say so and stop.
                _report($"{rejection} Nothing was imported.");
                Clear();
                return;
            }

            vault.Save();

            var written = plan.Created.Count + plan.Updated.Count;
            _announce($"Imported {(written == 1 ? "1 variable" : $"{written} variables")} into {EnvProfileNames.GroupPath(_project, _planned)}. {Source} is still there.");
        }
        catch (VaultChangedOnDiskException)
        {
            _report("Something else changed this vault since you opened it. Lock and unlock to see it, then import again.");
            return;
        }
        catch (VaultException e)
        {
            _report(e.Message);
            return;
        }

        Clear();
        _imported();
    }

    private void Clear()
    {
        _document = null;
        _previewed = [];
        Source = string.Empty;
        Rows = [];
        Notes = string.Empty;
        RaisePreviewing();
    }

    private void RaisePreviewing()
    {
        Raise(nameof(IsPreviewing));
        ConfirmCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
    }

    private static string Describe(EnvImportChange change) => change switch
    {
        EnvImportChange.New => "new",
        EnvImportChange.Replaces => "replaces the stored value, which stays in history",
        _ => "unchanged",
    };

    private static string NotesOf(DotEnvDocument document)
    {
        var comments = string.Join(", ", document.Notes.Where(n => n.Kind == DotEnvNoteKind.InlineCommentRemoved).Select(n => n.Key));
        var literal = string.Join(", ", document.Notes.Where(n => n.Kind == DotEnvNoteKind.LiteralInterpolation).Select(n => n.Key));
        List<string> notes = [];

        if (comments.Length > 0)
        {
            notes.Add($"A trailing ' #' comment was removed from {comments}; quote the value if the '#' was part of it.");
        }

        if (literal.Length > 0)
        {
            notes.Add($"Values are stored exactly as written; ${{...}} is not expanded in {literal}.");
        }

        return DisplayTextSanitizer.Sanitize(string.Join(" ", notes), 2048).Text;
    }
}
