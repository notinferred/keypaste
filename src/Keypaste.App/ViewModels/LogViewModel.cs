using Keypaste.Core.Audit;

namespace Keypaste.App.ViewModels;

/// <summary>Which records the Activity table holds.</summary>
internal enum LogFilter
{
    All,

    /// <summary>What agents and tokens asked for.</summary>
    Agents,

    /// <summary>What the person answered or did: approvals, refusals they gave, shares, bundles.</summary>
    You,

    /// <summary>Every refusal, whoever or whatever refused.</summary>
    Denied,
}

/// <summary>A filter, and the words the heading states it in (D-0032).</summary>
internal sealed record LogFilterOption(LogFilter Filter, string Words);

/// <summary>
/// The Activity screen: the audit log as a table, newest first, with the chain's verdict on request.
/// </summary>
/// <remarks>
/// <para>
/// <b>It needs no unlocked vault.</b> The audit log is machine state, plaintext by design, and <c>keypaste log</c>
/// reads it without a master password.
/// </para>
/// <para>
/// <b>The records are the core's.</b> <see cref="AuditHistory"/> reads them through <see cref="AuditReader"/> and
/// checks the chain with <see cref="AuditChainVerifier"/>, as <c>keypaste log</c> does; the sentences about the file
/// as a whole — the count, the filter, the notes and the verdict — are <see cref="AuditText"/>'s (D-0032).
/// </para>
/// <para>
/// <b>The chain is checked on every load.</b> A row the chain cannot vouch for is marked, or a record somebody
/// inserted would read exactly like one keypaste wrote. The verdict's paragraphs wait to be asked for.
/// </para>
/// </remarks>
internal sealed class LogViewModel : ObservableObject
{
    /// <summary>What an empty machine is told.</summary>
    internal const string NothingYet = "Nothing has asked keypaste for a credential on this machine yet.";

    private readonly string _path;
    private readonly TimeProvider _clock;

    private AuditHistory _history = new(AuditReadKind.Missing, [], [], string.Empty);
    private IReadOnlyList<LogRow> _all = [];
    private IReadOnlyList<LogRow> _rows = [];
    private IReadOnlyList<string> _notes = [];
    private string _summary = string.Empty;
    private string _message = string.Empty;
    private bool _verdictShown;
    private LogFilterOption _filter;

    internal LogViewModel(string? home, TimeProvider? clock = null)
    {
        _path = KeypasteHome.AuditPath(home);
        _clock = clock ?? TimeProvider.System;
        _filter = Filters[0];

        RefreshCommand = new RelayCommand(Refresh);
        VerifyCommand = new RelayCommand(ToggleVerdict, () => _history.Verdict.Count > 0);

        Refresh();
    }

    internal IReadOnlyList<LogFilterOption> Filters { get; } =
    [
        new(LogFilter.All, string.Empty),
        new(LogFilter.Agents, "agent and token requests only"),
        new(LogFilter.You, "your answers and actions only"),
        new(LogFilter.Denied, "refused calls only"),
    ];

    internal LogFilterOption Filter
    {
        get => _filter;
        set
        {
            if (value is null || !Set(ref _filter, value))
            {
                return;
            }

            Apply();
            Raise(nameof(ShowAll));
            Raise(nameof(ShowAgents));
            Raise(nameof(ShowYou));
            Raise(nameof(ShowDenied));
        }
    }

    /// <summary>The segments' checked states; checking one chooses it, and the unchecking that follows is ignored.</summary>
    internal bool ShowAll
    {
        get => _filter.Filter == LogFilter.All;
        set => Choose(value, LogFilter.All);
    }

    /// <inheritdoc cref="ShowAll"/>
    internal bool ShowAgents
    {
        get => _filter.Filter == LogFilter.Agents;
        set => Choose(value, LogFilter.Agents);
    }

    /// <inheritdoc cref="ShowAll"/>
    internal bool ShowYou
    {
        get => _filter.Filter == LogFilter.You;
        set => Choose(value, LogFilter.You);
    }

    /// <inheritdoc cref="ShowAll"/>
    internal bool ShowDenied
    {
        get => _filter.Filter == LogFilter.Denied;
        set => Choose(value, LogFilter.Denied);
    }

    /// <summary>The records the filter keeps, newest first.</summary>
    internal IReadOnlyList<LogRow> Rows => _rows;

    internal bool HasRows => _rows.Count > 0;

    /// <summary>Whether the table is drawn at all: the file was read and checked.</summary>
    internal bool HasTable => _history.Kind is AuditReadKind.Intact or AuditReadKind.Broken;

    internal bool FilterEmpty => HasTable && _rows.Count == 0;

    /// <summary>What the table is and is not showing, in the core's words: count, filter and file.</summary>
    internal string Summary => _summary;

    /// <summary>The core's notes on the rows shown: unverified rows, unread reasons, lines that are not records.</summary>
    internal string Notes => string.Join(Environment.NewLine, _notes);

    internal bool HasNotes => _notes.Count > 0;

    /// <summary>What the hash chain says about the whole file.</summary>
    internal IReadOnlyList<string> VerdictLines => _history.Verdict;

    internal string VerdictText => string.Join(Environment.NewLine, _history.Verdict);

    /// <summary>Whether the verdict has been asked for.</summary>
    internal bool VerdictShown => _verdictShown;

    internal string VerifyLabel => _verdictShown ? "Hide verdict" : "Verify chain";

    /// <summary>One calm sentence about the file as a whole, or nothing: no log yet, unreadable, unchecked, or broken.</summary>
    internal string Message => _message;

    internal bool HasMessage => _message.Length > 0;

    /// <summary>Whether the message is the chain being broken, which the screen marks as a warning.</summary>
    internal bool IsBroken => _history.Kind == AuditReadKind.Broken;

    internal RelayCommand RefreshCommand { get; }

    internal RelayCommand VerifyCommand { get; }

    /// <summary>Re-reads the log from disk and re-checks it; the verdict folds away, because it described the last read.</summary>
    internal void Refresh()
    {
        _history = AuditHistory.Read(_path);
        _verdictShown = false;
        _message = _history.Kind switch
        {
            AuditReadKind.Missing => NothingYet,
            AuditReadKind.Unreadable => $"That log couldn't be read: {_history.Error}",
            AuditReadKind.Unchecked => "That log couldn't be checked, so nothing from it is shown here.",
            AuditReadKind.Broken => "This log has been edited since keypaste wrote it. Verify chain says where.",
            _ => string.Empty,
        };

        _all = [.. _history.Entries.Reverse().Select(entry => LogRow.From(entry, !_history.Unverified.Contains(entry.Line), _clock))];

        Raise(nameof(HasTable));
        Raise(nameof(VerdictLines));
        Raise(nameof(VerdictText));
        Raise(nameof(VerdictShown));
        Raise(nameof(VerifyLabel));
        Raise(nameof(Message));
        Raise(nameof(HasMessage));
        Raise(nameof(IsBroken));
        VerifyCommand.RaiseCanExecuteChanged();

        Apply();
    }

    private void Apply()
    {
        Func<LogRow, bool> keep = _filter.Filter switch
        {
            LogFilter.Agents => row => !row.ByYou,
            LogFilter.You => row => row.AnsweredOrDoneByYou,
            LogFilter.Denied => row => row.Denied,
            _ => _ => true,
        };

        _rows = [.. _all.Where(keep)];

        var shown = _rows.Select(row => row.Source).Reverse().ToList();
        IReadOnlyList<string> words = _filter.Words.Length == 0 ? [] : [_filter.Words];

        _summary = HasTable ? AuditText.Heading(_path, shown.Count, _history.Total, words) : string.Empty;
        _notes = HasTable ? AuditText.Notes(shown, _history.Unreadable, _history.Unverified) : [];

        Raise(nameof(Rows));
        Raise(nameof(HasRows));
        Raise(nameof(FilterEmpty));
        Raise(nameof(Summary));
        Raise(nameof(Notes));
        Raise(nameof(HasNotes));
    }

    private void Choose(bool chosen, LogFilter filter)
    {
        if (chosen)
        {
            Filter = Filters.Single(option => option.Filter == filter);
        }
    }

    private void ToggleVerdict()
    {
        _verdictShown = !_verdictShown;
        Raise(nameof(VerdictShown));
        Raise(nameof(VerifyLabel));
    }
}
