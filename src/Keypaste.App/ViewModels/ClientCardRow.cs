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

    /// <summary>The policy's words, as the select lists them: <see cref="ClientPolicies.Describe"/> up to its explanation.</summary>
    internal static IReadOnlyList<string> PolicyOptions { get; } =
        [.. Enum.GetValues<ClientPolicy>().Select(Words)];

    /// <summary>What the policy held means beyond its name, or empty.</summary>
    internal string Explanation => ClientPolicies.Describe(_policy) is var described && described.IndexOf(':', StringComparison.Ordinal) is var colon and >= 0
        ? described[(colon + 1)..].Trim() + "."
        : string.Empty;

    internal bool HasExplanation => Explanation.Length > 0;

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
        get => Words(_policy);
        set
        {
            if (Enum.GetValues<ClientPolicy>().FirstOrDefault(policy => Words(policy) == value) is var chosen
                && Words(chosen) == value)
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
            Raise(nameof(Explanation));
            Raise(nameof(HasExplanation));
        }
    }

    private static string Words(ClientPolicy policy) => ClientPolicies.Describe(policy).Split(':')[0];

    /// <summary>Two lower-case letters: the first of two words, or the first two of one (<c>cursor</c> is <c>cu</c>).</summary>
    private static string InitialsOf(string title)
    {
        var words = title.Split(['-', ' ', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
        var initials = words.Length > 1 ? $"{words[0][0]}{words[1][0]}" : words.FirstOrDefault() ?? string.Empty;
        return new string([.. initials.Take(2)]).ToLowerInvariant();
    }
}

/// <summary>A policy a person chose for one client card.</summary>
/// <param name="Row">The card.</param>
/// <param name="Policy">The policy.</param>
internal sealed record PolicyChoice(ClientCardRow Row, ClientPolicy Policy);
