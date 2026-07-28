using Keypaste.Core.Audit;

namespace Keypaste.App.ViewModels;

/// <summary>
/// The activity view: every call an AI agent made through the bridge, as <c>keypaste log</c> says it.
/// </summary>
/// <remarks>
/// <para>
/// <b>It needs no unlocked vault.</b> The audit log is machine state rather than vault state — it is
/// plaintext by design, because it is the record that has to survive the vault being locked — and
/// <c>keypaste log</c> reads it without a master password. Asking for one here would be theatre, in
/// exactly the way <c>LogCommand</c> already argues, and it would make the one screen a person opens
/// when something looks wrong the one screen they cannot open.
/// </para>
/// <para>
/// <b>The lines are <see cref="AuditText"/>'s, verbatim.</b> Heading, table and notes arrive already
/// rendered from the core and go into a monospace block untouched. Re-drawing that table as a
/// <c>DataGrid</c> would write the same sentence twice, which CORE.md law 4.3 forbids and D-0032
/// decided for this exact pair of front ends: <c>keypaste log</c> and this view must say the same
/// thing about the same file. <c>LogViewModelTests</c> is what actually holds that.
/// </para>
/// <para>
/// <b>The chain is checked on every load, and the verdict is shown on request.</b> Checking is not
/// the optional part: the table has to mark the rows the chain cannot vouch for, or a record
/// somebody inserted reads exactly like one keypaste wrote. What is optional is the several
/// paragraphs about what a passing check does and does not prove, which belong to the person who
/// asked for them rather than to everyone who opened a window.
/// </para>
/// <para>
/// <b>No Avalonia type appears here.</b> That is what lets its tests be ordinary facts rather than
/// dispatches onto the assembly's one headless session.
/// </para>
/// </remarks>
internal sealed class LogViewModel : ObservableObject
{
    /// <summary>
    /// What an empty machine is told.
    /// </summary>
    /// <remarks>
    /// <c>internal</c> rather than <c>private</c> because the naming rule in <c>.editorconfig</c>
    /// applies <c>_camelCase</c> to every private field, constants included. An absent log is not a
    /// failure — it is what a machine looks like before any agent has asked for anything — so this
    /// is a sentence rather than an error.
    /// </remarks>
    internal const string NothingYet = "Nothing has asked keypaste for a credential on this machine yet.";

    private readonly string _path;

    private IReadOnlyList<string> _lines = [];
    private IReadOnlyList<string> _verdict = [];
    private string _message = string.Empty;
    private bool _verdictShown;

    internal LogViewModel(string? home)
    {
        _path = KeypasteHome.AuditPath(home);

        RefreshCommand = new RelayCommand(Refresh);
        VerifyCommand = new RelayCommand(ShowVerdict, () => _verdict.Count > 0);

        Refresh();
    }

    /// <summary>The log being shown, whether or not it exists.</summary>
    internal string Path => _path;

    /// <summary>
    /// The rendering, exactly as the core produced it: heading, then table, then notes.
    /// </summary>
    /// <remarks>
    /// Nothing is inserted between the three, because everything in this list has to be something
    /// <see cref="AuditText"/> said. Spacing is the view's business.
    /// </remarks>
    internal IReadOnlyList<string> Lines => _lines;

    /// <summary>The same, as one block for a selectable text control to hold.</summary>
    internal string Text => string.Join(Environment.NewLine, _lines);

    internal bool HasLines => _lines.Count > 0;

    /// <summary>What the hash chain says about the whole file.</summary>
    internal IReadOnlyList<string> VerdictLines => _verdict;

    /// <inheritdoc cref="Text"/>
    internal string VerdictText => string.Join(Environment.NewLine, _verdict);

    /// <summary>Whether the verdict has been asked for.</summary>
    internal bool VerdictShown => _verdictShown;

    /// <summary>
    /// One calm sentence about the file as a whole, or nothing.
    /// </summary>
    /// <remarks>
    /// It carries the three things that are true of the file rather than of a row: that there is no
    /// log yet, that this one could not be read or checked, and that the chain is broken. The last
    /// is said on load rather than left behind the button, because a table drawn from an edited file
    /// must not look like a table drawn from one that was not — the same reason <c>keypaste log</c>
    /// alarms on stderr before it prints.
    /// </remarks>
    internal string Message => _message;

    internal bool HasMessage => _message.Length > 0;

    internal RelayCommand RefreshCommand { get; }

    internal RelayCommand VerifyCommand { get; }

    /// <summary>Re-reads the log from disk and re-checks it.</summary>
    /// <remarks>
    /// The verdict is folded back out of sight, because it described the file as it was a moment
    /// ago and this is a different read of it.
    /// </remarks>
    internal void Refresh()
    {
        _lines = [];
        _verdict = [];
        _message = string.Empty;
        _verdictShown = false;

        Load();

        Raise(nameof(Lines));
        Raise(nameof(Text));
        Raise(nameof(HasLines));
        Raise(nameof(VerdictLines));
        Raise(nameof(VerdictText));
        Raise(nameof(VerdictShown));
        Raise(nameof(Message));
        Raise(nameof(HasMessage));
        VerifyCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Reads and renders, in the order <c>LogCommand</c> does it.
    /// </summary>
    /// <remarks>
    /// The reader and the verifier are two passes over the same file on purpose: the verifier works
    /// on bytes and the reader on JSON, so a parser difference can drop a row from a table but can
    /// never change a verdict.
    /// </remarks>
    private void Load()
    {
        if (!File.Exists(_path))
        {
            _message = NothingYet;
            return;
        }

        if (!AuditReader.TryRead(_path, out var entries, out var unreadable, out var error))
        {
            _message = $"That log couldn't be read: {error}";
            return;
        }

        var report = AuditChainVerifier.Verify(_path);

        // A table drawn from a file nothing checked must not be handed over as though something had.
        if (report.Verdict == AuditChainVerdict.Unreadable)
        {
            _message = "That log couldn't be checked, so nothing from it is shown here.";
            return;
        }

        var unverified = report.Unverified;

        // No filters in this version, so the heading says the whole file's count and shows no
        // "of N" — which is the reading AuditText gives an unfiltered table.
        _lines =
        [
            AuditText.Heading(_path, entries.Count, entries.Count, []),
            .. AuditText.Table(entries, unverified),
            .. AuditText.Notes(entries, unreadable, unverified),
        ];

        _verdict = AuditText.Verdict(report);

        if (report.Verdict == AuditChainVerdict.Broken)
        {
            _message = "This log has been edited since keypaste wrote it. Verify chain says where.";
        }
    }

    /// <summary>
    /// Reveals what the check on the current read found.
    /// </summary>
    /// <remarks>
    /// It does not check again. The chain was verified when the file was read, and re-running it
    /// against a file that may have grown since would produce a verdict about records the table
    /// above is not showing.
    /// </remarks>
    private void ShowVerdict()
    {
        if (_verdictShown)
        {
            return;
        }

        _verdictShown = true;
        Raise(nameof(VerdictShown));
    }
}
