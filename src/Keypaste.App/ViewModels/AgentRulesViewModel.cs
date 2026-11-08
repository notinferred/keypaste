using Keypaste.Core.Agents;

namespace Keypaste.App.ViewModels;

/// <summary>One of a category's three rules on Settings › Agents; the one that holds is checked.</summary>
/// <param name="Category">The category it sets.</param>
/// <param name="Rule">The rule.</param>
/// <param name="Words">What the segment says.</param>
/// <param name="IsChosen">Whether it holds now.</param>
internal sealed record CategoryChoice(FieldCategory Category, CategoryRule Rule, string Words, bool IsChosen);

/// <summary>One category's row: its name and its three rules.</summary>
/// <param name="Category">The category.</param>
/// <param name="Name">Its name as the row shows it.</param>
/// <param name="Choices">Ask, Allow and Block.</param>
internal sealed record CategoryRow(FieldCategory Category, string Name, IReadOnlyList<CategoryChoice> Choices);

/// <summary>Settings › Agents: what every agent gets of each kind of value on this computer, the username switch and each category's rule.</summary>
/// <remarks>
/// Each change goes through the authority that answers agents, which seals it with the vault's key and records it. Allow takes
/// the person's confirmation first, since it releases values nobody is asked about; Ask and Block take effect at once.
/// </remarks>
internal sealed partial class AgentRulesViewModel : ObservableObject, IDisposable
{
    private const string _everyAgent = "every agent";

    private readonly Session.AppAuthority _authority;
    private IReadOnlyList<CategoryRow> _rows = [];
    private bool _showsUsername = true;
    private FieldCategory? _confirming;

    /// <param name="authority">The authority that answers agents, which reads and seals the rules.</param>
    internal AgentRulesViewModel(Session.AppAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);

        _authority = authority;
        ChooseCommand = new RelayCommand<CategoryChoice>(choice => Choose(choice!), choice => choice is not null && !HasProblem);
        ConfirmAllowCommand = new RelayCommand(ConfirmAllow);
        CancelAllowCommand = new RelayCommand(() => Confirming = null);

        Read();
    }

    /// <summary>Each ruled category's row, in the order Settings lists them.</summary>
    internal IReadOnlyList<CategoryRow> Rows
    {
        get => _rows;
        private set => SetProperty(ref _rows, value);
    }

    /// <summary>Whether agents see an item's username without asking; turning it off hides the username alone.</summary>
    internal bool ShowsUsername
    {
        get => _showsUsername;
        set
        {
            if (value == _showsUsername)
            {
                return;
            }

            Change(rules => rules with { ShowsUsername = value }, value ? "Agents see usernames again." : "Agents are asked before they get a username.");
        }
    }

    /// <summary>Why the rules on file are ignored and the defaults hold, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIgnored))]
    internal partial string Ignored { get; private set; } = string.Empty;

    internal bool IsIgnored => Ignored.Length > 0;

    /// <summary>Why the rules cannot be read or changed here, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    internal partial string Problem { get; private set; } = string.Empty;

    internal bool HasProblem => Problem.Length > 0;

    /// <summary>What became of the last change, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    internal partial string Message { get; private set; } = string.Empty;

    internal bool HasMessage => Message.Length > 0;

    /// <summary>The category whose Allow waits on the person's confirmation, or null.</summary>
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

    /// <summary>What the person confirms before Allow holds.</summary>
    internal string ConfirmText => Confirming is { } category ? AgentRules.AllowConfirmation(category) : string.Empty;

    /// <summary>The confirming button's words, naming its result.</summary>
    internal string ConfirmAction => Confirming is { } category ? $"Allow {FieldCategories.Shown(category)}" : string.Empty;

    internal IRelayCommand<CategoryChoice> ChooseCommand { get; }

    internal IRelayCommand ConfirmAllowCommand { get; }

    internal IRelayCommand CancelAllowCommand { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    /// <summary>Reads the rules again, after a change made elsewhere.</summary>
    internal void Refresh() => Read();

    private void Read()
    {
        if (!_authority.TryRules(out var rules, out var ignored))
        {
            Problem = "These rules can't be read or changed while the vault is locked or agents.toml can't be read. Correct that, then open Settings again.";
            Ignored = string.Empty;
            Rows = [];
            ChooseCommand.NotifyCanExecuteChanged();
            return;
        }

        Problem = string.Empty;
        Ignored = ignored is null ? string.Empty : $"{char.ToUpperInvariant(ignored[0])}{ignored[1..]}. Until then every value is asked about and no agent may run commands.";
        SetProperty(ref _showsUsername, rules.ShowsUsername, nameof(ShowsUsername));

        List<CategoryRow> rows = [.. FieldCategories.Ruled.Select(category => Row(category, rules.RuleFor(category)))];

        // Kept when unchanged, so a refresh rebuilds no segment and the keyboard stays where it was.
        if (rows.Count != Rows.Count || !rows.Zip(Rows).All(pair => pair.First.Choices.SequenceEqual(pair.Second.Choices)))
        {
            Rows = rows;
        }

        ChooseCommand.NotifyCanExecuteChanged();
    }

    private static CategoryRow Row(FieldCategory category, CategoryRule held)
    {
        var shown = FieldCategories.Shown(category);

        return new CategoryRow(
            category,
            char.ToUpperInvariant(shown[0]) + shown[1..],
            [
                new CategoryChoice(category, CategoryRule.Ask, "Ask", held == CategoryRule.Ask),
                new CategoryChoice(category, CategoryRule.Allow, "Allow", held == CategoryRule.Allow),
                new CategoryChoice(category, CategoryRule.Block, "Block", held == CategoryRule.Block),
            ]);
    }

    private void Choose(CategoryChoice choice)
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
            rules => rules.With(choice.Category, choice.Rule),
            choice.Rule == CategoryRule.Block ? $"Agents are refused {shown} without you being asked." : $"You're asked before an agent gets {shown}.");
    }

    private void ConfirmAllow()
    {
        if (Confirming is not { } category)
        {
            return;
        }

        Confirming = null;
        Change(rules => rules.Allowing(category, confirmed: true), $"Every agent gets {FieldCategories.Shown(category)} without asking. Each release is recorded in the log.");
    }

    private void Change(Func<AgentRules, AgentRules> change, string made)
    {
        var reply = _authority.ChangeRules(_everyAgent, change);
        Message = reply.Changed ? made : $"Not changed: {reply.Reason}";
        Read();
    }
}
