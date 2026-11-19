using Keypaste.App.Resources;
using Keypaste.Core.Agents;

namespace Keypaste.App.ViewModels;

/// <summary>One of a category's three rules on Settings › Agents; the one that holds is raised.</summary>
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

/// <summary>Settings › Agents: the defaults every agent gets on this computer, the username and the hosts and references switches, and each category's rule.</summary>
/// <remarks>
/// Each change goes through the authority that answers agents, which seals it with the vault's key and records it. Allow and
/// turning a switch on take the person's confirmation first, since they give agents what nobody is asked about; Ask, Block and
/// turning a switch off take effect at once.
/// </remarks>
internal sealed partial class AgentRulesViewModel : ObservableObject, IDisposable
{
    private const string _everyAgent = "every agent";

    private readonly Session.AppAuthority _authority;
    private IReadOnlyList<CategoryRow> _rows = [];
    private bool _showsUsername = true;
    private bool _showsLinkedHosts;
    private Confirmation? _confirming;

    /// <param name="authority">The authority that answers agents, which reads and seals the rules.</param>
    internal AgentRulesViewModel(Session.AppAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);

        _authority = authority;
        ChooseCommand = new RelayCommand<CategoryChoice>(choice => Choose(choice!), choice => choice is not null && !HasProblem);
        ConfirmGrantCommand = new RelayCommand(Confirm);
        CancelGrantCommand = new RelayCommand(() => Confirming = null);

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

            if (value)
            {
                Ask(
                    nameof(ShowsUsername),
                    new Confirmation(Strings.EveryAgentWillSeeEachItemsUsername, Strings.ShowUsernamesToAgents, Strings.TurnTheSwitchOffHereToStopIt, rules => rules with { ShowsUsername = true }, "Agents see usernames again."));
                return;
            }

            Confirming = null;
            Change(rules => rules with { ShowsUsername = false }, "Agents are asked before they get a username.");
        }
    }

    /// <summary>Whether a search shows agents the host in an item's Host field and which items its fields reference.</summary>
    internal bool ShowsLinkedHosts
    {
        get => _showsLinkedHosts;
        set
        {
            if (value == _showsLinkedHosts)
            {
                return;
            }

            if (value)
            {
                Ask(
                    nameof(ShowsLinkedHosts),
                    new Confirmation(Strings.EveryAgentsSearchesWillShowHostsAndReferences, Strings.ShowHostsAndReferencesToAgents, Strings.TurnTheSwitchOffHereToStopIt, rules => rules with { ShowsLinkedHosts = true }, "Agents' searches show hosts and references."));
                return;
            }

            Confirming = null;
            Change(rules => rules with { ShowsLinkedHosts = false }, "Agents' searches no longer show hosts or references.");
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

    private Confirmation? Confirming
    {
        get => _confirming;
        set
        {
            if (SetProperty(ref _confirming, value))
            {
                OnPropertyChanged(nameof(IsConfirming));
                OnPropertyChanged(nameof(ConfirmText));
                OnPropertyChanged(nameof(ConfirmAction));
                OnPropertyChanged(nameof(ConfirmCaption));
            }
        }
    }

    /// <summary>Whether a category's Allow or a switch turned on waits on the person's confirmation.</summary>
    internal bool IsConfirming => Confirming is not null;

    /// <summary>What the person confirms agents will then get.</summary>
    internal string ConfirmText => Confirming?.Text ?? string.Empty;

    /// <summary>The confirming button's words, naming its result.</summary>
    internal string ConfirmAction => Confirming?.Action ?? string.Empty;

    /// <summary>Where the person undoes it once confirmed.</summary>
    internal string ConfirmCaption => Confirming?.Caption ?? string.Empty;

    internal IRelayCommand<CategoryChoice> ChooseCommand { get; }

    internal IRelayCommand ConfirmGrantCommand { get; }

    internal IRelayCommand CancelGrantCommand { get; }

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
            Problem = "These rules can't be read or changed while the vault is locked or its agent rules can't be read. Correct that, then open Settings again.";
            Ignored = string.Empty;
            Rows = [];
            ChooseCommand.NotifyCanExecuteChanged();
            return;
        }

        Problem = string.Empty;
        Ignored = ignored is null ? string.Empty : $"{char.ToUpperInvariant(ignored[0])}{ignored[1..]}. Until then every value is asked about and no agent may run commands.";
        SetProperty(ref _showsUsername, rules.ShowsUsername, nameof(ShowsUsername));
        SetProperty(ref _showsLinkedHosts, rules.ShowsLinkedHosts, nameof(ShowsLinkedHosts));

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
                new CategoryChoice(category, CategoryRule.Ask, Strings.Ask, held == CategoryRule.Ask),
                new CategoryChoice(category, CategoryRule.Allow, Strings.Allow, held == CategoryRule.Allow),
                new CategoryChoice(category, CategoryRule.Block, Strings.Block, held == CategoryRule.Block),
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
            var shown = FieldCategories.Shown(choice.Category);
            Message = string.Empty;
            Confirming = new Confirmation(
                AgentRules.AllowConfirmation(choice.Category),
                $"Allow {shown}",
                Strings.EachReleaseIsRecordedInTheLogAndYouCanSetAskAgainHere,
                rules => rules.Allowing(choice.Category, confirmed: true),
                $"Every agent gets {shown} without asking. Each release is recorded in the log.");
            return;
        }

        Confirming = null;
        var asked = FieldCategories.Shown(choice.Category);
        Change(
            rules => rules.With(choice.Category, choice.Rule),
            choice.Rule == CategoryRule.Block ? $"Agents are refused {asked} without you being asked." : $"You're asked before an agent gets {asked}.");
    }

    /// <summary>Puts a switch's turning on to the person, leaving the switch drawn off until they confirm it.</summary>
    private void Ask(string property, Confirmation confirmation)
    {
        Message = string.Empty;
        Confirming = confirmation;
        OnPropertyChanged(property);
    }

    private void Confirm()
    {
        if (Confirming is not { } confirmation)
        {
            return;
        }

        Confirming = null;
        Change(confirmation.Change, confirmation.Made);
    }

    private void Change(Func<AgentRules, AgentRules> change, string made)
    {
        var reply = _authority.ChangeRules(_everyAgent, change);
        Message = reply.Changed ? made : $"Not changed: {reply.Reason}";
        Read();
    }

    /// <summary>A change that gives agents more, waiting on the person's confirmation.</summary>
    /// <param name="Text">What agents will then get.</param>
    /// <param name="Action">The confirming button's words.</param>
    /// <param name="Caption">Where the person undoes it.</param>
    /// <param name="Change">The change once confirmed.</param>
    /// <param name="Made">What the message says once it holds.</param>
    private sealed record Confirmation(string Text, string Action, string Caption, Func<AgentRules, AgentRules> Change, string Made);
}
