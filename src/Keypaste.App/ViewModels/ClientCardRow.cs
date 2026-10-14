using Keypaste.Core.Agents;
using Keypaste.Core.Clients;

namespace Keypaste.App.ViewModels;

/// <summary>One MCP client card on the Agents screen: who, whether attached now, and whether its settings are open. Never a value.</summary>
/// <remarks>
/// Names come from what clients say about themselves and from the labels in their configuration:
/// display only (THREATS.md T-3). Settings follow the label, so a client without one is held to
/// <c>*</c>'s and cannot be given its own; the <c>*</c> card is where every other agent's belong (T-36).
/// </remarks>
internal sealed class ClientCardRow : ObservableObject
{
    private bool _isSettingsOpen;

    internal ClientCardRow(McpClientCard? card, DateTimeOffset now)
    {
        DependsOn(nameof(SettingsIcon), nameof(IsSettingsOpen));

        if (card is null)
        {
            Label = AgentNames.Any;
            Name = "Every other client";
            Title = Name;
            Detail = "Clients without settings of their own";
            Initials = "*";
            Status = string.Empty;
            StatusTone = StatusTone.Muted;
            LastSeenText = string.Empty;
            HasSettings = true;
            // A label lives in the client's own configuration, which an agent may be able to edit.
            Hint = "Unlabeled clients and labels without settings of their own get these, so keep them the strictest.";
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
        // A label an older bridge wrote to the log may be one no settings can be kept under.
        HasSettings = AgentNames.IsNamed(card.Label);
        Hint = HasSettings ? string.Empty : "Start this client's bridge with --client-label to give it settings of its own.";
    }

    /// <summary>The name its settings are kept under, <c>*</c> for the catch-all card, or null for an unlabeled client.</summary>
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

    /// <summary>Whether it can have settings of its own: a label one can be kept under, or the <c>*</c> card.</summary>
    internal bool HasSettings { get; }

    internal string Hint { get; }

    internal bool HasHint => Hint.Length > 0;

    /// <summary>Whether its settings are open below the cards.</summary>
    internal bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set => Set(ref _isSettingsOpen, value);
    }

    /// <summary>The disclosure's chevron.</summary>
    internal string SettingsIcon => _isSettingsOpen ? "chevron-down" : "chevron-right";

    /// <summary>Two lower-case letters: the first of two words, or the first two of one (<c>cursor</c> is <c>cu</c>).</summary>
    private static string InitialsOf(string title)
    {
        var words = title.Split(['-', ' ', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
        var initials = words.Length > 1 ? $"{words[0][0]}{words[1][0]}" : words.FirstOrDefault() ?? string.Empty;
        return new string([.. initials.Take(2)]).ToLowerInvariant();
    }
}
