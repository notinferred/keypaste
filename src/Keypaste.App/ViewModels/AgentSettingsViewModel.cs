using Keypaste.Core;
using Keypaste.Core.Agents;

namespace Keypaste.App.ViewModels;

/// <summary>One choice a setting offers on an agent's settings panel; the one that holds is checked.</summary>
/// <param name="Setting">What it sets: <c>run</c>.</param>
/// <param name="Word">Its word as <c>keypaste clients</c> spells it.</param>
/// <param name="Words">What the panel says.</param>
/// <param name="IsChosen">Whether it holds now.</param>
internal sealed record SettingChoice(string Setting, string Word, string Words, bool IsChosen);

/// <summary>One agent's settings, on the panel the Agents screen opens from its card: whether it may ask to run commands.</summary>
/// <remarks>
/// Kept in this computer's <c>agents.toml</c>, as <c>keypaste clients</c> keeps it. Turning run on releases nothing unasked,
/// since every run is asked, so the change is made at once. What each kind of value gets is set for every agent in Settings.
/// </remarks>
internal sealed partial class AgentSettingsViewModel : ObservableObject, IDisposable
{
    private const string _run = "run";

    private readonly AgentRulesFile _rules;
    private readonly Action? _changed;
    private IReadOnlyList<SettingChoice> _runChoices = [];

    /// <param name="rules">This computer's agent rules.</param>
    /// <param name="agent">The agent's name.</param>
    /// <param name="changed">Called after each change, so the screen around the panel reads again.</param>
    internal AgentSettingsViewModel(AgentRulesFile rules, string agent, Action? changed = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(agent);

        _rules = rules;
        _changed = changed;
        Agent = agent;
        ChooseCommand = new RelayCommand<SettingChoice>(choice => Choose(choice!), choice => choice is not null && HasRules);

        Read();
    }

    /// <summary>The agent's name.</summary>
    internal string Agent { get; }

    /// <summary>The agent's name as the panel shows it.</summary>
    internal string Identity => $"{EntryNameSanitizer.Sanitize(Agent, AgentNames.MaximumLength).Text} asks for each value under the rules in Settings, the same for every agent.";

    /// <summary>Run's two choices.</summary>
    internal IReadOnlyList<SettingChoice> RunChoices
    {
        get => _runChoices;
        private set => SetProperty(ref _runChoices, value);
    }

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

    internal IRelayCommand<SettingChoice> ChooseCommand { get; }

    private bool HasRules => !HasProblem;

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    /// <summary>Reads the rules again, after a change made elsewhere.</summary>
    internal void Refresh() => Read();

    private void Read()
    {
        if (!_rules.TryRead(out var rules, out var problem))
        {
            Problem = $"These settings can't be read: {problem}. Correct that, then open them again.";
            RunChoices = [];
            return;
        }

        Problem = string.Empty;
        var runs = rules.Runs(Agent);
        List<SettingChoice> choices = [new SettingChoice(_run, "off", "Off", !runs), new SettingChoice(_run, "on", "On", runs)];

        // Kept when unchanged, so a refresh rebuilds no segment and the keyboard stays where it was.
        if (!choices.SequenceEqual(RunChoices))
        {
            RunChoices = choices;
        }
        ChooseCommand.NotifyCanExecuteChanged();
    }

    private void Choose(SettingChoice choice)
    {
        if (choice.IsChosen || !_rules.TryRead(out var rules, out var problem))
        {
            Read();
            return;
        }

        try
        {
            Message = _rules.TryWrite(rules.WithRun(Agent, choice.Word == "on"), out problem)
                ? choice.Word == "on" ? "Run is on. Each run is still asked." : "Run is off."
                : $"Not changed: {problem}";
        }
        catch (ArgumentException tooMany)
        {
            Message = $"Not changed: {tooMany.Message}";
        }

        Read();
        _changed?.Invoke();
    }
}
