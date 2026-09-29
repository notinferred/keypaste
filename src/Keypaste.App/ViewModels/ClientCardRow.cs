using Keypaste.Core.Clients;

namespace Keypaste.App.ViewModels;

/// <summary>One MCP client card on the Agents screen: who, whether attached now, and its policy. Never a value.</summary>
/// <remarks>
/// Names come from what clients say about themselves and from the labels in their configuration:
/// display only (THREATS.md T-3). A client without a label is held to the <c>*</c> row's policy and
/// cannot be given its own; the <c>*</c> card is where the strict policy belongs (T-36).
/// </remarks>
internal sealed class ClientCardRow : ObservableObject
{
    private readonly Action<ClientCardRow, ClientPolicy>? _choose;
    private ClientPolicy _policy;
    private bool _isMenuOpen;

    internal ClientCardRow(McpClientCard? card, ClientPolicy policy, DateTimeOffset now, Action<ClientCardRow, ClientPolicy>? choose)
    {
        _choose = choose;
        _policy = policy;
        Choices = [.. Enum.GetValues<ClientPolicy>().Select(choice => new PolicyChoice(this, choice))];

        if (card is null)
        {
            Label = ClientPolicies.AnyClient;
            Name = "Every other client";
            Title = Name;
            Detail = "Clients without a policy of their own";
            Initials = "*";
            Status = string.Empty;
            StatusTone = StatusTone.Muted;
            LastSeenText = string.Empty;
            CanSetPolicy = true;
            // A label lives in the client's own configuration, which an agent may be able to edit.
            Hint = "Unlabeled clients and labels without a row here get this policy, so keep it the strictest.";
            return;
        }

        Label = card.Label;
        Name = card.Name;
        Title = card.Label ?? card.Name;
        Detail = $"{card.Name} · {card.Kind}";
        Initials = InitialsOf(Title);
        Status = card.Status == ClientStatus.Connected ? "Connected" : "Idle";
        StatusTone = card.Status == ClientStatus.Connected ? StatusTone.Ok : StatusTone.Muted;
        LastSeenText = card.Status == ClientStatus.Connected ? "now" : card.LastSeen is { } seen ? UseText.Ago(seen, now) : "never";
        // A label an older bridge wrote to the log may be one no clients.toml row can hold.
        CanSetPolicy = card.Label is { } written && ClientPolicies.IsValidLabel(written, out _);
        Hint = CanSetPolicy ? string.Empty : "Start this client's bridge with --client-label to give it its own policy.";
    }

    /// <summary>The label a policy row is keyed by, <c>*</c> for the catch-all card, or null for an unlabeled client.</summary>
    internal string? Label { get; }

    /// <summary>The program the client says it is, such as <c>Claude Code</c>.</summary>
    internal string Name { get; }

    /// <summary>What the card is headed by: the label the grants table also names it by, else the program.</summary>
    internal string Title { get; }

    internal string Detail { get; }

    internal string Initials { get; }

    internal string Status { get; }

    internal StatusTone StatusTone { get; }

    internal bool IsConnected => StatusTone == StatusTone.Ok;

    internal string LastSeenText { get; }

    internal bool CanSetPolicy { get; }

    internal string Hint { get; }

    /// <summary>What the policy held means beyond its name, or empty.</summary>
    internal string Explanation => ClientPolicies.Describe(_policy) is var described && described.IndexOf(':', StringComparison.Ordinal) is var colon and >= 0
        ? described[(colon + 1)..].Trim() + "."
        : string.Empty;

    internal bool HasExplanation => Explanation.Length > 0;

    /// <summary>Each policy the card's ⋯ menu offers, the one it holds checked.</summary>
    internal IReadOnlyList<PolicyChoice> Choices { get; }

    /// <summary>Whether the card's ⋯ menu is open.</summary>
    internal bool IsMenuOpen
    {
        get => _isMenuOpen;
        set
        {
            if (Set(ref _isMenuOpen, value))
            {
                Raise(nameof(MenuLayer));
            }
        }
    }

    /// <summary>Lifts the card while its menu is open, so the menu draws over the cards after it.</summary>
    internal int MenuLayer => _isMenuOpen ? 1 : 0;

    /// <summary>The policy it is held to; choosing another writes <c>clients.toml</c>.</summary>
    internal ClientPolicy Policy
    {
        get => _policy;
        set
        {
            if (value != _policy && CanSetPolicy)
            {
                _choose?.Invoke(this, value);
            }
        }
    }

    /// <summary>The words of the policy it is held to: <see cref="ClientPolicies.Describe"/> up to its explanation.</summary>
    internal string PolicyText => Words(_policy);

    /// <summary>Shows the policy the file now holds.</summary>
    internal void Held(ClientPolicy policy)
    {
        if (Set(ref _policy, policy, nameof(Policy)))
        {
            Raise(nameof(PolicyText));
            Raise(nameof(Explanation));
            Raise(nameof(HasExplanation));
        }
    }

    internal static string Words(ClientPolicy policy) => ClientPolicies.Describe(policy).Split(':')[0];

    /// <summary>Two lower-case letters: the first of two words, or the first two of one (<c>cursor</c> is <c>cu</c>).</summary>
    private static string InitialsOf(string title)
    {
        var words = title.Split(['-', ' ', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
        var initials = words.Length > 1 ? $"{words[0][0]}{words[1][0]}" : words.FirstOrDefault() ?? string.Empty;
        return new string([.. initials.Take(2)]).ToLowerInvariant();
    }
}

/// <summary>A policy offered for, or chosen on, one client card.</summary>
/// <param name="Row">The card.</param>
/// <param name="Policy">The policy.</param>
internal sealed record PolicyChoice(ClientCardRow Row, ClientPolicy Policy)
{
    internal string Words => ClientCardRow.Words(Policy);

    /// <summary>Whether the card is held to it, which its menu checks.</summary>
    internal bool IsHeld => Row.Policy == Policy;
}
