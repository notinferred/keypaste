using Keypaste.Core.Approval;

namespace Keypaste.App.ViewModels;

/// <summary>One item on a picker, as its row shows it: never its value, which is not read until the person allows it.</summary>
/// <param name="Title">Its title, sanitized.</param>
/// <param name="Details">Its group, username and URL, those it has, sanitized.</param>
/// <param name="Field">The field the agent asked for.</param>
internal sealed record PickRow(string Title, string Details, string Field)
{
    /// <summary>What a screen reader says of the row: its names, never a value.</summary>
    internal string Spoken => $"{Title}, {Details}";
}

/// <summary>
/// A credential request more than one item answers to, in front of the person at this desktop: they pick the item the
/// agent gets, then answer as any request is answered, once.
/// </summary>
/// <remarks>
/// Nothing is picked for them, and Allow once cannot be pressed until they pick, as well as until the prompt has armed.
/// </remarks>
internal sealed partial class ItemPickViewModel : PromptViewModel
{
    private readonly IReadOnlyList<ItemCandidate> _candidates;
    private readonly bool _offersAlways;
    private readonly string? _agent;
    private readonly string _itemTitle;
    private int _picked = -1;

    internal ItemPickViewModel(ApprovalPrompt prompt)
        : base(prompt?.Identity)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        _offersAlways = prompt.OffersAlways;
        _agent = prompt.Label;
        _candidates = prompt.Candidates;
        Rows = [.. _candidates.Select(candidate => new PickRow(candidate.Title, Details(candidate), prompt.Field))];

        Client = prompt.Client;
        Label = prompt.Label ?? "none configured";
        Entry = prompt.Entry;
        Field = prompt.Field;
        _itemTitle = prompt.ItemTitle;
        Instruction = $"{_candidates.Count} items answer to this. Pick the one to send.";
        Reason = prompt.Reason;
        Scrubbed = ApprovalViewModel.ScrubbedText(prompt);
        Truncation = ApprovalViewModel.TruncationText(prompt);
    }

    /// <summary>The items, in the vault's order.</summary>
    internal IReadOnlyList<PickRow> Rows { get; }

    /// <summary>The row the person picked, or null until they pick one.</summary>
    [ObservableProperty]
    internal partial PickRow? Picked { get; set; }

    partial void OnPickedChanged(PickRow? value)
    {
        // Only before the answer, which is a latch: what was picked when the person answered is what is released.
        if (!IsAnswered)
        {
            Volatile.Write(ref _picked, value is null ? -1 : IndexOf(value));
        }

        Refresh();
    }

    /// <inheritdoc/>
    internal override bool OffersAlways => _offersAlways;

    /// <inheritdoc/>
    internal override string AlwaysTip => $"Don't ask again for {Agent} and this key: it gets the item you pick whenever it asks for {_itemTitle}. Change in Settings \u203a Agents.";

    /// <inheritdoc/>
    internal override string NoAlways => ApprovalViewModel.NoAlwaysText(_offersAlways, _agent);

    /// <summary>The question's first words: Give, or Always give while confirming.</summary>
    internal string Verb => IsConfirming ? "Always give " : "Give ";

    /// <summary>The field the agent asked for.</summary>
    internal string Field { get; }

    /// <summary>The name every candidate shares, with the vault, as the card names it.</summary>
    internal string Item => InVault(_itemTitle);

    /// <summary>Whose words the quote is.</summary>
    internal string SaysLabel => ApprovalViewModel.Says(Agent);

    /// <summary>The agent's words, quoted.</summary>
    internal string Quoted => ApprovalViewModel.Quote(Reason);

    /// <summary>The tool the request came through.</summary>
    internal string Tool { get; } = "keypaste.request_credential";

    /// <inheritdoc/>
    protected override void OnAllowsChanged() => OnPropertyChanged(nameof(Verb));

    /// <summary>Where the row picked as the person answered is among the candidates, or null; safe from any thread.</summary>
    internal int? PickedIndex => Volatile.Read(ref _picked) is >= 0 and var index ? index : null;

    private int IndexOf(PickRow row)
    {
        for (var i = 0; i < Rows.Count; i++)
        {
            if (ReferenceEquals(Rows[i], row))
            {
                return i;
            }
        }

        return -1;
    }

    protected override bool CanAllow => Picked is not null;

    /// <summary>What the requesting client calls itself. Never proof of anything.</summary>
    internal string Client { get; }

    /// <inheritdoc/>
    protected override string Asks => $"Give {Agent} {Field}? {Rows.Count} items named {_itemTitle} answer to it";

    /// <summary>The label the client's configuration gave its bridge.</summary>
    internal string Label { get; }

    /// <summary>The name the agent asked for, which every candidate has.</summary>
    internal string Entry { get; }

    /// <summary>What the person is to do, said above the list.</summary>
    internal string Instruction { get; }

    /// <summary>The agent's words, already sanitized and capped. Untrusted text.</summary>
    internal string Reason { get; }

    /// <summary>What was scrubbed from the names or the reason, or nothing.</summary>
    internal string Scrubbed { get; }

    /// <summary>That the reason shown is shorter than the one sent, or nothing.</summary>
    internal string Truncation { get; }

    private static string Details(ItemCandidate candidate) =>
        string.Join(" · ", new[] { candidate.Group.Length > 0 ? candidate.Group : "top level", candidate.Username, candidate.Url }.Where(part => part.Length > 0));
}
