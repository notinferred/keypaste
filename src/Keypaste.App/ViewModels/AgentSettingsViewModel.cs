using System.Globalization;
using Keypaste.App.Session;
using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;
using Keypaste.Core.Audit;
using Keypaste.Core.Ipc;

namespace Keypaste.App.ViewModels;

/// <summary>One choice a setting offers on an agent's settings panel; the one that holds is checked.</summary>
/// <param name="Setting">
/// What it sets: <c>mode</c>, <c>values</c>, <c>run</c>, <c>see</c> (all projects or only those picked),
/// or <c>pick</c>, one project or environment among those picked.
/// </param>
/// <param name="Word">Its word as <c>keypaste clients</c> spells it; <c>all</c> or <c>only</c> for <c>see</c>; for <c>pick</c>, a selector.</param>
/// <param name="Words">What the panel says.</param>
/// <param name="IsChosen">Whether it holds now.</param>
/// <param name="Project">The project row a mode is for, or null for the agent's default.</param>
internal sealed record SettingChoice(string Setting, string Word, string Words, bool IsChosen, ProjectScope? Project = null);

/// <summary>One project row on an agent's settings panel: its mode choices and its limit on Automatic.</summary>
/// <param name="Scope">The project or environment it covers.</param>
/// <param name="Modes">Its mode choices.</param>
/// <param name="Limit">Its limit on Automatic in words, or empty when its mode is not Automatic.</param>
internal sealed record SettingRow(ProjectScope Scope, IReadOnlyList<SettingChoice> Modes, string Limit)
{
    internal string Name => Scope.Name;
}

/// <summary>
/// One agent's settings, on the panel the Agents screen opens from its card: what it can see, whether
/// it is given values, whether it may run commands, its mode, project rows and limits.
/// </summary>
/// <remarks>
/// <para>
/// Each choice is one change, made through the session authority that answers agents, as
/// <c>keypaste clients</c> makes one. A change that gives the agent nothing more applies at once,
/// here and in the vault; any other waits for the person to confirm it in the prompt window, and
/// nothing changes until they do. Either way it is recorded in the audit log, and the panel then reads
/// what the vault and this computer hold, so a refused change leaves every control as it was.
/// </para>
/// <para>
/// What it can see is all projects or only those picked, and the picks are sent together as one
/// change, so switching to a few projects never passes through seeing nothing. "Use the vault's
/// settings" is a change of its own, recorded as itself with what it lifted.
/// </para>
/// <para>
/// Settings follow the name the agent's connection was given, never the name it calls itself
/// (THREATS.md T-3), and <c>*</c>'s only take away from the defaults, so its panel offers no Automatic
/// and no commands.
/// </para>
/// </remarks>
internal sealed partial class AgentSettingsViewModel : ObservableObject, IDisposable
{
    private const string _byDefault = "by default";
    private const string _mode = "mode";
    private const string _values = "values";
    private const string _run = "run";
    private const string _see = "see";
    private const string _pick = "pick";
    private const string _all = "all";
    private const string _only = "only";

    /// <summary>Everything <see cref="Read"/> sets.</summary>
    private static readonly string[] _read =
    [
        nameof(Identity), nameof(ModeChoices), nameof(ValueChoices), nameof(RunChoices), nameof(SeeScopes), nameof(SeeChoices),
        nameof(IsOnly), nameof(AlsoSees), nameof(HasAlsoSees), nameof(SeePattern), nameof(Rows), nameof(HasRows), nameof(NewRowScopes), nameof(NewRowScope),
        nameof(LimitTargets), nameof(OffersLimits), nameof(LimitTarget), nameof(LimitNow), nameof(HeldBack), nameof(IsHeldBack),
        nameof(Beneath), nameof(IsBeneath), nameof(CanChange), nameof(PicksCaption), nameof(IsMoreHeldOpen), nameof(IsMoreOpen), nameof(IsAdvancedOpen),
    ];

    private static readonly string[] _timesOfDay = [@"h\:mm", @"hh\:mm", @"h\:mm\:ss", @"hh\:mm\:ss"];

    private static readonly (AgentMode Mode, string Words)[] _modes =
    [
        (AgentMode.Ask, "Ask every time"),
        (AgentMode.AllowForAWhile, "Ask, allow for a while"),
        (AgentMode.Automatic, "Automatic"),
    ];

    private readonly AppAuthority _authority;
    private readonly Action? _changed;
    private readonly CancellationTokenSource _closing = new();
    private AgentSettingsState? _state;
    private IReadOnlyList<SettingChoice> _modeChoices = [];
    private IReadOnlyList<SettingChoice> _valueChoices = [];
    private IReadOnlyList<SettingChoice> _runChoices = [];
    private IReadOnlyList<SettingChoice> _seeScopes = [];
    private IReadOnlyList<SettingChoice> _seeChoices = [];
    private List<string> _offered = [];
    private HashSet<string> _picked = new(StringComparer.Ordinal);
    private bool _choosingOnly;
    private IReadOnlyList<SettingRow> _rows = [];
    private IReadOnlyList<string> _newRowScopes = [];
    private List<string> _limitTargets = [];
    private string? _limitTarget;
    private string _perHour = string.Empty;
    private DateTime? _untilDate;
    private string _untilTime = string.Empty;
    private string _seePattern = string.Empty;
    private string _shownSeePattern = string.Empty;
    private string _shownPerHour = string.Empty;
    private DateTime? _shownUntilDate;
    private string _shownUntilTime = string.Empty;
    private bool _moreAskedOpen;
    private bool _advancedAskedOpen;
    private bool _disposed;

    /// <param name="authority">The session authority that answers agents, which reads and changes their settings.</param>
    /// <param name="agent">The agent's name, or <c>*</c>.</param>
    /// <param name="changed">Called after each change, so the screen around the panel reads again.</param>
    internal AgentSettingsViewModel(AppAuthority authority, string agent, Action? changed = null)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(agent);

        _authority = authority;
        _changed = changed;
        Agent = agent;

        ChooseCommand = new RelayCommand<SettingChoice>(
            choice => Start(() => ChooseAsync(choice!)),
            choice => CanChange && choice is not null && (choice.Setting != _pick || IsOnly));
        ApplyPicksCommand = new RelayCommand(() => Start(ApplyPicksAsync), () => CanChange && PicksChanged);
        ApplySeeCommand = new RelayCommand(() => Start(ApplySeeAsync), () => CanChange);
        AddRowCommand = new RelayCommand(() => Start(AddRowAsync), () => CanChange && NewRowScope is not null);
        RemoveRowCommand = new RelayCommand<SettingRow>(row => Start(() => RemoveRowAsync(row!)), row => CanChange && row is not null);
        ApplyLimitCommand = new RelayCommand(() => Start(ApplyLimitAsync), () => CanChange && _limitTarget is not null);
        UseVaultCommand = new RelayCommand(() => Start(UseVaultAsync), () => CanChange && IsHeldBack);
        ToggleAdvancedCommand = new RelayCommand(() => IsAdvancedOpen = !IsAdvancedOpen);

        Read();
    }

    /// <summary>The agent's name, or <c>*</c>.</summary>
    internal string Agent { get; }

    /// <summary>Whether this is <c>*</c>'s panel: every agent without settings of its own.</summary>
    internal bool IsEveryOther => string.Equals(Agent, AgentNames.Any, StringComparison.Ordinal);

    /// <summary>Whom the settings follow, said plainly.</summary>
    internal string Identity => IsEveryOther
        ? "Every agent without settings of its own gets these, beneath the defaults. They only take away, so there is no Automatic and no commands here."
        : $"These follow the name {Shown}, which this agent's connection was given, not the name the agent calls itself. Any program started with that name gets them."
            + (_state is { HasRecord: false } ? " It has no settings of its own yet, so it gets the defaults." : string.Empty);

    /// <summary>That protected environments are always asked, whatever the settings say.</summary>
    internal string ProtectedNote { get; } =
        "Protected environments (prod, production, prod-*, production-*) are always asked, one release at a time, whatever these say.";

    /// <summary>The agent's mode where no row covers an item.</summary>
    internal IReadOnlyList<SettingChoice> ModeChoices => _modeChoices;

    /// <summary>Whether it is given values, or only into commands it runs.</summary>
    internal IReadOnlyList<SettingChoice> ValueChoices => _valueChoices;

    /// <summary>Whether it may run commands; empty for <c>*</c>.</summary>
    internal IReadOnlyList<SettingChoice> RunChoices => _runChoices;

    internal bool OffersRun => !IsEveryOther;

    /// <summary>All projects, or only those picked below.</summary>
    internal IReadOnlyList<SettingChoice> SeeScopes => _seeScopes;

    /// <summary>Whether it sees only the projects picked, or is being switched to them; the picks can be changed only then.</summary>
    internal bool IsOnly => _choosingOnly || !SeesAll(_state?.Vault ?? AgentAccess.Defaults);

    /// <summary>Every project, and each environment of one, it may be let see, each checked when picked.</summary>
    internal IReadOnlyList<SettingChoice> SeeChoices => _seeChoices;

    /// <summary>What the button does with the picks, said as a warning when none is picked and no pattern remains, since it then sees nothing.</summary>
    internal string PicksCaption => !_offered.Any(_picked.Contains) && !Patterns().Any()
        ? "Nothing picked: this lets it see no items."
        : "Pick the projects and environments it may see, which change as one when you press this.";

    /// <summary>What it sees by a pattern besides the projects, such as <c>api/**</c>, said so the picks are never read as all of it; or empty.</summary>
    internal string AlsoSees => Patterns().ToList() is { Count: > 0 } patterns ? $"It also sees, by pattern: {string.Join(", ", patterns)}." : string.Empty;

    internal bool HasAlsoSees => AlsoSees.Length > 0;

    /// <summary>What it can see, as <c>--expose</c>'s selectors separated by commas, applied with <see cref="ApplySeeCommand"/>.</summary>
    internal string SeePattern
    {
        get => _seePattern;
        set => SetProperty(ref _seePattern, value ?? string.Empty);
    }

    /// <summary>Whether anything behind More differs from an agent's defaults, so More cannot close and look as if nothing does.</summary>
    internal bool IsMoreHeldOpen =>
        _state?.Vault is { } vault
        && (!vault.Values || vault.Projects.Count > 0 || Patterns().Any() || vault.Limit != AccessLimit.None);

    /// <summary>Whether More is open: while anything in it is set or being typed, and otherwise as the person left it.</summary>
    internal bool IsMoreOpen
    {
        get => _moreAskedOpen || IsMoreHeldOpen;
        set
        {
            _moreAskedOpen = value || SeeTyped || LimitTyped;
            OnPropertyChanged();
        }
    }

    /// <summary>Whether the pattern field is shown: while it holds a pattern, and otherwise as the person left it.</summary>
    internal bool IsAdvancedOpen
    {
        get => _advancedAskedOpen || Patterns().Any();
        set
        {
            _advancedAskedOpen = value || SeeTyped;
            _moreAskedOpen |= _advancedAskedOpen;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMoreOpen));
        }
    }

    /// <summary>Its project rows, each beating the default for what it covers.</summary>
    internal IReadOnlyList<SettingRow> Rows => _rows;

    internal bool HasRows => _rows.Count > 0;

    /// <summary>The projects and environments a row can be added for: the vault's, without a row yet, and none protected.</summary>
    internal IReadOnlyList<string> NewRowScopes => _newRowScopes;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddRowCommand))]
    internal partial string? NewRowScope { get; set; }

    /// <summary>Where a limit on Automatic can be set: by default and each row, wherever the mode is Automatic.</summary>
    internal IReadOnlyList<string> LimitTargets => _limitTargets;

    internal bool OffersLimits => _limitTargets.Count > 0;

    internal string? LimitTarget
    {
        get => _limitTarget;
        set
        {
            if (SetProperty(ref _limitTarget, value))
            {
                ApplyLimitCommand.NotifyCanExecuteChanged();
                ShowLimit();
            }
        }
    }

    /// <summary>What leaving would lose: picks, patterns or a limit not yet applied, or null.</summary>
    internal string? UnsavedDraft => PicksChanged || SeeTyped || LimitTyped ? $"your settings for {Shown} not yet applied" : null;

    private bool SeeTyped => _seePattern != _shownSeePattern;

    private bool LimitTyped => _perHour != _shownPerHour || _untilDate != _shownUntilDate || _untilTime != _shownUntilTime;

    /// <summary>At most this many releases an hour, or empty for no hourly limit.</summary>
    internal string PerHourText
    {
        get => _perHour;
        set => SetProperty(ref _perHour, value ?? string.Empty);
    }

    /// <summary>The day Automatic ends on this computer's clock, or null for no end.</summary>
    internal DateTime? UntilDate
    {
        get => _untilDate;
        set => SetProperty(ref _untilDate, value?.Date);
    }

    /// <summary>The time of day on <see cref="UntilDate"/> that Automatic ends, such as <c>18:00</c>, or empty for the close of that day.</summary>
    internal string UntilTimeText
    {
        get => _untilTime;
        set => SetProperty(ref _untilTime, value ?? string.Empty);
    }

    /// <summary>The limit at <see cref="LimitTarget"/> now, its end on this computer's clock with that clock's offset from UTC.</summary>
    internal string LimitNow { get; private set; } = string.Empty;

    /// <summary>What this computer holds the agent back from that the vault gives it, or empty.</summary>
    internal string HeldBack { get; private set; } = string.Empty;

    internal bool IsHeldBack => HeldBack.Length > 0;

    /// <summary>That the vault's settings apply only beneath the defaults for now, or empty.</summary>
    internal string Beneath { get; private set; } = string.Empty;

    internal bool IsBeneath => Beneath.Length > 0;

    /// <summary>Why the settings cannot be read, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    internal partial string Problem { get; private set; } = string.Empty;

    internal bool HasProblem => Problem.Length > 0;

    /// <summary>What became of the last change, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    internal partial string Message { get; private set; } = string.Empty;

    internal bool HasMessage => Message.Length > 0;

    /// <summary>Whether a change is waiting for its answer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChange))]
    [NotifyCanExecuteChangedFor(nameof(ChooseCommand), nameof(ApplyPicksCommand), nameof(ApplySeeCommand), nameof(AddRowCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveRowCommand), nameof(ApplyLimitCommand), nameof(UseVaultCommand))]
    internal partial bool IsChanging { get; private set; }

    /// <summary>Whether a choice can be made now: the settings were read and no change is waiting.</summary>
    internal bool CanChange => !IsChanging && _state is not null;

    /// <summary>The change last started, done once it has its answer.</summary>
    internal Task Changing { get; private set; } = Task.CompletedTask;

    /// <summary>Makes a choice: a mode, values, commands, or one project or environment it can see.</summary>
    internal RelayCommand<SettingChoice> ChooseCommand { get; }

    /// <summary>Lets it see only the projects and environments picked, as one change.</summary>
    internal RelayCommand ApplyPicksCommand { get; }

    /// <summary>Lets it see what <see cref="SeePattern"/> names.</summary>
    internal RelayCommand ApplySeeCommand { get; }

    /// <summary>Adds a row for <see cref="NewRowScope"/>, starting at the mode it has now.</summary>
    internal RelayCommand AddRowCommand { get; }

    /// <summary>Removes a row, so what it covers takes the default.</summary>
    internal RelayCommand<SettingRow> RemoveRowCommand { get; }

    /// <summary>Sets the limit on Automatic at <see cref="LimitTarget"/>.</summary>
    internal RelayCommand ApplyLimitCommand { get; }

    /// <summary>Lifts this computer's ceilings back to what the vault gives, once the person confirms it.</summary>
    internal RelayCommand UseVaultCommand { get; }

    internal RelayCommand ToggleAdvancedCommand { get; }

    private string Shown => EntryNameSanitizer.Sanitize(Agent, ApprovalPrompt.MaximumClientLength).Text;

    /// <summary>This computer's time zone, which every end to Automatic is read and shown in.</summary>
    private TimeZoneInfo Zone => _authority.Session.Clock.LocalTimeZone;

    /// <summary>
    /// Makes a choice and waits for what became of it; choosing what holds changes nothing. Only these,
    /// and each pick under it, change nothing until <see cref="ApplyPicksAsync"/> sends the picks.
    /// </summary>
    /// <param name="choice">The choice.</param>
    /// <returns>Done once the change has its answer and the panel has read again.</returns>
    internal Task ChooseAsync(SettingChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);

        if (string.Equals(choice.Setting, _pick, StringComparison.Ordinal))
        {
            Pick(choice.Word);
            return Task.CompletedTask;
        }

        if (choice.IsChosen)
        {
            return Task.CompletedTask;
        }

        return choice.Setting switch
        {
            _mode => ApplyAsync(project: choice.Project?.Name, mode: choice.Word),
            _values => ApplyAsync(values: choice.Word),
            _run => ApplyAsync(run: choice.Word),
            _ when string.Equals(choice.Word, _only, StringComparison.Ordinal) => ChooseOnly(true),
            _ when SeesAll(_state?.Vault ?? AgentAccess.Defaults) => ChooseOnly(false),
            _ => ApplyAsync(see: [EntryExposure.DefaultGlob, .. Patterns()]),
        };
    }

    /// <summary>Lets the agent see only the projects and environments picked, and whatever patterns it sees beside them, as one change.</summary>
    internal Task ApplyPicksAsync() => ApplyAsync(see: [.. _offered.Where(_picked.Contains), .. Patterns()]);

    /// <summary>Lets the agent see what <see cref="SeePattern"/> names.</summary>
    internal Task ApplySeeAsync()
    {
        _shownSeePattern = _seePattern;
        return ApplyAsync(see: [.. _seePattern.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]);
    }

    /// <summary>Adds a row for <see cref="NewRowScope"/>.</summary>
    internal Task AddRowAsync() =>
        NewRowScope is { } name && ProjectScope.TryParseName(name, out var scope, out _) && _state is { } state
            ? ApplyAsync(project: name, mode: AgentModes.Name(ModeOf(state.Vault, scope)))
            : Task.CompletedTask;

    /// <summary>Removes a row.</summary>
    internal Task RemoveRowAsync(SettingRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return ApplyAsync(project: row.Name, reset: true);
    }

    /// <summary>
    /// Sets the limit at <see cref="LimitTarget"/>, its end read on this computer's clock and sent in
    /// UTC; an empty field removes that limit.
    /// </summary>
    internal Task ApplyLimitAsync()
    {
        TakeTypedLimitAsShown();

        if (_limitTarget is not { } target)
        {
            return Task.CompletedTask;
        }

        if (!TryUntil(out var until, out var error))
        {
            Message = $"Not changed: {error}.";
            return Task.CompletedTask;
        }

        return ApplyAsync(
            project: string.Equals(target, _byDefault, StringComparison.Ordinal) ? null : target,
            perHour: _perHour.Trim() is { Length: > 0 } perHour ? perHour : AccessEdit.None,
            until: until);
    }

    /// <summary>Asks for the vault's settings as they are, which lifts what this computer holds back once the person confirms it.</summary>
    internal Task UseVaultAsync() => ChangeAsync(AccessEdit.VaultSettings);

    /// <summary>Reads the agent's settings again from the vault and this computer's ceilings.</summary>
    internal void Read()
    {
        var seeTyped = SeeTyped;
        var limitTyped = LimitTyped;

        if (_authority.TrySettings(Agent, out var state, out var problem))
        {
            _state = state;
            Problem = string.Empty;
        }
        else
        {
            _state = null;
            Problem = $"These settings can't be read: {problem}. Correct that, then open them again.";
        }

        var vault = _state?.Vault ?? AgentAccess.Defaults;
        var projects = Projects();

        _modeChoices = ModesFor(vault.Mode, null);
        _valueChoices =
        [
            new(_values, "yes", "Yes", vault.Values),
            new(_values, "no", "No, only into commands it runs", !vault.Values),
        ];
        List<SettingChoice> run =
        [
            new(_run, "off", "Off", !vault.Run),
            new(_run, "on", "On, each command still asked about", vault.Run),
        ];
        _runChoices = IsEveryOther ? [] : run;
        var scopes = projects.SelectMany(project => project.Environments.Prepend(project.Whole)).ToList();
        _offered = [.. scopes.Select(Selector)];
        _picked = new HashSet<string>(vault.See.Globs.Where(_offered.Contains), StringComparer.Ordinal);
        _choosingOnly = false;
        _seeChoices = [.. scopes.Select(scope => new SettingChoice(_pick, Selector(scope), scope.Name, _picked.Contains(Selector(scope))))];
        ShowSeeScopes();
        _shownSeePattern = string.Join(", ", vault.See.Globs);

        // A refresh from an outside change leaves a pattern or limit being typed as it is.
        if (!seeTyped)
        {
            _seePattern = _shownSeePattern;
        }
        _rows =
        [
            .. vault.Projects.Select(row => new SettingRow(
                row.Scope,
                ModesFor(row.Mode, row.Scope),
                row.Mode == AgentMode.Automatic ? AccessChangePrompt.Words(row.Limit, Zone) : string.Empty)),
        ];
        _newRowScopes =
        [
            .. projects.SelectMany(project => project.Unprotected.Prepend(project.Whole))
                .Where(scope => vault.Projects.All(row => row.Scope != scope))
                .Select(scope => scope.Name),
        ];
        NewRowScope = Kept(NewRowScope, _newRowScopes);

        List<string> targets = vault.Mode == AgentMode.Automatic ? [_byDefault] : [];
        targets.AddRange(vault.Projects.Where(row => row.Mode == AgentMode.Automatic).Select(row => row.Scope.Name));
        _limitTargets = targets;
        _limitTarget = Kept(_limitTarget, _limitTargets);
        if (!limitTyped)
        {
            ShowLimit();
        }

        HeldBack = _state is { HeldBack.Count: > 0 } held
            ? $"This computer holds {Shown} back from what the vault gives it: {string.Join("; ", held.HeldBack.Select(gain => AccessChangePrompt.Words(gain, held.Vault, Zone)))}."
            : string.Empty;
        Beneath = _state is { BeneathDefaults: true }
            ? "The vault holds a change not saved yet, another program changed its file, or keypaste does not wholly understand these settings, so for now they apply only beneath the defaults."
            : string.Empty;

        // Open because something was set, it stays open when that is removed, rather than closing under the person's hand.
        _moreAskedOpen |= IsMoreHeldOpen;
        _advancedAskedOpen |= Patterns().Any();

        foreach (var name in _read)
        {
            OnPropertyChanged(name);
        }

        foreach (var command in new IRelayCommand[] { ApplyPicksCommand, ChooseCommand, AddRowCommand, ApplyLimitCommand, UseVaultCommand, ApplySeeCommand, RemoveRowCommand })
        {
            command.NotifyCanExecuteChanged();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _closing.Cancel();
        _closing.Dispose();
    }

    private void Start(Func<Task> change) => Changing = change();

    /// <summary>Asks the authority for one change in <c>keypaste clients</c>'s words, then reads the settings again.</summary>
    private Task ApplyAsync(
        string? project = null,
        string? mode = null,
        IReadOnlyList<string>? see = null,
        string? values = null,
        string? run = null,
        string? perHour = null,
        string? until = null,
        bool reset = false)
    {
        if (!CanChange || _disposed)
        {
            return Task.CompletedTask;
        }

        if (!AccessEdit.TryCreate(project, mode, see, values, run, perHour, until, reset, out var edit, out var error))
        {
            Message = $"Not changed: {error}.";
            return Task.CompletedTask;
        }

        return ChangeAsync(edit);
    }

    /// <summary>Asks the authority for one change, then reads the settings again.</summary>
    private async Task ChangeAsync(AccessEdit edit)
    {
        if (!CanChange || _disposed)
        {
            return;
        }

        IsChanging = true;
        Message = "If this gives the agent more, confirm it in the window keypaste opened.";

        try
        {
            var reply = await _authority.ChangeAccessAsync(Agent, edit, _closing.Token).ConfigureAwait(true);
            Message = Said(reply, edit.OnThisClock(reply.Reason, Zone));

            // What it already holds ends too, so the grants listed match what it can now get.
            if (reply is { Decision: AuditDecision.Granted, Method: AuditMethod.Narrowing })
            {
                if (IsEveryOther)
                {
                    _authority.RevokeAll();
                }
                else
                {
                    _authority.RevokeClient(Agent);
                }
            }
        }
        catch (OperationCanceledException)
        {
            Message = "Not changed: the settings were closed before the change had an answer.";
        }
        finally
        {
            IsChanging = false;
        }

        if (!_disposed)
        {
            Read();
            _changed?.Invoke();
        }
    }

    private static string Said(AccessChangeReply reply, string reason)
    {
        var said = reply.Decision == AuditDecision.Granted ? $"Changed: {reason}." : $"Not changed: {reason}.";
        return reply.Recorded ? said : said + " The audit log could not record it. Check that it can be written.";
    }

    /// <summary>
    /// The end the date and time fields name, in the words the pipe carries: none with no day, the close
    /// of the day with no time, and otherwise that time of day, each on this computer's clock.
    /// </summary>
    private bool TryUntil(out string until, out string error)
    {
        until = AccessEdit.None;
        error = string.Empty;
        var time = _untilTime.Trim();

        if (_untilDate is not { } day)
        {
            if (time.Length == 0)
            {
                return true;
            }

            error = "pick the day Automatic ends, or clear the time of day for no end";
            return false;
        }

        var ofDay = UntilTime.CloseOfDay;

        if (time.Length > 0 && !TimeSpan.TryParseExact(time, _timesOfDay, CultureInfo.InvariantCulture, out ofDay))
        {
            error = "a time of day is written like 18:00 on this computer's clock, or left empty for the close of the day";
            return false;
        }

        var words = (day + ofDay).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        if (!UntilTime.TryRecord(words, _authority.Session.Clock, out var recorded, out error))
        {
            return false;
        }

        until = recorded ?? AccessEdit.None;
        return true;
    }

    /// <summary>Whether the picks differ from what the agent sees now, so applying them is a change.</summary>
    private bool PicksChanged =>
        IsOnly && (_choosingOnly || !_picked.SetEquals((_state?.Vault ?? AgentAccess.Defaults).See.Globs.Where(_offered.Contains)));

    private static bool SeesAll(AgentAccess access) => access.See.Globs.Contains(EntryExposure.DefaultGlob, StringComparer.Ordinal);

    /// <summary>What the agent sees beside all projects and the projects offered as picks, such as a place pattern, which a change of picks keeps.</summary>
    private IEnumerable<string> Patterns() =>
        (_state?.Vault ?? AgentAccess.Defaults).See.Globs
            .Where(glob => !string.Equals(glob, EntryExposure.DefaultGlob, StringComparison.Ordinal) && !_offered.Contains(glob, StringComparer.Ordinal));

    /// <summary>Switches the picks on, or back off while the agent sees all projects; nothing is sent.</summary>
    private Task ChooseOnly(bool only)
    {
        _choosingOnly = only;
        ShowSeeScopes();
        OnPropertyChanged(nameof(SeeScopes));
        OnPropertyChanged(nameof(IsOnly));
        OnPropertyChanged(nameof(SeeChoices));
        OnPropertyChanged(nameof(PicksCaption));
        ApplyPicksCommand.NotifyCanExecuteChanged();
        ChooseCommand.NotifyCanExecuteChanged();
        return Task.CompletedTask;
    }

    /// <summary>Picks a project or environment, or unpicks it; nothing is sent until the picks are applied.</summary>
    private void Pick(string selector)
    {
        if (!IsOnly)
        {
            return;
        }

        if (!_picked.Add(selector))
        {
            _picked.Remove(selector);
        }

        _seeChoices = [.. _seeChoices.Select(choice => choice with { IsChosen = _picked.Contains(choice.Word) })];
        OnPropertyChanged(nameof(SeeChoices));
        OnPropertyChanged(nameof(PicksCaption));
        ApplyPicksCommand.NotifyCanExecuteChanged();
    }

    private void ShowSeeScopes() =>
        _seeScopes =
        [
            new(_see, _all, "All projects", !IsOnly),
            new(_see, _only, "Only these", IsOnly),
        ];

    private List<SettingChoice> ModesFor(AgentMode held, ProjectScope? project) =>
    [
        .. _modes
            .Where(mode => !(IsEveryOther && mode.Mode == AgentMode.Automatic))
            .Select(mode => new SettingChoice(_mode, AgentModes.Name(mode.Mode), mode.Words, mode.Mode == held, project)),
    ];

    /// <summary>The mode a scope has now: its row, else its project's row, else the default.</summary>
    private static AgentMode ModeOf(AgentAccess access, ProjectScope scope) =>
        (access.Projects.FirstOrDefault(row => row.Scope == scope)
            ?? access.Projects.FirstOrDefault(row => row.Scope.Environment is null && string.Equals(row.Scope.Project, scope.Project, StringComparison.Ordinal)))
            ?.Mode ?? access.Mode;

    private void ShowLimit()
    {
        var limit = _state?.Vault is not { } vault || _limitTarget is null ? AccessLimit.None
            : string.Equals(_limitTarget, _byDefault, StringComparison.Ordinal) ? vault.Limit
            : vault.Projects.FirstOrDefault(row => string.Equals(row.Scope.Name, _limitTarget, StringComparison.Ordinal))?.Limit ?? AccessLimit.None;

        PerHourText = limit.PerHour?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        LimitNow = _limitTarget is null ? string.Empty : $"Now: {AccessChangePrompt.Words(limit, Zone)}.";
        OnPropertyChanged(nameof(LimitNow));

        if (limit.Until is not { } until)
        {
            UntilDate = null;
            UntilTimeText = string.Empty;
        }
        else
        {
            var here = TimeZoneInfo.ConvertTime(until, Zone);
            UntilDate = here.Date;
            UntilTimeText = UntilTime.IsCloseOfDay(until, Zone) ? string.Empty
                : here.Second == 0 ? here.ToString("HH:mm", CultureInfo.InvariantCulture)
                : here.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }

        _shownPerHour = _perHour;
        _shownUntilDate = _untilDate;
        _shownUntilTime = _untilTime;
    }

    // Applying asks for what was typed, so the read after it shows what the vault then holds.
    private void TakeTypedLimitAsShown()
    {
        _shownPerHour = _perHour;
        _shownUntilDate = _untilDate;
        _shownUntilTime = _untilTime;
    }

    /// <summary>The choice still offered, else the first one.</summary>
    private static string? Kept(string? chosen, IReadOnlyList<string> offered) =>
        chosen is { } kept && offered.Contains(kept, StringComparer.Ordinal) ? kept
        : offered.Count > 0 ? offered[0]
        : null;

    private static string Selector(ProjectScope scope) => EntryExposure.TagSelectorPrefix + ProjectTag.Prefix + scope.Name;

    /// <summary>The vault's projects as scopes: each whole project, its environments, and those not protected.</summary>
    private List<ProjectScopes> Projects()
    {
        if (_authority.Session.Unlocked is not { } vault)
        {
            return [];
        }

        List<ProjectScopes> projects = [];

        foreach (var listing in ProjectCatalog.Read(vault).Projects)
        {
            if (!ProjectScope.TryCreate(listing.Name, null, out var whole, out _))
            {
                continue;
            }

            List<ProjectScope> environments = [];
            List<ProjectScope> unprotected = [];

            foreach (var environment in listing.Environments)
            {
                if (ProjectScope.TryCreate(listing.Name, environment.Name, out var scope, out _))
                {
                    environments.Add(scope);

                    if (!environment.IsProtected)
                    {
                        unprotected.Add(scope);
                    }
                }
            }

            projects.Add(new ProjectScopes(whole, environments, unprotected));
        }

        return projects;
    }

    private sealed record ProjectScopes(ProjectScope Whole, IReadOnlyList<ProjectScope> Environments, IReadOnlyList<ProjectScope> Unprotected);
}
