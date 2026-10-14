using Keypaste.Core.Agents;
using Keypaste.Core.Clients;

namespace Keypaste.App.ViewModels;

/// <summary>
/// One agent or AI tool on the Agents screen: who it is, whether its tool holds keypaste, whether it is
/// in use, a summary of its settings, and its settings and Connect pane when open. Never a value.
/// </summary>
/// <remarks>
/// Names come from what agents say about themselves and from the names their connections were given:
/// display only (THREATS.md T-3). Settings follow the name, so an agent with none is held to Every other
/// agent's and has no Settings of its own (T-36). A row is kept by its key and updated in place, so an
/// open disclosure, its unsaved edits and an open pane survive each refresh.
/// </remarks>
internal sealed class AgentRow : ObservableObject, IDisposable
{
    private readonly Func<string, AgentSettingsViewModel?> _openSettings;
    private McpClientCard _card;
    private string _status = string.Empty;
    private bool _isLive;
    private string _summary = string.Empty;
    private bool _isOpen;
    private AgentSettingsViewModel? _settings;
    private ConnectClientViewModel? _connection;

    /// <param name="card">What Core built for this key.</param>
    /// <param name="now">What "last asked" is measured from.</param>
    /// <param name="openSettings">Opens the settings of the name given, or null when nothing answers agents.</param>
    internal AgentRow(McpClientCard card, DateTimeOffset now, Func<string, AgentSettingsViewModel?> openSettings)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(openSettings);

        _card = card;
        _openSettings = openSettings;
        Key = card.Key;
        IsEveryOther = string.Equals(card.Key, AgentNames.Any, StringComparison.Ordinal);
        HasSettings = IsEveryOther || AgentNames.IsNamed(card.Label);
        Label = HasSettings ? card.Label : null;
        Title = IsEveryOther ? card.Name : card.Tool?.DisplayName ?? card.Label ?? card.Name;
        Initials = IsEveryOther ? "*" : HasSettings ? InitialsOf(Title) : "?";

        DependsOn(nameof(HasSummary), nameof(Summary));
        DependsOn(nameof(HasConnection), nameof(Connection));
        DependsOn(nameof(OffersConnect), nameof(Presence));
        DependsOn(nameof(OffersConnectAgain), nameof(Presence));
        DependsOn(nameof(OffersDisconnect), nameof(Presence));
        DependsOn(nameof(OffersToolActions), nameof(Presence));

        Update(card, now);
    }

    /// <summary>Its label, else <c>name:</c> and the name it gave, or a tool's id, or <c>*</c>.</summary>
    internal string Key { get; }

    /// <summary>The name its settings are kept under, <c>*</c> for Every other agent, or null for an agent with no name.</summary>
    internal string? Label { get; }

    internal bool IsEveryOther { get; }

    /// <summary>A tool's name, else the agent's, as the row is headed.</summary>
    internal string Title { get; }

    internal string Initials { get; }

    /// <summary>What it is now: whether its tool holds keypaste, whether it is in use, or when it last asked.</summary>
    internal string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    /// <summary>Whether its status dot says all is well: its tool holds keypaste, or one of its bridges is attached now.</summary>
    internal bool IsLive
    {
        get => _isLive;
        private set => Set(ref _isLive, value);
    }

    /// <summary>What its settings give it, in a line, or empty.</summary>
    internal string Summary
    {
        get => _summary;
        set => Set(ref _summary, value ?? string.Empty);
    }

    internal bool HasSummary => _summary.Length > 0;

    /// <summary>The catalogue's tool this row is, or null for an agent of no tool.</summary>
    internal McpClient? Tool => _card.Tool;

    /// <summary>Whether a tool found on this computer holds keypaste; null for a row that is no found tool.</summary>
    internal McpToolPresence? Presence => _card.Presence;

    /// <summary>Whether it can have settings of its own: a name they can be kept under, or Every other agent.</summary>
    internal bool HasSettings { get; }

    /// <summary>A found tool without keypaste, or one whose configuration keypaste cannot read, has Connect on its row.</summary>
    internal bool OffersConnect => Tool is not null && Presence?.State is McpToolState.NotConnected or McpToolState.CannotTell;

    /// <summary>A connected tool can be connected again under Settings, for a moved vault or app.</summary>
    internal bool OffersConnectAgain => Tool is not null && Presence?.State is McpToolState.Connected;

    /// <summary>A connected tool, or one keypaste cannot read, can be disconnected under Settings.</summary>
    internal bool OffersDisconnect => Tool is not null && Presence?.State is McpToolState.Connected or McpToolState.CannotTell;

    internal bool OffersToolActions => OffersConnectAgain || OffersDisconnect;

    /// <summary>Whether its Settings disclosure is open; opening it the first time reads its settings.</summary>
    internal bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (!HasSettings || !Set(ref _isOpen, value) || !value || _settings is not null || Label is null)
            {
                return;
            }

            _settings = _openSettings(Label);
            Raise(nameof(Settings));
        }
    }

    /// <summary>Its settings panel, once its disclosure has been opened.</summary>
    internal AgentSettingsViewModel? Settings => _settings;

    /// <summary>The Connect or Disconnect pane open under this row, or null.</summary>
    internal ConnectClientViewModel? Connection
    {
        get => _connection;
        set => Set(ref _connection, value);
    }

    internal bool HasConnection => _connection is not null;

    /// <summary>Takes what Core built for this key again, keeping what is open.</summary>
    internal void Update(McpClientCard card, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(card);

        var presence = !Equals(_card.Presence, card.Presence);
        _card = card;

        if (presence)
        {
            Raise(nameof(Presence));
        }

        var inUse = card.Connections > 0;
        IsLive = inUse || card.Presence?.State == McpToolState.Connected;
        Status = string.Join(" · ", StatusParts(card, now, inUse));
    }

    /// <summary>Re-reads the open settings, after a change made elsewhere on the screen.</summary>
    internal void ReadSettings() => _settings?.Read();

    private IEnumerable<string> StatusParts(McpClientCard card, DateTimeOffset now, bool inUse)
    {
        if (IsEveryOther)
        {
            yield return "Agents without settings of their own";
            yield break;
        }

        if (card.Presence is { } presence)
        {
            yield return presence.State switch
            {
                McpToolState.Connected => "Connected",
                McpToolState.NotConnected => "Not connected",
                McpToolState.CannotTell => $"keypaste can't tell whether {Title} has it: {presence.Reason}",
                _ => presence.Reason ?? $"{Title} can't be connected",
            };
        }

        if (inUse)
        {
            yield return "in use now";
        }
        else if (card.LastSeen is { } seen)
        {
            yield return "last asked " + UseText.Ago(seen, now);
        }
        else if (card.Presence is null)
        {
            yield return "hasn't asked in the last 30 days";
        }

        if (!HasSettings)
        {
            yield return "has no name, so Every other agent's settings apply";
        }
    }

    /// <summary>Two lower-case letters: the first of two words, or the first two of one (<c>cursor</c> is <c>cu</c>).</summary>
    private static string InitialsOf(string title)
    {
        var words = title.Split(['-', ' ', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
        var initials = words.Length > 1 ? $"{words[0][0]}{words[1][0]}" : words.FirstOrDefault() ?? string.Empty;
        return new string([.. initials.Take(2)]).ToLowerInvariant();
    }

    public void Dispose()
    {
        _settings?.Dispose();
        _connection?.Dispose();
    }
}
