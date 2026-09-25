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

    internal ClientCardRow(McpClientCard? card, ClientPolicy policy, DateTimeOffset now, Action<ClientCardRow, ClientPolicy>? choose)
    {
        _choose = choose;
        _policy = policy;

        if (card is null)
        {
            Label = ClientPolicies.AnyClient;
            Name = "Every other client";
            Detail = "* · clients without a policy of their own";
            Initials = "*";
            Status = string.Empty;
            StatusTone = StatusTone.Muted;
            LastSeenText = string.Empty;
            CanSetPolicy = true;
            Hint = "Clients started without --client-label are held to this policy.";
            return;
        }

        Label = card.Label;
        Name = card.Name;
        Detail = $"{card.Name} · {card.Kind}";
        Initials = InitialsOf(card.Name);
        Status = card.Status == ClientStatus.Connected ? "Connected" : "Idle";
        StatusTone = card.Status == ClientStatus.Connected ? StatusTone.Ok : StatusTone.Muted;
        LastSeenText = card.Status == ClientStatus.Connected ? "now" : card.LastSeen is { } seen ? UseText.Ago(seen, now) : "never";
        CanSetPolicy = card.Label is not null;
        Hint = CanSetPolicy ? string.Empty : "Start this client's bridge with --client-label to give it its own policy.";
    }

    /// <summary>The label a policy row is keyed by, <c>*</c> for the catch-all card, or null for an unlabeled client.</summary>
    internal string? Label { get; }

    internal string Name { get; }

    internal string Detail { get; }

    internal string Initials { get; }

    internal string Status { get; }

    internal StatusTone StatusTone { get; }

    internal bool IsConnected => StatusTone == StatusTone.Ok;

    internal string LastSeenText { get; }

    internal bool CanSetPolicy { get; }

    internal string Hint { get; }

    /// <summary>The policy's words, as the select lists them.</summary>
    internal static IReadOnlyList<string> PolicyOptions { get; } =
        [.. Enum.GetValues<ClientPolicy>().Select(ClientPolicies.Describe)];

    /// <summary>The same words, for the card's own select.</summary>
    internal IReadOnlyList<string> Options { get; } = PolicyOptions;

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

    /// <summary>The policy's words, for a select bound to <see cref="PolicyOptions"/>.</summary>
    internal string PolicyText
    {
        get => ClientPolicies.Describe(_policy);
        set
        {
            if (Enum.GetValues<ClientPolicy>().FirstOrDefault(policy => ClientPolicies.Describe(policy) == value) is var chosen
                && ClientPolicies.Describe(chosen) == value)
            {
                Policy = chosen;
            }
        }
    }

    /// <summary>Shows the policy the file now holds.</summary>
    internal void Held(ClientPolicy policy)
    {
        if (Set(ref _policy, policy, nameof(Policy)))
        {
            Raise(nameof(PolicyText));
        }
    }

    private static string InitialsOf(string name)
    {
        var words = name.Split(['-', ' ', '_'], StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])));
    }
}

/// <summary>A policy a person chose for one client card.</summary>
/// <param name="Row">The card.</param>
/// <param name="Policy">The policy.</param>
internal sealed record PolicyChoice(ClientCardRow Row, ClientPolicy Policy);
