using Keypaste.Core;
using Keypaste.Core.Agents;
using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>One choice a setting offers on an agent's settings panel; the one that holds is checked.</summary>
/// <param name="Setting">What it sets: <c>run</c>.</param>
/// <param name="Word">Its word as <c>keypaste clients</c> spells it.</param>
/// <param name="Words">What the panel says.</param>
/// <param name="IsChosen">Whether it holds now.</param>
internal sealed record SettingChoice(string Setting, string Word, string Words, bool IsChosen);

/// <summary>One of a category's choices for one agent; the one that holds is checked.</summary>
/// <param name="Category">The category it sets.</param>
/// <param name="Rule">The agent's own rule, or null for what Settings sets.</param>
/// <param name="Words">What the segment says.</param>
/// <param name="IsChosen">Whether it holds now.</param>
internal sealed record AgentCategoryChoice(FieldCategory Category, CategoryRule? Rule, string Words, bool IsChosen);

/// <summary>One category's row for one agent: its name, what Settings gives every agent, and the agent's choices.</summary>
/// <param name="Category">The category.</param>
/// <param name="Name">Its name as the row shows it.</param>
/// <param name="Choices">As in Settings, Ask, Allow and Block.</param>
internal sealed record AgentCategoryRow(FieldCategory Category, string Name, IReadOnlyList<AgentCategoryChoice> Choices);

/// <summary>An answer keypaste remembers for this agent, as its row says it, with what Remove forgets.</summary>
/// <param name="Text">What the row says, every name in it scrubbed.</param>
/// <param name="Forget">The rules without it.</param>
internal sealed record RememberedRow(string Text, Func<AgentRules, AgentRules> Forget);

/// <summary>
/// One agent's settings, on the panel the Agents screen opens from its card: whether it may ask to run commands, its own
/// rule for any category in place of Settings', and the answers a person told keypaste to remember for it, each with Remove.
/// </summary>
/// <remarks>
/// Kept in this computer's <c>agents.toml</c>, sealed with the vault's key by the authority that answers agents. Turning run
/// on releases nothing unasked, since every run is asked, so the change is made at once; so do Ask, Block, As in Settings
/// and Remove. Allow takes the person's confirmation first, since it releases values nobody is asked about.
/// </remarks>
internal sealed partial class AgentSettingsViewModel : ObservableObject, IDisposable
{
    private const string _run = "run";

    private readonly Session.AppAuthority _authority;
    private readonly Action? _changed;
    private IReadOnlyList<SettingChoice> _runChoices = [];
    private IReadOnlyList<AgentCategoryRow> _rows = [];
    private IReadOnlyList<RememberedRow> _remembered = [];
    private FieldCategory? _confirming;

    /// <param name="authority">The authority that answers agents, which reads and seals the rules.</param>
    /// <param name="agent">The agent's name.</param>
    /// <param name="changed">Called after each change, so the screen around the panel reads again.</param>
    internal AgentSettingsViewModel(Session.AppAuthority authority, string agent, Action? changed = null)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(agent);

        _authority = authority;
        _changed = changed;
        Agent = agent;
        ChooseCommand = new RelayCommand<SettingChoice>(choice => Choose(choice!), choice => choice is not null && HasRules);
        ChooseCategoryCommand = new RelayCommand<AgentCategoryChoice>(choice => Choose(choice!), choice => choice is not null && HasRules);
        ConfirmAllowCommand = new RelayCommand(ConfirmAllow);
        CancelAllowCommand = new RelayCommand(() => Confirming = null);
        RemoveCommand = new RelayCommand<RememberedRow>(row => Remove(row!), row => row is not null && HasRules);

        Read();
    }

    /// <summary>The agent's name.</summary>
    internal string Agent { get; }

    private string Shown => EntryNameSanitizer.Sanitize(Agent, AgentNames.MaximumLength).Text;

    /// <summary>The agent's name as the panel shows it.</summary>
    internal string Identity => $"{Shown} gets each kind of value as Settings sets for every agent, unless a row here sets its own. An answer you told keypaste to remember for one item decides before either.";

    /// <summary>Run's two choices.</summary>
    internal IReadOnlyList<SettingChoice> RunChoices
    {
        get => _runChoices;
        private set => SetProperty(ref _runChoices, value);
    }

    /// <summary>Each ruled category's row for this agent, in the order Settings lists them.</summary>
    internal IReadOnlyList<AgentCategoryRow> Rows
    {
        get => _rows;
        private set => SetProperty(ref _rows, value);
    }

    /// <summary>The answers remembered for this agent, in the order they were given.</summary>
    internal IReadOnlyList<RememberedRow> Remembered
    {
        get => _remembered;
        private set
        {
            if (SetProperty(ref _remembered, value))
            {
                OnPropertyChanged(nameof(HasRemembered));
            }
        }
    }

    internal bool HasRemembered => Remembered.Count > 0;

    /// <summary>Why the rules cannot be read, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    internal partial string Problem { get; private set; } = string.Empty;

    internal bool HasProblem => Problem.Length > 0;

    /// <summary>What became of the last change, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    internal partial string Message { get; private set; } = string.Empty;

    internal bool HasMessage => Message.Length > 0;

    /// <summary>The category whose Allow for this agent waits on the person's confirmation, or null.</summary>
    internal FieldCategory? Confirming
    {
        get => _confirming;
        private set
        {
            if (SetProperty(ref _confirming, value))
            {
                OnPropertyChanged(nameof(IsConfirming));
                OnPropertyChanged(nameof(ConfirmText));
                OnPropertyChanged(nameof(ConfirmAction));
            }
        }
    }

    internal bool IsConfirming => Confirming is not null;

    /// <summary>What the person confirms before Allow holds for this agent.</summary>
    internal string ConfirmText => Confirming is { } category ? AgentRules.AllowConfirmation(Shown, category) : string.Empty;

    /// <summary>The confirming button's words, naming its result.</summary>
    internal string ConfirmAction => Confirming is { } category ? $"Allow {FieldCategories.Shown(category)}" : string.Empty;

    internal IRelayCommand<SettingChoice> ChooseCommand { get; }

    internal IRelayCommand<AgentCategoryChoice> ChooseCategoryCommand { get; }

    internal IRelayCommand ConfirmAllowCommand { get; }

    internal IRelayCommand CancelAllowCommand { get; }

    internal IRelayCommand<RememberedRow> RemoveCommand { get; }

    private bool HasRules => !HasProblem;

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    /// <summary>Reads the rules again, after a change made elsewhere.</summary>
    internal void Refresh() => Read();

    private void Read()
    {
        if (!_authority.TryRules(out var rules, out _))
        {
            Problem = "These settings can't be read while the vault is locked or agents.toml can't be read. Correct that, then open them again.";
            RunChoices = [];
            Rows = [];
            Remembered = [];
            Notify();
            return;
        }

        Problem = string.Empty;
        var runs = rules.Runs(Agent);
        List<SettingChoice> choices = [new SettingChoice(_run, "off", "Off", !runs), new SettingChoice(_run, "on", "On", runs)];

        // Each kept when unchanged, so a refresh rebuilds no segment and the keyboard stays where it was.
        if (!choices.SequenceEqual(RunChoices))
        {
            RunChoices = choices;
        }

        List<AgentCategoryRow> rows = [.. FieldCategories.Ruled.Select(category => Row(category, rules.RuleFor(category), rules.RuleFor(Agent, category)))];

        if (rows.Count != Rows.Count || !rows.Zip(Rows).All(pair => pair.First.Choices.SequenceEqual(pair.Second.Choices)))
        {
            Rows = rows;
        }

        Remembered =
        [
            .. rules.Answers.Where(answer => answer.Agent == Agent).Select(answer => new RememberedRow(
                $"{(answer.Rule == CategoryRule.Allow ? "Always allowed" : "Always blocked")}: {Item(answer.Item)}, {Field(answer.Field)}",
                held => held.Forgetting(answer))),
            .. rules.Picks.Where(pick => pick.Agent == Agent).Select(pick => new RememberedRow(
                $"Always picked: {Item(pick.Item)}, {Field(pick.Field)}, when it asks for {DisplayTextSanitizer.Sanitize(pick.Name).Text}",
                held => held.Forgetting(pick))),
        ];

        Notify();
    }

    private void Notify()
    {
        ChooseCommand.NotifyCanExecuteChanged();
        ChooseCategoryCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
    }

    private string Item(string item) => _authority.ItemShown(item) ?? "an item no longer in this vault";

    private static string Field(string field) => field.StartsWith(CredentialFields.Attachment + ":", StringComparison.Ordinal)
        ? $"attachment {EntryNameSanitizer.Sanitize(field[(CredentialFields.Attachment.Length + 1)..]).Text}"
        : EntryNameSanitizer.Sanitize(field).Text;

    private static AgentCategoryRow Row(FieldCategory category, CategoryRule every, CategoryRule? own)
    {
        var shown = FieldCategories.Shown(category);

        return new AgentCategoryRow(
            category,
            char.ToUpperInvariant(shown[0]) + shown[1..],
            [
                new AgentCategoryChoice(category, null, $"As in Settings ({Word(every)})", own is null),
                new AgentCategoryChoice(category, CategoryRule.Ask, "Ask", own == CategoryRule.Ask),
                new AgentCategoryChoice(category, CategoryRule.Allow, "Allow", own == CategoryRule.Allow),
                new AgentCategoryChoice(category, CategoryRule.Block, "Block", own == CategoryRule.Block),
            ]);
    }

    private static string Word(CategoryRule rule) => rule switch
    {
        CategoryRule.Allow => "allow",
        CategoryRule.Block => "block",
        _ => "ask",
    };

    private void Choose(SettingChoice choice)
    {
        if (!choice.IsChosen)
        {
            Change(rules => rules.WithRun(Agent, choice.Word == "on"), choice.Word == "on" ? "Run is on. Each run is still asked." : "Run is off.");
        }
        else
        {
            Read();
            _changed?.Invoke();
        }
    }

    private void Choose(AgentCategoryChoice choice)
    {
        if (choice.IsChosen)
        {
            Confirming = null;
            return;
        }

        if (choice.Rule == CategoryRule.Allow)
        {
            Message = string.Empty;
            Confirming = choice.Category;
            return;
        }

        Confirming = null;
        var shown = FieldCategories.Shown(choice.Category);
        Change(
            rules => rules.With(Agent, choice.Category, choice.Rule),
            choice.Rule switch
            {
                CategoryRule.Block => $"{Shown} is refused {shown} without you being asked.",
                CategoryRule.Ask => $"You're asked before {Shown} gets {shown}.",
                _ => $"{Shown} gets {shown} as Settings sets for every agent.",
            });
    }

    private void ConfirmAllow()
    {
        if (Confirming is not { } category)
        {
            return;
        }

        Confirming = null;
        Change(rules => rules.Allowing(Agent, category, confirmed: true), $"{Shown} gets {FieldCategories.Shown(category)} without asking. Each release is recorded in the log.");
    }

    private void Remove(RememberedRow row) => Change(row.Forget, "Removed. keypaste decides the next request as if you had never answered.");

    private void Change(Func<AgentRules, AgentRules> change, string made)
    {
        var reply = _authority.ChangeRules(Agent, change);
        Message = reply.Changed ? made : $"Not changed: {reply.Reason}";
        Read();
        _changed?.Invoke();
    }
}
